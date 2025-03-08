﻿using MajdataPlay.Extensions;
using MajdataPlay.Game.Buffers;
using MajdataPlay.Game.Controllers;
using MajdataPlay.Game.Types;
using MajdataPlay.Game.Utils;
using MajdataPlay.IO;
using MajdataPlay.Types;
using MajdataPlay.Utils;
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
#nullable enable
namespace MajdataPlay.Game.Notes
{
    internal sealed class TouchHoldDrop : NoteLongDrop, INoteQueueMember<TouchQueueInfo>, IRendererContainer,IPoolableNote<TouchHoldPoolingInfo, TouchQueueInfo>, IMajComponent
    {
        public TouchGroup? GroupInfo { get; set; } = null;
        public TouchQueueInfo QueueInfo { get; set; } = TouchQueueInfo.Default;
        public RendererStatus RendererState
        {
            get => _rendererState;
            set
            {
                if (State < NoteStatus.Initialized)
                    return;

                switch (value)
                {
                    case RendererStatus.Off:
                        foreach (var renderer in _fanRenderers)
                            renderer.forceRenderingOff = true;
                        _borderRenderer.forceRenderingOff = true;
                        _borderMask.forceRenderingOff = true;
                        break;
                    case RendererStatus.On:
                        foreach (var renderer in _fanRenderers)
                            renderer.forceRenderingOff = false;
                        _borderRenderer.forceRenderingOff = false;
                        _borderMask.forceRenderingOff = false;
                        break;
                    default:
                        return;
                }
                _rendererState = value;
            }
        }
        public char areaPosition;
        public bool isFirework;

        Sprite board_On;
        Sprite board_Off;

        readonly GameObject[] _fans = new GameObject[4];
        readonly Transform[] _fanTransforms = new Transform[4];
        readonly SpriteRenderer[] _fanRenderers = new SpriteRenderer[4];

        float displayDuration;
        float moveDuration;
        float wholeDuration;

        GameObject _pointObject;
        GameObject _borderObject;
        SpriteMask _borderMask;
        SpriteRenderer _pointRenderer;
        SpriteRenderer _borderRenderer;
        NotePoolManager _notePoolManager;

        bool? _lastHoldState = null;
        float _releaseTime = 0;
        Range<float> _bodyCheckRange;
        readonly float _touchPanelOffset = MajEnv.UserSetting?.Judge.TouchPanelOffset ?? 0;

        const int _fanSpriteSortOrder = 2;
        const int _borderSortOrder = 6;
        const int _pointBorderSortOrder = 1;

