﻿using MajdataPlay.Extensions;
using MajdataPlay.Types;
using MajdataPlay.Utils;
using MychIO.Device;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
#nullable enable
namespace MajdataPlay.IO
{
    internal partial class InputManager : MonoBehaviour
    {
        void StartUpdatingKeyboardState()
        {
            if (!_buttonRingUpdateTask.IsCompleted)
                return;
            _buttonRingUpdateTask = Task.Factory.StartNew(() =>
            {
                var token = MajEnv.GlobalCT;
                var pollingRate = _btnPollingRateMs;
                var stopwatch = new Stopwatch();
                var t1 = stopwatch.Elapsed;

                Thread.CurrentThread.Priority = System.Threading.ThreadPriority.BelowNormal;
                stopwatch.Start();
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var now = DateTime.Now;
                        var buttons = _buttons.Span;
                        for (var i = 0; i < buttons.Length; i++)
                        {
                            var button = buttons[i];
                            var keyCode = button.BindingKey;

                            _buttonRingInputBuffer.Enqueue(new ()
                            {
                                Index = i,
                                State = Keyboard.IsKeyDown(keyCode) ? SensorStatus.On : SensorStatus.Off,
                                Timestamp = now
                            });
                        }
                    }
                    catch (Exception e)
                    {
                        MajDebug.LogError($"From KeyBoard listener: \n{e}");
                    }
                    finally
                    {
                        var t2 = stopwatch.Elapsed;
                        var elapsed = t2 - t1;
                        t1 = t2;
                        if (elapsed < pollingRate)
                            Thread.Sleep(pollingRate - elapsed);
                    }
                }
            }, TaskCreationOptions.LongRunning);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void UpdateButtonState()
        {
            var buttons = _buttons.Span;
            while (_buttonRingInputBuffer.TryDequeue(out var report))
            {
                if (!report.Index.InRange(0, 11))
                    continue;
                var button = buttons[report.Index];
                var oldState = button.State;
                var newState = report.State;
                var timestamp = report.Timestamp;

                if (oldState == newState)
                    continue;
                else if (_isBtnDebounceEnabled)
                {
                    if (JitterDetect(button.Area, timestamp, true))
                        continue;
                    _btnLastTriggerTimes[button.Area] = timestamp;
                }
                button.State = newState;
                MajDebug.Log($"Key \"{button.BindingKey}\": {newState}");
                var msg = new InputEventArgs()
                {
                    Type = button.Area,
                    OldStatus = oldState,
                    Status = newState,
                    IsButton = true
                };
                button.PushEvent(msg);
                PushEvent(msg);
            }
        }
        public void BindButton(EventHandler<InputEventArgs> checker, SensorArea sType)
        {
            var button = GetButton(sType);
            if (button == null)
                throw new Exception($"{sType} Button not found.");
            button.AddSubscriber(checker);
        }
        public void UnbindButton(EventHandler<InputEventArgs> checker, SensorArea sType)
        {
            var button = GetButton(sType);
            if (button == null)
                throw new Exception($"{sType} Button not found.");
            button.RemoveSubscriber(checker);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int GetIndexByButtonRingZone(ButtonRingZone btnZone)
        {
            return btnZone switch
            {
                ButtonRingZone.BA1 => 0,
                ButtonRingZone.BA2 => 1,
                ButtonRingZone.BA3 => 2,
                ButtonRingZone.BA4 => 3,
                ButtonRingZone.BA5 => 4,
                ButtonRingZone.BA6 => 5,
                ButtonRingZone.BA7 => 6,
                ButtonRingZone.BA8 => 7,
                ButtonRingZone.ArrowUp => 9,
                ButtonRingZone.ArrowDown => 11,
                ButtonRingZone.Select => 8,
                ButtonRingZone.InsertCoin => 10,
                _ => throw new ArgumentOutOfRangeException("Does your 8-key game have 9 keys?")
            };
        }
    }
}
