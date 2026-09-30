using MajdataPlay.Diagnostics;
using MajdataPlay.Net.Curl.Core.PInvoke;
using MajdataPlay.Net.Curl.Lifecycle;
using MajdataPlay.Net.Curl.Utils;
using MajdataPlay.UnsafeKit;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Net.Curl.Core
{
    public class CurlMulti : CurlHandle, IAsyncDisposable
    {
        readonly Task _workerThread;
        readonly CancellationTokenSource _cts = new();
        readonly HashSet<CurlTask> _activeTasks = new();
        readonly object _lifecycleLock = new();
        bool _isStopping;
        Task? _disposeTask;

        readonly ConcurrentQueue<CurlTask> _pendingToSubmitTasks = new();
        readonly ConcurrentQueue<CurlTask> _pendingToCancelTasks = new();
        readonly ConcurrentQueue<CurlTask> _pendingToResumeTasks = new();
        readonly ConcurrentQueue<CurlTask> _pendingToDisposeTasks = new();

        readonly Action<CurlTask> _onResumeRequested;
        readonly Action<CurlTask> _onDisposeRequested;

        internal CurlMulti() 
        {
            LibCurlLifecycle.Retain();
            ThisHandle = LibCurl.Multi.Init();
            _onResumeRequested = OnTaskResume;
            _onDisposeRequested = OnTaskDispose;
            _workerThread = Task.Factory.StartNew(Run, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public Task<CurlResponse> AddToQueue(HttpRequestMessage request, Stream? contentStream, CurlHttpConfig config, CancellationToken token = default)
        {
            lock (_lifecycleLock)
            {
                ThrowIfStopping();
                var curlEasy = new CurlEasy();
                var curlRequest = new CurlRequest(request, contentStream);
                var curlTask = new CurlTask(curlEasy, curlRequest, config, _onResumeRequested, _onDisposeRequested, token);

                curlRequest.ApplyTo(curlEasy);
                config.ApplyToEasy(curlEasy, curlRequest.RequestUri);

                _pendingToSubmitTasks.Enqueue(curlTask);
            
                token.Register(() =>
                {
                    lock (_lifecycleLock)
                    {
                        if (!_isStopping)
                        {
                            _pendingToCancelTasks.Enqueue(curlTask);
                            WakeUp();
                        }
                    }
                });

                WakeUp();

                return curlTask.Task;
            }
        }

        void OnTaskResume(CurlTask task)
        {
            lock (_lifecycleLock)
            {
                if (!_isStopping)
                {
                    _pendingToResumeTasks.Enqueue(task);
                    WakeUp();
                }
            }
        }
        void OnTaskDispose(CurlTask task)
        {
            lock (_lifecycleLock)
            {
                if (ThisHandle == IntPtr.Zero)
                {
                    // A response may outlive its HTTP client and worker.
                    task.Response.CleanUp();
                }
                else
                {
                    _pendingToDisposeTasks.Enqueue(task);
                    WakeUp();
                }
            }
        }
        void Run()
        {
            var thisToken = _cts.Token;
            Thread.CurrentThread.Name = "Curl multi worker";
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;
            var disposedEx = new ObjectDisposedException(nameof(CurlEasy));
            while (!thisToken.IsCancellationRequested)
            {
                try
                {
                    var returnCode = default(CurlCode?);
                    var multiReturnCode = default(CurlMCode?);
                    while (_pendingToSubmitTasks.TryDequeue(out var pendingTask))
                    {
                        if (pendingTask.CancellationToken.IsCancellationRequested)
                        {
                            pendingTask.TryEnterCancelledState();
                            pendingTask.Response.CleanUp();
                            continue;
                        }
                        if (pendingTask.TryEnterSubmittedState())
                        {
                            pendingTask.Config.ApplyToMulti(this);
                            _activeTasks.Add(pendingTask);
                            multiReturnCode = LibCurl.Multi.AddEasyHandle(ThisHandle, pendingTask.Easy.Handle);
                        }
                    }

                    while (_pendingToCancelTasks.TryDequeue(out var cancelTask))
                    {
                        if (cancelTask.TryEnterCancelledState())
                        {
                            if (_activeTasks.Remove(cancelTask))
                            {
                                multiReturnCode = LibCurl.Multi.RemoveEasyHandle(ThisHandle, cancelTask.Easy.Handle);
                            }
                        }
                    }

                    while (_pendingToResumeTasks.TryDequeue(out var pausedTask))
                    {
                        returnCode = pausedTask.Easy.Pause(CurlPauseAction.None);
                    }

                    while (_pendingToDisposeTasks.TryDequeue(out var disposeTask))
                    {
                        if(_activeTasks.Remove(disposeTask))
                        {
                            LibCurl.Multi.RemoveEasyHandle(ThisHandle, disposeTask.Easy.Handle);
                        }
                        disposeTask.TryFail(disposedEx);
                        disposeTask.Response.CleanUp();
                    }

                    if (thisToken.IsCancellationRequested)
                    {
                        return;
                    }

                    multiReturnCode = LibCurl.Multi.Perform(ThisHandle, out var runningHandles);

                    if (thisToken.IsCancellationRequested)
                    {
                        return;
                    }
                    while (LibCurl.Multi.GetMessage(ThisHandle, out var remaining) is CurlMsg multiMsg)
                    {
                        if (multiMsg.Code == CurlMsgCode.Done)
                        {
                            CompleteTask(multiMsg);
                        }
                    }

                    multiReturnCode = LibCurl.Multi.Poll(ThisHandle, IntPtr.Zero, 0, 1000, out _);
                }
                catch (Exception e)
                {
                    MajDebug.LogError($"[libcurl][Multi worker]{e}");
                    // Shutdown must interrupt the retry delay as well as curl_multi_poll.
                    thisToken.WaitHandle.WaitOne(2500);
                }
            }            
        }
        void WakeUp()
        {
            LibCurl.Multi.Wakeup(ThisHandle);
        }

        void CompleteTask(CurlMsg multiMsg)
        {
            var easyHandle = multiMsg.EasyHandle;
            LibCurl.Easy.GetInfo(easyHandle, CurlInfo.Private, out IntPtr privatePtr);
            if(UnsafeHelper.TryGetInstanceFromGCHandle<CurlTask>(privatePtr, out var curlTask))
            {
                if(_activeTasks.Remove(curlTask))
                {
                    var curlErr = CurlUtility.GetEasyException(easyHandle, multiMsg.Data.Result);
                    if (curlErr is not null)
                    {
                        curlTask.TryFail(curlErr);
                    }
                    else
                    {
                        curlTask.TryEnterHeaderReadState();
                        curlTask.TryEnterCompletedState(multiMsg.Data.Result);
                    }
                    LibCurl.Easy.SetOption(easyHandle, CurlOption.Private, IntPtr.Zero);
                    LibCurl.Multi.RemoveEasyHandle(ThisHandle, easyHandle);
                }
            }            
        }

        public override void Dispose()
        {
            BeginDispose().GetAwaiter().GetResult();
        }
        public ValueTask DisposeAsync()
        {
            return new ValueTask(BeginDispose());
        }
        Task BeginDispose()
        {
            lock (_lifecycleLock)
            {
                if (_disposeTask is not null)
                {
                    return _disposeTask;
                }
                _isStopping = true;
                _cts.Cancel();
                // Keep the native handle valid until the worker has stopped using it.
                WakeUp();
                _disposeTask = DisposeCoreAsync(ThisHandle);
                return _disposeTask;
            }
        }
        async Task DisposeCoreAsync(IntPtr handle)
        {
            try
            {
                await _workerThread.ConfigureAwait(false);
            }
            finally
            {
                lock (_lifecycleLock)
                {
                    ThisHandle = IntPtr.Zero;
                    try
                    {
                        CleanUp(handle);
                    }
                    finally
                    {
                        _cts.Dispose();
                        GC.SuppressFinalize(this);
                    }
                }
            }
        }
        void ThrowIfStopping()
        {
            if (_isStopping)
            {
                throw new ObjectDisposedException(nameof(CurlMulti));
            }
            ThrowIfDisposed();
        }
        void CleanUp(IntPtr handle)
        {
            var disposedEx = new ObjectDisposedException(nameof(CurlEasy));
            var tasksToDispose = new HashSet<CurlTask>(_activeTasks);
            while (_pendingToSubmitTasks.TryDequeue(out var task))
            {
                tasksToDispose.Add(task);
            }
            while (_pendingToDisposeTasks.TryDequeue(out var task))
            {
                tasksToDispose.Add(task);
            }
            try
            {
                foreach (var task in tasksToDispose)
                {
                    try
                    {
                        if (_activeTasks.Contains(task))
                        {
                            LibCurl.Multi.RemoveEasyHandle(handle, task.Easy.Handle);
                        }
                        task.TryFail(disposedEx);
                        task.Response.CleanUp();
                    }
                    catch (Exception exception)
                    {
                        MajDebug.LogException(exception);
                    }
                }
            }
            finally
            {
                _activeTasks.Clear();
                while (_pendingToCancelTasks.TryDequeue(out _)) { }
                while (_pendingToResumeTasks.TryDequeue(out _)) { }
                LibCurl.Multi.CleanUp(handle);
                LibCurlLifecycle.Release();
            }
        }
    }    
}
