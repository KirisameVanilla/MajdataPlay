﻿using MajdataPlay.Types;
using MajdataPlay.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using MajdataPlay.Utils;
using MajdataPlay.Collections;
using System.Linq;
using MajdataPlay.Game.Types;
#nullable enable
namespace MajdataPlay.Result
{
    public partial class ResultScreenManager : MonoBehaviour
    {
        public TextMeshProUGUI title;
        public TextMeshProUGUI artist;
        public TextMeshProUGUI designer;
        public TextMeshProUGUI level;

        public TextMeshProUGUI accDX;
        public TextMeshProUGUI accHistory;
        public TextMeshProUGUI dxScore;
        public TextMeshProUGUI rank;

        public TextMeshProUGUI perfectCount;
        public TextMeshProUGUI greatCount;
        public TextMeshProUGUI goodCount;
        public TextMeshProUGUI missCount;

        public TextMeshProUGUI fastCount;
        public TextMeshProUGUI lateCount;

        public TextMeshProUGUI omg;

        public TextMeshProUGUI subMonitor;


        public GameObject clearLogo;
        public GameObject xxlb;

        public Image coverImg;

        GameInfo _gameInfo = Majdata<GameInfo>.Instance!;

        UniTask OnlineSaveTask = UniTask.Delay(0);

        void Start()
        {
            if(MajInstances.RecordHelper is not null
               && MajInstances.RecordHelper.Recording
               && MajInstances.RecordHelper.Connected)
                MajInstances.RecordHelper.StopRecord();

            rank.text = "";
            var gameManager = MajInstances.GameManager;
            var result = _gameInfo.GetLastResult();
            var isClassic = gameManager.Setting.Judge.Mode == JudgeMode.Classic;

            MajInstances.LightManager.SetAllLight(Color.white);

            var totalJudgeRecord = JudgeDetail.UnpackJudgeRecord(result.JudgeRecord.TotalJudgeInfo);
            var song = result.SongDetail;
            var historyResult = MajInstances.ScoreManager.GetScore(song, gameManager.SelectedDiff);

            var intractSender = GetComponent<OnlineInteractionSender>();
            intractSender.Init(song);

            if (result.Acc.DX < 70)
            {
                omg.text = "您输了";
                xxlb.GetComponent<Animator>().SetTrigger("Bad");
            }
            else
            {
                omg.text = "您赢了";
                xxlb.GetComponent<Animator>().SetTrigger("Good");
            }

            title.text = song.Title;
            artist.text = song.Artist;
            designer.text = song.Designers[(int)_gameInfo.CurrentLevel] ?? "Undefined";
            level.text = _gameInfo.CurrentLevel.ToString() + " " + song.Levels[(int)_gameInfo.CurrentLevel];

            accDX.text = isClassic ? $"{result.Acc.Classic:F2}%" : $"{result.Acc.DX:F4}%";
            var nowacc = isClassic ? result.Acc.Classic : result.Acc.DX;
            var historyacc = isClassic ? historyResult.Acc.Classic : historyResult.Acc.DX;
            accHistory.text = $"{nowacc - historyacc:+0.0000;-0.0000;0}%";
            var dxScoreRank = new DXScoreRank(result.DXScore, result.TotalDXScore);
            if (dxScoreRank.Rank > 0)
                dxScore.text = $"*{dxScoreRank.Rank} {result.DXScore}/{result.TotalDXScore}";
            else
                dxScore.text = $"{result.DXScore}/{result.TotalDXScore}";

            perfectCount.text = $"{totalJudgeRecord.CriticalPerfect + totalJudgeRecord.Perfect}";
            greatCount.text = $"{totalJudgeRecord.Great}";
            goodCount.text = $"{totalJudgeRecord.Good}";
            missCount.text = $"{totalJudgeRecord.Miss}";

            fastCount.text = $"{result.Fast}";
            lateCount.text = $"{result.Late}";

            subMonitor.text = BuildSubDisplayText(result.JudgeRecord);

            LoadCover(song).Forget();

            var breakJudgeInfo = JudgeDetail.UnpackJudgeRecord(result.JudgeRecord[ScoreNoteType.Break]);

            if (!totalJudgeRecord.IsFullCombo)
                clearLogo.SetActive(false);
            else if (totalJudgeRecord.IsAllPerfect)
            {
                if (breakJudgeInfo.IsTheoretical)
                    clearLogo.GetComponentInChildren<TextMeshProUGUI>().text = "AP+";
                else
                    clearLogo.GetComponentInChildren<TextMeshProUGUI>().text = "AP";
            }
            else if (totalJudgeRecord.IsFullComboPlus)
                clearLogo.GetComponentInChildren<TextMeshProUGUI>().text = "FC+";

            MajInstances.AudioManager.PlaySFX("bgm_result.mp3", true);
            PlayVoice(result.Acc.DX, song).Forget();
            if (!MajInstances.GameManager.Setting.Mod.IsAnyModActive())
            {
                MajInstances.ScoreManager.SaveScore(result, result.Level);
                var score = MaiScore.CreateFromResult(result,result.Level);
                if (score is not null && song is OnlineSongDetail)
                {
                    OnlineSaveTask = intractSender.SendScore(score);
                }
            }
        }

        async UniTask LoadCover(ISongDetail song)
        {
            coverImg.sprite = await song.GetCoverAsync(true);
        }

