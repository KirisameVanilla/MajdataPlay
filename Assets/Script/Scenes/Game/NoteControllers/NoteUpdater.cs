﻿using MajdataPlay.Game.Types;
using MajdataPlay.Interfaces;
using MajdataPlay.Types;
using MajdataPlay.Attributes;
using MajdataPlay.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MajdataPlay.Game
{
    public class NoteUpdater : MonoBehaviour
    {
        public double UpdateElapsedMs => _updateElapsedMs;
        public double FixedUpdateElapsedMs => _fixedUpdateElapsedMs;
        public double LateUpdateElapsedMs => _lateUpdateElapsedMs;

        NoteInfo[] _updatableComponents = Array.Empty<NoteInfo>();
        NoteInfo[] _fixedUpdatableComponents = Array.Empty<NoteInfo>();
        NoteInfo[] _lateUpdatableComponents = Array.Empty<NoteInfo>();

        [ReadOnlyField]
        [SerializeField]
        double _updateElapsedMs = 0;
        [ReadOnlyField]
        [SerializeField]
        double _fixedUpdateElapsedMs = 0;
        [ReadOnlyField]
        [SerializeField]
        double _lateUpdateElapsedMs = 0;
        public void Initialize()
        {
            Transform[] childs = new Transform[transform.childCount];
            List<NoteInfo> updatableComponents = new();
            List<NoteInfo> fixedUpdatableComponents = new();
            List<NoteInfo> lateUpdatableComponents = new();
            for (int i = 0; i < childs.Length; i++)
                childs[i] = transform.GetChild(i);
            foreach(var child in childs)
            {
                var childComponents = child.GetComponents<IStateful<NoteStatus>>();
                if(childComponents.Length != 0)
                {
                    foreach(var component in childComponents)
                    {
                        var noteInfo = new NoteInfo(component);
                        if(noteInfo.IsValid)
                        {
                            if(noteInfo.IsUpdatable)
                                updatableComponents.Add(noteInfo);
                            if(noteInfo.IsFixedUpdatable)
                                fixedUpdatableComponents.Add(noteInfo);
                            if (noteInfo.IsLateUpdatable)
                                lateUpdatableComponents.Add(noteInfo);
                        }
                    }
                }
            }
            _updatableComponents = updatableComponents.ToArray();
            _fixedUpdatableComponents = fixedUpdatableComponents.ToArray();
            _lateUpdatableComponents = lateUpdatableComponents.ToArray();
        }

        internal virtual void OnUpdate()
        {
            var start = MajTimeline.UnscaledTime;
            foreach (var component in ArrayHelper.ToEnumerable(_updatableComponents))
            {
                try
                {
                    if (component.IsExecutable())
                        component.OnUpdate();
                    else
                        continue;
                }
                catch (Exception e)
                {
                    MajDebug.LogException(e);
                }
            }
            var end = MajTimeline.UnscaledTime;
            var timeSpan = end - start;
            _updateElapsedMs = timeSpan.TotalMilliseconds;
        }
        internal virtual void OnFixedUpdate()
        {
            var start = MajTimeline.UnscaledTime;
            foreach (var component in ArrayHelper.ToEnumerable(_fixedUpdatableComponents))
            {
                try
                {
                    if (component.IsExecutable())
                        component.OnFixedUpdate();
                    else
                        continue;
                }
                catch (Exception e)
                {
                    MajDebug.LogException(e);
                }
            }
            var end = MajTimeline.UnscaledTime;
            var timeSpan = end - start;
            _fixedUpdateElapsedMs = timeSpan.TotalMilliseconds;
        }
        internal virtual void OnLateUpdate()
        {
            var start = MajTimeline.UnscaledTime;
            foreach (var component in ArrayHelper.ToEnumerable(_lateUpdatableComponents))
            {
                try
                {
                    if (component.IsExecutable())
                        component.OnLateUpdate();
                    else
                        continue;
                }
                catch (Exception e)
                {
                    MajDebug.LogException(e);
                }
            }
            var end = MajTimeline.UnscaledTime;
            var timeSpan = end - start;
            _lateUpdateElapsedMs = timeSpan.TotalMilliseconds;
        }
    }
}
