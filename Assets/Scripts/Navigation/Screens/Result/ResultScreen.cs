using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using DG.Tweening;
using Newtonsoft.Json.Linq;
using Proyecto26;
using UniRx.Async;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class ResultScreen : Screen, ScreenChangeListener
{
    public const string Id = "Result";

    public TransitionElement lowerLeftColumn;
    public TransitionElement lowerRightColumn;
    public TransitionElement upperRightColumn;
    
    public GameObject infoContainer;
    public Text scoreText;
    public Text newBestText;
    public Text gradeText;
    public Text accuracyText;
    public Text maxComboText;
    public Text standardMetricText;
    public Text advancedMetricText;

    public RankingsTab rankingsTab;
    public RatingTab ratingTab;

    public InteractableMonoBehavior shareButton;
    public CircleButton nextButton;
    public CircleButton retryButton;
    
    private bool uploadRecordSuccess;
    private GameState gameState;

    public override string GetId() => Id;

    public override async void OnScreenInitialized()
    {
        base.OnScreenInitialized();
        Context.ScreenManager.AddHandler(this);
        
        gameState = Context.GameState;
        if (gameState == null)
        {
            // Test mode
            Debug.Log("Result not set, entering test mode...");
            await Context.LevelManager.LoadFromMetadataFiles(new List<string>
                {Context.DataPath + "/suconh_typex.alice/level.json"});
            Context.SelectedLevel = Context.LevelManager.LoadedLocalLevels.Values.First();
            Context.SelectedDifficulty = Difficulty.Parse(Context.LevelManager.LoadedLocalLevels.Values.First().Meta.charts[0].type);
            Context.LocalPlayer.PlayRanked = false;
            gameState = new GameState(Context.SelectedLevel, Context.SelectedDifficulty);
            Context.OnlinePlayer.OnAuthenticated.AddListener(() =>
            {
                rankingsTab.UpdateRankings(Context.SelectedLevel.Id, Context.SelectedDifficulty.Id);
                ratingTab.UpdateLevelRating(Context.SelectedLevel.Id);
                UploadRecord();
            });
        }
        Context.GameState = null;
        
        nextButton.State = CircleButtonState.Start;
        nextButton.interactableMonoBehavior.onPointerClick.AddListener(_ =>
        {
            nextButton.StopPulsing();
            Done();
        });
        retryButton.State = CircleButtonState.Retry;
        retryButton.interactableMonoBehavior.onPointerClick.AddListener(_ =>
        {
            retryButton.StopPulsing();
            RetryGame();
        });

        // Load translucent cover
        var image = TranslucentCover.Instance.image;
        image.color = Color.white.WithAlpha(0);
        var sprite = Context.SpriteCache.GetCachedSpriteFromMemory("game://cover");
        if (sprite == null)
        {
            var path = "file://" + Context.SelectedLevel.Path + Context.SelectedLevel.Meta.background.path;
            using (var request = UnityWebRequestTexture.GetTexture(path))
            {
                await request.SendWebRequest();
                if (request.isNetworkError || request.isHttpError)
                {
                    Debug.LogError($"Failed to download cover from {path}");
                    Debug.LogError(request.error);
                    return;
                }

                sprite = DownloadHandlerTexture.GetContent(request).CreateSprite();
            }
        }

        image.sprite = sprite;
        image.FitSpriteAspectRatio();

        // Update performance info
        scoreText.text = Mathf.FloorToInt((float) gameState.Score).ToString("D6");
        accuracyText.text = "RESULT_X_ACCURACY".Get((Math.Floor(gameState.Accuracy * 100 * 100) / 100).ToString("0.00"));
        if (Mathf.Approximately((float) gameState.Accuracy, 1))
        {
            accuracyText.text = "RESULT_FULL_ACCURACY".Get();
        }
        maxComboText.text = "RESULT_X_COMBO".Get(gameState.MaxCombo);
        if (gameState.GradeCounts[NoteGrade.Bad] == 0 && gameState.GradeCounts[NoteGrade.Miss] == 0)
        {
            maxComboText.text = "RESULT_FULL_COMBO".Get();
        }

        var scoreGrade = ScoreGrades.From(gameState.Score);
        gradeText.text = scoreGrade.ToString();
        gradeText.GetComponent<GradientMeshEffect>().SetGradient(scoreGrade.GetGradient());
        if (scoreGrade == ScoreGrade.MAX || scoreGrade == ScoreGrade.SSS)
        {
            scoreText.GetComponent<GradientMeshEffect>().SetGradient(scoreGrade.GetGradient());
        }
        else
        {
            scoreText.GetComponent<GradientMeshEffect>().enabled = false;
        }

        standardMetricText.text = $"<b>Perfect</b> {gameState.GradeCounts[NoteGrade.Perfect]}    " +
                                  $"<b>Great</b> {gameState.GradeCounts[NoteGrade.Great]}    " +
                                  $"<b>Good</b> {gameState.GradeCounts[NoteGrade.Good]}    " +
                                  $"<b>Bad</b> {gameState.GradeCounts[NoteGrade.Bad]}    " +
                                  $"<b>Miss</b> {gameState.GradeCounts[NoteGrade.Miss]}";
        advancedMetricText.text = $"<b>Early</b> {gameState.EarlyCount}    " +
                                  $"<b>Late</b> {gameState.LateCount}    " +
                                  $"<b>{"RESULT_AVG_TIMING_ERR".Get()}</b> {gameState.AverageTimingError:0.000}s    " +
                                  $"<b>{"RESULT_STD_TIMING_ERR".Get()}</b> {gameState.StandardTimingError:0.000}s";
        if (!Context.LocalPlayer.DisplayEarlyLateIndicators) advancedMetricText.text = "";

        // Increment local play count
        Context.LocalPlayer.SetPlayCount(gameState.Level.Id, gameState.Difficulty.Id,
            Context.LocalPlayer.GetPlayCount(gameState.Level.Id, gameState.Difficulty.Id) + 1);

        // Save performance to local
        var clearType = string.Empty;
        if (gameState.Mods.Contains(Mod.AP)) clearType = "AP";
        if (gameState.Mods.Contains(Mod.FC)) clearType = "FC";
        if (gameState.Mods.Contains(Mod.Hard)) clearType = "Hard";
        if (gameState.Mods.Contains(Mod.ExHard)) clearType = "ExHard";

        if (!Context.LocalPlayer.HasPerformance(gameState.Level.Id, gameState.Difficulty.Id, Context.LocalPlayer.PlayRanked))
        {
            newBestText.text = "RESULT_NEW".Get();
            Context.LocalPlayer.SetBestPerformance(gameState.Level.Id, gameState.Difficulty.Id,
                Context.LocalPlayer.PlayRanked,
                new LocalPlayer.Performance
                {
                    Score = (int) gameState.Score, Accuracy = (float) (gameState.Accuracy * 100), ClearType = clearType
                });
        }
        else
        {
            var historicBest = Context.LocalPlayer.GetBestPerformance(gameState.Level.Id,
                gameState.Difficulty.Id, Context.LocalPlayer.PlayRanked);
            var newBest = new LocalPlayer.Performance
            {
                Score = historicBest.Score, Accuracy = historicBest.Accuracy, ClearType = historicBest.ClearType
            };
            if (gameState.Score > historicBest.Score)
            {
                newBestText.text = $"+{Mathf.FloorToInt((float) (gameState.Score - historicBest.Score))}";
                newBest.Score = (int) gameState.Score;
                newBest.ClearType = clearType;
            }
            else
            {
                newBestText.text = "";
            }

            var multipliedAccuracy = gameState.Accuracy * 100;
            if (multipliedAccuracy > historicBest.Accuracy)
            {
                newBest.Accuracy = (float) multipliedAccuracy;
            }

            Context.LocalPlayer.SetBestPerformance(gameState.Level.Id, gameState.Difficulty.Id,
                Context.LocalPlayer.PlayRanked, newBest);
        }
        
        shareButton.onPointerClick.AddListener(_ => StartCoroutine(Share()));

        await Resources.UnloadUnusedAssets();
    }

    public override void OnScreenDestroyed()
    {
        base.OnScreenDestroyed();
        Context.ScreenManager.RemoveHandler(this);
    }

    public override void OnScreenBecameActive()
    {
        base.OnScreenBecameActive();

        TranslucentCover.Instance.image.DOFade(0.9f, 0.8f);
        ProfileWidget.Instance.Enter();
        upperRightColumn.Enter();

        ratingTab.UpdateLevelRating(gameState.Level.Id)
            .Then(it =>
            {
                if (it == null) return;
                if (Context.OnlinePlayer.IsAuthenticated && it.rating <= 0 && ScoreGrades.From(gameState.Score) >= ScoreGrade.A)
                {
                    // Invoke the rate dialog
                    ratingTab.rateLevelElement.rateButton.onPointerClick.Invoke(null);
                }
            });

        if (Context.OnlinePlayer.IsAuthenticated)
        {
            UploadRecord();
        }
        else
        {
            EnterControls();
        }

        if (Context.LocalPlayer.PlayRanked && !Context.OnlinePlayer.IsAuthenticated)
        {
            rankingsTab.UpdateRankings(gameState.Level.Id, gameState.Difficulty.Id);
        }

        LoopAudioPlayer.Instance.PlayResultLoopAudio();
        LoopAudioPlayer.Instance.FadeInLoopPlayer(0);
    }

    public override void OnScreenPostActive()
    {
        base.OnScreenPostActive();
        LayoutRebuilder.ForceRebuildLayoutImmediate(infoContainer.transform as RectTransform);
        infoContainer.GetComponentsInChildren<TransitionElement>().ForEach(it => it.UseCurrentStateAsDefault());
    }

    public override void OnScreenBecameInactive()
    {
        base.OnScreenBecameInactive();
        Run.After(0.4f, () => TranslucentCover.Instance.image.color = Color.white.WithAlpha(0));
    }

    private bool isSharing;

    public IEnumerator Share()
    {
        if (isSharing) yield break;
        
        isSharing = true;
        Context.AudioManager.Get("Navigate1").Play(ignoreDsp: true);
        
        var levelMeta = Context.SelectedLevel.Meta;

        yield return new WaitForEndOfFrame();
        
        var screenshot = new Texture2D(UnityEngine.Screen.width, UnityEngine.Screen.height, TextureFormat.RGB24, false);
        screenshot.ReadPixels(new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height), 0, 0);
        screenshot.Apply();

        var tmpPath = Path.Combine(Application.temporaryCachePath, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ".png");
        File.WriteAllBytes(tmpPath, screenshot.EncodeToPNG());

        Destroy(screenshot);

        var diff = Difficulty.ConvertToDisplayLevel(levelMeta.GetDifficultyLevel(Context.SelectedDifficulty.Id));
        var shareText = $"#cytoid [Lv.{diff}] {levelMeta.artist} - {levelMeta.title} / Charter: {levelMeta.charter}";
        if (uploadRecordSuccess)
        {
            shareText += $"\nhttps://cytoid.io/levels/{levelMeta.id}";
        }

        new NativeShare()
            .AddFile(tmpPath)
            .SetText(shareText)
            .Share();

        isSharing = false;
    }

    public void UploadRecord()
    {
        rankingsTab.spinner.IsSpinning = true;
        var uri = $"{Context.ApiBaseUrl}/levels/{gameState.Level.Id}/charts/{gameState.Difficulty.Id}/records";
        Debug.Log("Posting to " + uri);
        RestClient.Post<UploadRecordResult>(new RequestHelper
        {
            Uri = uri,
            BodyString = JObject.FromObject(new UploadRecord
            {
                score = (int) gameState.Score,
                accuracy = double.Parse(gameState.Accuracy.ToString("0.00000000")),
                details = new UploadRecord.Details
                {
                    perfect = gameState.GradeCounts[NoteGrade.Perfect],
                    great = gameState.GradeCounts[NoteGrade.Great],
                    good = gameState.GradeCounts[NoteGrade.Good],
                    bad = gameState.GradeCounts[NoteGrade.Bad],
                    miss = gameState.GradeCounts[NoteGrade.Miss],
                    maxCombo = gameState.MaxCombo
                },
                mods = gameState.Mods.Select(it => Enum.GetName(typeof(Mod), it)).ToList(),
                ranked = Context.LocalPlayer.PlayRanked,
            }).ToString(),
            Headers = Context.OnlinePlayer.GetJwtAuthorizationHeaders(),
            EnableDebug = true
        }).Then(_ =>
            {
                uploadRecordSuccess = true;
                Toast.Next(Toast.Status.Success, "TOAST_PERFORMANCE_SYNCHRONIZED".Get());
                EnterControls();
                rankingsTab.UpdateRankings(gameState.Level.Id, gameState.Difficulty.Id);
                Context.OnlinePlayer.FetchProfile();
            }
        ).Catch(error =>
        {
            Debug.Log(error.Response);
            if (error.IsNetworkError)
            {
                Toast.Next(Toast.Status.Failure, "TOAST_CHECK_NETWORK_CONNECTION".Get());
            }
            else if (error.IsHttpError)
            {
                Toast.Next(Toast.Status.Failure, "TOAST_LEVEL_NOT_RANKED".Get());
            }

            var dialog = Dialog.Instantiate();
            dialog.Message = "DIALOG_RETRY_SYNCHRONIZE_PERFORMANCE".Get();
            dialog.UseProgress = false;
            dialog.UsePositiveButton = true;
            dialog.UseNegativeButton = true;
            dialog.OnPositiveButtonClicked = _ =>
            {
                dialog.Close();
                UploadRecord();
            };
            dialog.OnNegativeButtonClicked = _ =>
            {
                dialog.Close();
                EnterControls();
                rankingsTab.UpdateRankings(gameState.Level.Id, gameState.Difficulty.Id);
                Context.OnlinePlayer.FetchProfile();
            };
            dialog.Open();
        });
    }

    public void Done()
    {
        LoopAudioPlayer.Instance.FadeOutLoopPlayer(0.4f);
        LoopAudioPlayer.Instance.PlayMainLoopAudio();
        Context.ScreenManager.ChangeScreen(GamePreparationScreen.Id, ScreenTransition.Out, willDestroy: true,
            onFinished: screen => Resources.UnloadUnusedAssets());
        Context.AudioManager.Get("LevelStart").Play();
    }

    public async void RetryGame()
    {
        LoopAudioPlayer.Instance.FadeOutLoopPlayer(0.4f);

        State = ScreenState.Inactive;

        ProfileWidget.Instance.FadeOut();

        Context.AudioManager.Get("LevelStart").Play();

        var sceneLoader = new SceneLoader("Game");
        sceneLoader.Load();
        await UniTask.Delay(TimeSpan.FromSeconds(0.8f));
        TranslucentCover.Instance.image.DOFade(0, 0.8f);
        await UniTask.Delay(TimeSpan.FromSeconds(0.8f));
        sceneLoader.Activate();
    }

    private void EnterControls()
    {
        lowerLeftColumn.Enter();
        lowerRightColumn.Enter();
    }

    public void OnScreenChangeStarted(Screen from, Screen to) => Expression.Empty();

    public void OnScreenChangeFinished(Screen from, Screen to)
    {
        if (from == this)
        {
            // Dispose game cover
            Context.SpriteCache.DisposeTaggedSpritesInMemory(SpriteTag.GameCover);
        }
    }
}