        async UniTask PlayVoice(double dxacc, ISongDetail song)
        {
            if (dxacc >= 97)
            {
                MajInstances.AudioManager.PlaySFX("Rank.wav");
                await UniTask.WaitForSeconds(1);
            }
            if (dxacc >= 100.5f)
            {
                MajInstances.AudioManager.PlaySFX("SSS+.wav");
                rank.text = "SSS+";
            }
            else if (dxacc >= 100f)
            {
                MajInstances.AudioManager.PlaySFX("SSS.wav");
                rank.text = "SSS";
            }
            else if (dxacc >= 99.5f)
            {
                MajInstances.AudioManager.PlaySFX("SS+.wav");
                rank.text = "SS+";
            }
            else if (dxacc >= 99f)
            {
                MajInstances.AudioManager.PlaySFX("SS.wav");
                rank.text = "SS";
            }
            else if (dxacc >= 98f)
            {
                MajInstances.AudioManager.PlaySFX("S+.wav");
                rank.text = "S+";
            }
            else if (dxacc >= 97f)
            {
                MajInstances.AudioManager.PlaySFX("S.wav");
                rank.text = "S";
            }
            if (dxacc > 97)
            {
                await UniTask.WaitForSeconds(2);
                var list = new string[] { "good.wav", "good_2.wav", "good_3.wav", "good_4.wav", "good_5.wav", "good_6.wav" };
                MajInstances.AudioManager.PlaySFX(list[Random.Range(0, list.Length)]);
                await UniTask.WaitForSeconds(3);
                if (song is OnlineSongDetail)
                {
                    MajInstances.AudioManager.PlaySFX("dian_zan.wav");
                }
            }
            else
            {
                var list = new string[] { "wuyu.wav", "wuyu_2.wav", "wuyu_3.wav" };
                MajInstances.AudioManager.PlaySFX(list[Random.Range(0, list.Length)]);
                await UniTask.WaitForSeconds(2);
            }
            await OnlineSaveTask;
            MajInstances.InputManager.BindAnyArea(OnAreaDown);
            MajInstances.LightManager.SetButtonLight(Color.green, 3);
        }


        string BuildSubDisplayText(JudgeDetail judgeRecord)
            {
                var tapJudgeInfo = JudgeDetail.UnpackJudgeRecord(judgeRecord[ScoreNoteType.Tap]);
                var holdJudgeInfo = JudgeDetail.UnpackJudgeRecord(judgeRecord[ScoreNoteType.Hold]);
                var slideJudgeInfo = JudgeDetail.UnpackJudgeRecord(judgeRecord[ScoreNoteType.Slide]);
                var touchJudgeInfo = JudgeDetail.UnpackJudgeRecord(judgeRecord[ScoreNoteType.Touch]);
                var breakJudgeInfo = JudgeDetail.UnpackJudgeRecord(judgeRecord[ScoreNoteType.Break]);
                string[] nmsl = new string[]
                {
                "NOTES\t\tCP    \t\tP    \t\tGr    \t\tGd   \t\tM",
                $"Tap  \t\t\t{tapJudgeInfo.CriticalPerfect}\t\t{tapJudgeInfo.Perfect}\t\t{tapJudgeInfo.Great}\t\t{tapJudgeInfo.Good}\t\t{tapJudgeInfo.Miss}",
                $"Hold\t\t\t{holdJudgeInfo.CriticalPerfect}\t\t{holdJudgeInfo.Perfect}\t\t{holdJudgeInfo.Great}\t\t{holdJudgeInfo.Good}\t\t{holdJudgeInfo.Miss}",
                $"Slide\t\t\t{slideJudgeInfo.CriticalPerfect}\t\t{slideJudgeInfo.Perfect}\t\t{slideJudgeInfo.Great}\t\t{slideJudgeInfo.Good}\t\t{slideJudgeInfo.Miss}",
                $"Touch\t\t\t{touchJudgeInfo.CriticalPerfect}\t\t{touchJudgeInfo.Perfect}\t\t{touchJudgeInfo.Great}\t\t{touchJudgeInfo.Good}\t\t{touchJudgeInfo.Miss}",
                $"Break\t\t\t{breakJudgeInfo.CriticalPerfect}\t\t{breakJudgeInfo.Perfect}\t\t{breakJudgeInfo.Great}\t\t{breakJudgeInfo.Good}\t\t{breakJudgeInfo.Miss}"
                };
                return string.Join("\n", nmsl);
            }


        private void OnAreaDown(object sender, InputEventArgs e)
        {
            if (e.IsDown && e.IsButton && e.Type == SensorArea.A4)
            {
                var canNextRound = _gameInfo.NextRound();
                if (_gameInfo.IsDanMode)
                {
                    if (!canNextRound)
                    {
                        MajInstances.InputManager.UnbindAnyArea(OnAreaDown);
                        MajInstances.SceneSwitcher.SwitchScene("TotalResult");
                        return;

                    }
                    else
                    {
                        MajInstances.InputManager.UnbindAnyArea(OnAreaDown);
                        MajInstances.AudioManager.StopSFX("bgm_result.mp3");

                        //TODO: Add Animation to show that
                        //SongStorage.WorkingCollection.Index++;
                        //MajInstances.GameManager.DanHP += SongStorage.WorkingCollection.DanInfo.RestoreHP;

                        MajInstances.SceneSwitcher.SwitchScene("Game", false);
                        return;
                    }
                }
                MajInstances.InputManager.UnbindAnyArea(OnAreaDown);
                MajInstances.AudioManager.StopSFX("bgm_result.mp3");
                MajInstances.SceneSwitcher.SwitchScene("List",false);
                return;
            }
        }
    }
}