        readonly static Range<float> DEFAULT_BODY_CHECK_RANGE = new Range<float>(float.MinValue, float.MinValue, ContainsType.Closed);
        protected override void Awake()
        {
            base.Awake();
            _notePoolManager = FindObjectOfType<NotePoolManager>();

            _fanTransforms[0] = Transform.GetChild(5);
            _fanTransforms[1] = Transform.GetChild(4);
            _fanTransforms[2] = Transform.GetChild(3);
            _fanTransforms[3] = Transform.GetChild(2);

            _fans[0] = _fanTransforms[0].gameObject;
            _fans[1] = _fanTransforms[1].gameObject;
            _fans[2] = _fanTransforms[2].gameObject;
            _fans[3] = _fanTransforms[3].gameObject;

            for (var i = 0; i < 4; i++)
            {
                _fanRenderers[i] = _fans[i].GetComponent<SpriteRenderer>();
            }

            _pointObject = transform.GetChild(6).gameObject;
            _borderObject = transform.GetChild(1).gameObject;
            _pointRenderer = _pointObject.GetComponent<SpriteRenderer>();
            _borderRenderer = _borderObject.GetComponent<SpriteRenderer>();
            _borderMask = Transform.GetChild(0).GetComponent<SpriteMask>();

            _pointObject.SetActive(true);
            _borderObject.SetActive(true);

            Transform.position = new Vector3(0, 0, 0);
            SetFansColor(new Color(1f, 1f, 1f, 0f));
            SetFansPosition(0.4f);

            base.SetActive(false);
            SetFanActive(false);
            SetBorderActive(false);
            SetPointActive(false);
            Active = false;

            if (!IsAutoplay)
                _noteManager.OnGameIOUpdate += GameIOListener;

            RendererState = RendererStatus.Off;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected override void Autoplay()
        {
            if (_isJudged || !IsAutoplay)
                return;
            if (GetTimeSpanToJudgeTiming() >= -0.016667f)
            {
                var autoplayGrade = AutoplayGrade;
                if (((int)autoplayGrade).InRange(0, 14))
                    _judgeResult = autoplayGrade;
                else
                    _judgeResult = (JudgeGrade)_randomizer.Next(0, 15);
                ConvertJudgeGrade(ref _judgeResult);
                _isJudged = true;
                _judgeDiff = _judgeResult switch
                {
                    < JudgeGrade.Perfect => 1,
                    > JudgeGrade.Perfect => -1,
                    _ => 0
                };
                PlayJudgeSFX(new JudgeResult()
                {
                    Grade = _judgeResult,
                    IsBreak = IsBreak,
                    IsEX = IsEX,
                    Diff = _judgeDiff
                });
                PlayHoldEffect();
            }
        }
        public void Initialize(TouchHoldPoolingInfo poolingInfo)
        {
            if (State >= NoteStatus.Initialized && State < NoteStatus.End)
                return;

            StartPos = poolingInfo.StartPos;
            areaPosition = poolingInfo.AreaPos;
            Timing = poolingInfo.Timing;
            _judgeTiming = Timing;
            SortOrder = poolingInfo.NoteSortOrder;
            Speed = poolingInfo.Speed;
            IsEach = poolingInfo.IsEach;
            IsBreak = poolingInfo.IsBreak;
            IsEX = poolingInfo.IsEX;
            QueueInfo = poolingInfo.QueueInfo;
            GroupInfo = poolingInfo.GroupInfo;
            _isJudged = false;
            _lastHoldState = null;
            Length = poolingInfo.LastFor;
            isFirework = poolingInfo.IsFirework;
            _sensorPos = poolingInfo.SensorPos;
            _playerReleaseTime = 0;
            _judgableRange = new(JudgeTiming - 0.15f, JudgeTiming + 0.316667f, ContainsType.Closed);
            _releaseTime = 0;

            if (Length < TOUCHHOLD_HEAD_IGNORE_LENGTH_SEC + TOUCHHOLD_TAIL_IGNORE_LENGTH_SEC)
            {
                _bodyCheckRange = DEFAULT_BODY_CHECK_RANGE;
            }
            else
            {
                _bodyCheckRange = new Range<float>(Timing + TOUCHHOLD_HEAD_IGNORE_LENGTH_SEC, (Timing + Length) - TOUCHHOLD_TAIL_IGNORE_LENGTH_SEC, ContainsType.Closed);
            }

            wholeDuration = 3.209385682f * Mathf.Pow(Speed, -0.9549621752f);
            moveDuration = 0.8f * wholeDuration;
            displayDuration = 0.2f * wholeDuration;

            LoadSkin();

            SetFansColor(new Color(1f, 1f, 1f, 0f));
            _borderMask.enabled = false;
            _borderMask.alphaCutoff = 0;
            SetActive(true);
            SetFanActive(false);
            SetBorderActive(false);
            SetPointActive(false);

            Transform.position = NoteHelper.GetTouchAreaPosition(_sensorPos);
            SetFansPosition(0.4f);
            RendererState = RendererStatus.Off;

            for (var i = 0; i < 4; i++)
                _fanRenderers[i].sortingOrder = SortOrder - (_fanSpriteSortOrder + i);
            _pointRenderer.sortingOrder = SortOrder - _pointBorderSortOrder;
            _borderRenderer.sortingOrder = SortOrder - _borderSortOrder;
            _borderMask.frontSortingOrder = SortOrder - _borderSortOrder;
            _borderMask.backSortingOrder = SortOrder - _borderSortOrder - 1;

            State = NoteStatus.Initialized;
        }
        public void End()
        {
            if (IsEnded)
                return;

            State = NoteStatus.End;

            _judgeResult = EndJudge(_judgeResult);
            ConvertJudgeGrade(ref _judgeResult);
            var result = new JudgeResult()
            {
                Grade = _judgeResult,
                IsBreak = IsBreak,
                IsEX = IsEX,
                Diff = _judgeDiff,
            };
            //_pointObject.SetActive(false);
            SetActive(false);
            RendererState = RendererStatus.Off;

            _objectCounter.ReportResult(this, result);
            if (!_isJudged)
                _noteManager.NextTouch(QueueInfo);
            if (isFirework && !result.IsMissOrTooFast)
                _effectManager.PlayFireworkEffect(transform.position);

            PlayJudgeSFX(new JudgeResult()
            {
                Grade = _judgeResult,
                IsBreak = false,
                IsEX = false,
                Diff = _judgeDiff
            });
            _lastHoldState = false;
            _audioEffMana.StopTouchHoldSound();
            _effectManager.PlayTouchHoldEffect(_sensorPos, result);
            _effectManager.ResetHoldEffect(_sensorPos);
            _notePoolManager.Collect(this);
        }

        protected override void LoadSkin()
        {
            var skin = MajInstances.SkinManager.GetTouchHoldSkin();

            SetFansMaterial(DefaultMaterial);
            if(IsBreak)
            {
                for (var i = 0; i < 4; i++)
                    _fanRenderers[i].sprite = skin.Fans_Break[i];
                _borderRenderer.sprite = skin.Boader_Break; // TouchHold Border
                _pointRenderer.sprite = skin.Point_Break;
                board_On = skin.Boader_Break;
                SetFansMaterial(BreakMaterial);
            }
            else
            {
                for (var i = 0; i < 4; i++)
                    _fanRenderers[i].sprite = skin.Fans[i];
                _borderRenderer.sprite = skin.Boader; // TouchHold Border
                _pointRenderer.sprite = skin.Point;
                board_On = skin.Boader;
            }
            board_Off = skin.Off;
        }
        protected override void Judge(float currentSec)
        {
            if (_isJudged)
                return;

            var diffSec = currentSec - JudgeTiming;
            var isFast = diffSec < 0;
            _judgeDiff = diffSec * 1000;
            var diffMSec = MathF.Abs(diffSec * 1000);

            if (isFast && diffMSec > TOUCH_JUDGE_SEG_1ST_PERFECT_MSEC)
                return;

            var result = diffMSec switch
            {
                <= TOUCH_JUDGE_SEG_1ST_PERFECT_MSEC => JudgeGrade.Perfect,
                <= TOUCH_JUDGE_SEG_2ND_PERFECT_MSEC => JudgeGrade.LatePerfect2nd,
                <= TOUCH_JUDGE_SEG_3RD_PERFECT_MSEC => JudgeGrade.LatePerfect3rd,
                <= TOUCH_JUDGE_SEG_1ST_GREAT_MSEC => JudgeGrade.LateGreat,
                <= TOUCH_JUDGE_SEG_2ND_GREAT_MSEC => JudgeGrade.LateGreat2nd,
                <= TOUCH_JUDGE_SEG_3RD_GREAT_MSEC => JudgeGrade.LateGreat3rd,
                <= TOUCH_JUDGE_GOOD_AREA_MSEC => JudgeGrade.LateGood,
                _ => isFast ? JudgeGrade.TooFast : JudgeGrade.Miss
            };

            ConvertJudgeGrade(ref result);
            _judgeResult = result;
            _isJudged = true;
            PlayHoldEffect();
        }
        void OnUpdate()
        {
            var timing = GetTimeSpanToArriveTiming();

            Autoplay();
            TooLateCheck();
            BodyCheck();

            switch(State)
            {
                case NoteStatus.Initialized:
                    if (-timing < wholeDuration)
                    {
                        SetPointActive(true);
                        SetFanActive(true);
                        RendererState = RendererStatus.On;
                        State = NoteStatus.Scaling;
                        goto case NoteStatus.Scaling;
                    }
                    return;
                case NoteStatus.Scaling:
                    {
                        var newColor = Color.white;
                        if (-timing < moveDuration)
                        {
                            SetFansColor(Color.white);
                            State = NoteStatus.Running;
                            goto case NoteStatus.Running;
                        }
                        var alpha = ((wholeDuration + timing) / displayDuration).Clamp(0, 1);
                        newColor.a = alpha;
                        SetFansColor(newColor);
                    }
                    return;
                case NoteStatus.Running:
                    {
                        var pow = -Mathf.Exp(8 * (timing * 0.43f / moveDuration) - 0.85f) + 0.42f;
                        var distance = Mathf.Clamp(pow, 0f, 0.4f);
                        if (float.IsNaN(distance))
                            distance = 0f;
                        if (timing >= 0)
                        {
                            var _pow = -Mathf.Exp(-0.85f) + 0.42f;
                            var _distance = Mathf.Clamp(_pow, 0f, 0.4f);
                            SetFansPosition(_distance);
                            SetBorderActive(true);
                            _borderMask.enabled = true;
                            State = NoteStatus.Arrived;
                            goto case NoteStatus.Arrived;
                        }
                        else
                            SetFansPosition(distance);
                    }
                    return;
                case NoteStatus.Arrived:
                    {
                        var value = 0.91f * (1 - (Length - timing) / Length);
                        var alpha = value.Clamp(0, 1f);
                        _borderMask.alphaCutoff = alpha;
                    }
                    return;
            }   
        }
        void GameIOListener(GameInputEventArgs args)
        {
            if (_isJudged || IsEnded)
                return;
            else if (args.IsButton)
                return;
            else if (args.Area != _sensorPos)
                return;
            else if (!args.IsClick)
                return;
            else if (!_judgableRange.InRange(ThisFrameSec))
                return;
            else if (!_noteManager.IsCurrentNoteJudgeable(QueueInfo))
                return;

            ref var isUsed = ref args.IsUsed.Target;

            if (isUsed)
                return;

            Judge(ThisFrameSec - _touchPanelOffset);

            if (_isJudged)
            {
                isUsed = true;
                _noteManager.NextTouch(QueueInfo);
                RegisterGrade();
            }
        }
        void RegisterGrade()
        {
            if (GroupInfo is not null && !_judgeResult.IsMissOrTooFast())
            {
                GroupInfo.JudgeResult = _judgeResult;
                GroupInfo.JudgeDiff = _judgeDiff;
                GroupInfo.RegisterResult(_judgeResult);
            }
        }
        void TooLateCheck()
        {
            // Too late check
            if (IsEnded || _isJudged)
                return;

            var timing = GetTimeSpanToJudgeTiming();
            var isTooLate = timing > 0.316667f;

            if (!isTooLate)
            {
                if (GroupInfo is not null)
                {
                    if (GroupInfo.Percent > 0.5f && GroupInfo.JudgeResult != null)
                    {
                        _isJudged = true;
                        _judgeResult = (JudgeGrade)GroupInfo.JudgeResult;
                        _judgeDiff = GroupInfo.JudgeDiff;
                        _noteManager.NextTouch(QueueInfo);
                    }
                }
            }
            else
            {
                _judgeResult = JudgeGrade.Miss;
                _isJudged = true;
                _judgeDiff = 316.667f;
                _noteManager.NextTouch(QueueInfo);
            }
        }
        void BodyCheck()
        {
            if (!_isJudged || IsEnded)
                return;

            var remainingTime = GetRemainingTime();

            if (remainingTime == 0)
            {
                End();
                return;
            }
            else if (!_bodyCheckRange.InRange(ThisFrameSec) || !NoteController.IsStart)
            {
                return;
            }

            var on = _ioManager.CheckSensorStatus(_sensorPos, SensorStatus.On);
            if (on || IsAutoplay)
            {
                PlayHoldEffect();
                _releaseTime = 0;
                _lastHoldState = true;
            }
            else
            {
                if (_releaseTime <= DELUXE_HOLD_RELEASE_IGNORE_TIME_SEC)
                {
                    _releaseTime += Time.deltaTime;
                    return;
                }
                _playerReleaseTime += Time.deltaTime;
                StopHoldEffect();
                _lastHoldState = false;
            }
        }
        public override void SetActive(bool state)
        {
            if (Active == state)
                return;
            base.SetActive(state);
            SetFanActive(state);
            SetBorderActive(state);
            SetPointActive(state);
            Active = state;
        }
        void SetFanActive(bool state)
        {
            switch (state)
            {
                case true:
                    foreach (var fanObj in _fans.AsSpan())
                    {
                        fanObj.layer = MajEnv.DEFAULT_LAYER;
                    }
                    break;
                case false:
                    foreach (var fanObj in _fans.AsSpan())
                    {
                        fanObj.layer = MajEnv.HIDDEN_LAYER;
                    }
                    break;
            }
        }
        void SetPointActive(bool state)
        {
            switch (state)
            {
                case true:
                    _pointObject.layer = MajEnv.DEFAULT_LAYER;
                    break;
                case false:
                    _pointObject.layer = MajEnv.HIDDEN_LAYER;
                    break;
            }
        }
        void SetBorderActive(bool state)
        {
            switch (state)
            {
                case true:
                    _borderObject.layer = MajEnv.DEFAULT_LAYER;
                    break;
                case false:
                    _borderObject.layer = MajEnv.HIDDEN_LAYER;
                    break;
            }
        }

        void SetFansPosition(in float distance)
        {
            for (var i = 0; i < 4; i++)
            {
                var pos = (0.226f + distance) * GetAngle(i);
                _fanTransforms[i].localPosition = pos;
            }
        }
        JudgeGrade EndJudge(in JudgeGrade result)
        {
            if (!_isJudged) 
                return result;
            var offset = (int)result > 7 ? 0 : _judgeDiff;
            var realityHT = (Length - 0.45f - offset / 1000f).Clamp(0, Length - 0.45f);
            var percent = ((realityHT - _playerReleaseTime) / realityHT).Clamp(0, 1);

            if (realityHT > 0)
            {
                if (percent >= 1f)
                {
                    if (result.IsMissOrTooFast())
                        return JudgeGrade.LateGood;
                    else if (MathF.Abs((int)result - 7) == 6)
                        return (int)result < 7 ? JudgeGrade.LateGreat : JudgeGrade.FastGreat;
                    else
                        return result;
                }
                else if (percent >= 0.67f)
                {
                    if (result.IsMissOrTooFast())
                        return JudgeGrade.LateGood;
                    else if (MathF.Abs((int)result - 7) == 6)
                        return (int)result < 7 ? JudgeGrade.LateGreat : JudgeGrade.FastGreat;
                    else if (result == JudgeGrade.Perfect)
                        return (int)result < 7 ? JudgeGrade.LatePerfect2nd : JudgeGrade.FastPerfect2nd;
                }
                else if (percent >= 0.33f)
                {
                    if (MathF.Abs((int)result - 7) >= 6)
                        return (int)result < 7 ? JudgeGrade.LateGood : JudgeGrade.FastGood;
                    else
                        return (int)result < 7 ? JudgeGrade.LateGreat : JudgeGrade.FastGreat;
                }
                else if (percent >= 0.05f)
                    return (int)result < 7 ? JudgeGrade.LateGood : JudgeGrade.FastGood;
                else if (percent >= 0)
                {
                    if (result.IsMissOrTooFast())
                        return JudgeGrade.Miss;
                    else
                        return (int)result < 7 ? JudgeGrade.LateGood : JudgeGrade.FastGood;
                }
            }
            //MajDebug.Log($"TouchHold: {MathF.Round(percent * 100, 2)}%\nTotal Len : {MathF.Round(realityHT * 1000, 2)}ms");
            return result;
        }
        void PlayHoldEffect()
        {
            //var r = MajInstances.AudioManager.GetSFX("touch_Hold_riser.wav");
            //MajDebug.Log($"IsPlaying:{r.IsPlaying}\nCurrent second: {r.CurrentSec}s");
            _audioEffMana.PlayTouchHoldSound();
            if (_lastHoldState is null || !(bool)_lastHoldState)
            {
                _effectManager.PlayHoldEffect(_sensorPos, _judgeResult);
                _borderRenderer.sprite = board_On;
                SetFansMaterial(DefaultMaterial);
            }
        }
        void StopHoldEffect()
        {
            if (_lastHoldState is null || (bool)_lastHoldState)
            {
                _effectManager.ResetHoldEffect(_sensorPos);
                _audioEffMana.StopTouchHoldSound();
                _borderRenderer.sprite = board_Off;
                SetFansMaterial(DefaultMaterial);
            }            
        }
        Vector3 GetAngle(int index)
        {
            var angle = Mathf.PI / 4 + index * (Mathf.PI / 2);
            return new Vector3(Mathf.Sin(angle), Mathf.Cos(angle));
        }
        void SetFansColor(Color color)
        {
            foreach (var fan in _fanRenderers.AsSpan()) 
                fan.color = color;
        }
        void SetFansMaterial(Material material)
        {
            for (var i = 0; i < 4; i++)
                _fanRenderers[i].sharedMaterial = material;
        }
        protected override void PlaySFX()
        {
            _audioEffMana.PlayTouchHoldSound();
        }
        protected override void PlayJudgeSFX(in JudgeResult judgeResult)
        {
            if (judgeResult.IsMissOrTooFast)
                return;
            _audioEffMana.PlayTapSound(judgeResult);
            if (isFirework)
                _audioEffMana.PlayHanabiSound();
        }

        RendererStatus _rendererState = RendererStatus.Off;
    }
}