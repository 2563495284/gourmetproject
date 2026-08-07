using System;
using System.Threading;
using DG.Tweening;
using GourmetProject.Game.Save;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace GourmetProject.Game.UI.Common
{
    /// <summary>
    /// 首次进入菜单前播放的八格动态漫画。玩家点击推进，最终格确认后打开主菜单并记录完成版本。
    /// </summary>
    public sealed class OpeningComicForm : UGuiForm, IPointerClickHandler
    {
        private const float EntranceDuration = 0.35f;
        private const float AdvanceDuration = 0.48f;
        private const float ExitDuration = 0.35f;
        private const float MainMenuOpenTimeout = 5f;

        private static readonly Vector2 CenterPosition = Vector2.zero;
        private static readonly Vector2 CenterSize = new(1320f, 760f);
        private static readonly Vector2 HistoryPosition = new(-610f, 10f);
        private static readonly Vector2 HistorySize = new(600f, 360f);
        private static readonly Vector2 CurrentPosition = new(320f, 10f);
        private static readonly Vector2 CurrentSize = new(1120f, 660f);
        private static readonly Vector2 IncomingPosition = new(1080f, 10f);
        private static readonly Vector2 HistoryExitPosition = new(-1240f, 10f);

        [Header("Content")]
        [SerializeField] private Sprite[] _panels;

        [Header("Three reusable panel views")]
        [SerializeField] private RectTransform[] _panelRoots;
        [SerializeField] private RectTransform[] _revealRoots;
        [SerializeField] private RectTransform[] _artRoots;
        [SerializeField] private Image[] _panelImages;
        [SerializeField] private CanvasGroup[] _panelCanvasGroups;

        [Header("Form chrome")]
        [SerializeField] private CanvasGroup _formCanvasGroup;
        [SerializeField] private TMP_Text _promptText;

        private OpeningComicPlaybackState _playback;
        private CancellationTokenSource _lifetimeCts;
        private Tween _promptTween;
        private int _currentViewIndex;
        private int _historyViewIndex = -1;
        private bool _inputLocked;
        private bool _isFinishing;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            _formCanvasGroup ??= GetComponent<CanvasGroup>();
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            CancelAnimations();

            _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            _inputLocked = true;
            _isFinishing = false;
            _historyViewIndex = -1;
            _currentViewIndex = 0;

            if (!ValidateBindings())
            {
                Log.Error("[OpeningComicForm] Prefab bindings are incomplete; falling back to the main menu.");
                OpenMainMenuWithoutCompletingAsync(_lifetimeCts.Token);
                return;
            }

            _playback = new OpeningComicPlaybackState(_panels.Length);
            _formCanvasGroup.alpha = 1f;
            _formCanvasGroup.blocksRaycasts = true;
            _formCanvasGroup.interactable = true;

            for (int i = 0; i < _panelRoots.Length; i++)
            {
                HideView(i);
            }

            PrepareView(_currentViewIndex, 0, CenterPosition, CenterSize, 0.92f, 0f, true);
            _panelRoots[_currentViewIndex].SetAsLastSibling();
            SetPromptVisible(false);

            Sequence entrance = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(gameObject);
            entrance.Join(_panelCanvasGroups[_currentViewIndex].DOFade(1f, EntranceDuration));
            entrance.Join(_panelRoots[_currentViewIndex].DOScale(1f, EntranceDuration).SetEase(Ease.OutBack));
            entrance.OnComplete(() =>
            {
                _inputLocked = false;
                ShowPrompt();
            });
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            CancelAnimations();
            _playback = null;
            base.OnClose(isShutdown, userData);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            HandleAdvanceRequest();
        }

        internal void HandleAdvanceRequest()
        {
            if (_inputLocked || _isFinishing || _playback == null)
            {
                return;
            }

            if (_playback.IsFinalPanel)
            {
                OpenMainMenuAndFinishAsync(_lifetimeCts.Token);
                return;
            }

            if (!_playback.TryBeginAdvance())
            {
                return;
            }

            AdvanceToNextPanel();
        }

        private void AdvanceToNextPanel()
        {
            _inputLocked = true;
            StopPromptPulse();
            SetPromptVisible(false);

            int outgoingHistoryIndex = _historyViewIndex;
            int movingCurrentIndex = _currentViewIndex;
            int incomingIndex = FindUnusedViewIndex(movingCurrentIndex, outgoingHistoryIndex);
            int nextPanelIndex = _playback.CurrentIndex + 1;

            PrepareView(incomingIndex, nextPanelIndex, IncomingPosition, CurrentSize, 0.96f, 0f, false);
            if (outgoingHistoryIndex >= 0)
            {
                _panelRoots[outgoingHistoryIndex].SetAsFirstSibling();
            }

            _panelRoots[movingCurrentIndex].SetAsLastSibling();
            _panelRoots[incomingIndex].SetAsLastSibling();

            Sequence sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(gameObject);

            if (outgoingHistoryIndex >= 0)
            {
                sequence.Join(_panelRoots[outgoingHistoryIndex]
                    .DOAnchorPos(HistoryExitPosition, AdvanceDuration)
                    .SetEase(Ease.InCubic));
                sequence.Join(_panelCanvasGroups[outgoingHistoryIndex]
                    .DOFade(0f, AdvanceDuration * 0.75f));
            }

            JoinViewLayoutTween(sequence, movingCurrentIndex, HistoryPosition, HistorySize, AdvanceDuration, Ease.OutCubic);
            sequence.Join(_panelRoots[movingCurrentIndex]
                .DOScale(0.98f, AdvanceDuration)
                .SetEase(Ease.OutCubic));

            sequence.Join(_panelRoots[incomingIndex]
                .DOAnchorPos(CurrentPosition, AdvanceDuration)
                .SetEase(Ease.OutCubic));
            sequence.Join(_panelCanvasGroups[incomingIndex].DOFade(1f, AdvanceDuration * 0.78f));
            sequence.Join(_panelRoots[incomingIndex]
                .DOScale(1f, AdvanceDuration)
                .SetEase(Ease.OutBack));
            sequence.Join(_revealRoots[incomingIndex]
                .DOSizeDelta(CurrentSize, AdvanceDuration)
                .SetEase(Ease.OutCubic));

            try
            {
                GameApp.Audio.PlayPickup();
            }
            catch (Exception ex)
            {
                Log.Warning("[OpeningComicForm] Could not play panel sound: {0}", ex.Message);
            }

            sequence.OnComplete(() =>
            {
                if (outgoingHistoryIndex >= 0)
                {
                    HideView(outgoingHistoryIndex);
                }

                _historyViewIndex = movingCurrentIndex;
                _currentViewIndex = incomingIndex;
                _playback.CompleteAdvance();
                _inputLocked = false;

                try
                {
                    GameApp.Audio.PlayPlacement();
                }
                catch (Exception ex)
                {
                    Log.Warning("[OpeningComicForm] Could not play settle sound: {0}", ex.Message);
                }

                ShowPrompt();
            });
        }

        private async void OpenMainMenuAndFinishAsync(CancellationToken token)
        {
            _isFinishing = true;
            _inputLocked = true;
            StopPromptPulse();
            SetPrompt("正在进入游戏…");
            SetPromptVisible(true);

            try
            {
                if (!GameApp.UI.HasUIForm(UIForms.MainMenu) && !GameApp.UI.IsLoadingUIForm(UIForms.MainMenu))
                {
                    int serialId = GameApp.UI.OpenUIForm(UIForms.MainMenu, UIForms.GroupDefault);
                    if (serialId <= 0)
                    {
                        RestoreFinalPanelAfterOpenFailure();
                        return;
                    }
                }

                float deadline = Time.realtimeSinceStartup + MainMenuOpenTimeout;
                while (!GameApp.UI.HasUIForm(UIForms.MainMenu) && Time.realtimeSinceStartup < deadline)
                {
                    await Awaitable.NextFrameAsync(token);
                }

                if (!GameApp.UI.HasUIForm(UIForms.MainMenu))
                {
                    RestoreFinalPanelAfterOpenFailure();
                    return;
                }

                try
                {
                    GameSaveData saveData = GameSavePersistence.Load();
                    OpeningComicProgress.MarkCompleted(saveData.GuideProgress);
                    GameSavePersistence.Save(saveData);
                }
                catch (Exception ex)
                {
                    Log.Error("[OpeningComicForm] Failed to persist completion: {0}", ex);
                }

                _formCanvasGroup.blocksRaycasts = false;
                Tween fade = _formCanvasGroup.DOFade(0f, ExitDuration)
                    .SetUpdate(true)
                    .SetLink(gameObject);
                await fade.AsyncWaitForCompletion();

                if (!token.IsCancellationRequested && UIForm != null)
                {
                    GameApp.UI.CloseUIForm(UIForm);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.Error("[OpeningComicForm] Failed to finish opening comic: {0}", ex);
                RestoreFinalPanelAfterOpenFailure();
            }
        }

        private async void OpenMainMenuWithoutCompletingAsync(CancellationToken token)
        {
            try
            {
                if (!GameApp.UI.HasUIForm(UIForms.MainMenu) && !GameApp.UI.IsLoadingUIForm(UIForms.MainMenu))
                {
                    GameApp.UI.OpenUIForm(UIForms.MainMenu, UIForms.GroupDefault);
                }

                float deadline = Time.realtimeSinceStartup + MainMenuOpenTimeout;
                while (!GameApp.UI.HasUIForm(UIForms.MainMenu) && Time.realtimeSinceStartup < deadline)
                {
                    await Awaitable.NextFrameAsync(token);
                }

                if (!token.IsCancellationRequested && GameApp.UI.HasUIForm(UIForms.MainMenu) && UIForm != null)
                {
                    GameApp.UI.CloseUIForm(UIForm);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void RestoreFinalPanelAfterOpenFailure()
        {
            _isFinishing = false;
            _inputLocked = false;
            SetPrompt("进入失败，点击重试");
            SetPromptVisible(true);
            StartPromptPulse();
        }

        private void ShowPrompt()
        {
            SetPrompt(_playback != null && _playback.IsFinalPanel ? "点击进入游戏" : "点击继续");
            SetPromptVisible(true);
            StartPromptPulse();
        }

        private void SetPrompt(string text)
        {
            if (_promptText != null)
            {
                _promptText.text = text;
            }
        }

        private void SetPromptVisible(bool visible)
        {
            if (_promptText != null)
            {
                _promptText.gameObject.SetActive(visible);
                Color color = _promptText.color;
                color.a = 1f;
                _promptText.color = color;
            }
        }

        private void StartPromptPulse()
        {
            StopPromptPulse();
            if (_promptText == null || !_promptText.gameObject.activeInHierarchy)
            {
                return;
            }

            _promptTween = _promptText.DOFade(0.38f, 0.72f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.InOutSine)
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        private void StopPromptPulse()
        {
            _promptTween?.Kill();
            _promptTween = null;
        }

        private void PrepareView(
            int viewIndex,
            int panelIndex,
            Vector2 position,
            Vector2 size,
            float scale,
            float alpha,
            bool fullyRevealed)
        {
            RectTransform root = _panelRoots[viewIndex];
            root.gameObject.SetActive(true);
            root.anchoredPosition = position;
            root.sizeDelta = size;
            root.localScale = new Vector3(scale, scale, 1f);

            _revealRoots[viewIndex].sizeDelta = fullyRevealed ? size : new Vector2(0f, size.y);
            _artRoots[viewIndex].sizeDelta = size;
            _panelImages[viewIndex].sprite = _panels[panelIndex];
            _panelImages[viewIndex].preserveAspect = true;
            _panelCanvasGroups[viewIndex].alpha = alpha;
        }

        private void HideView(int viewIndex)
        {
            _panelRoots[viewIndex].DOKill();
            _revealRoots[viewIndex].DOKill();
            _artRoots[viewIndex].DOKill();
            _panelCanvasGroups[viewIndex].DOKill();
            _panelCanvasGroups[viewIndex].alpha = 0f;
            _panelRoots[viewIndex].gameObject.SetActive(false);
        }

        private void JoinViewLayoutTween(
            Sequence sequence,
            int viewIndex,
            Vector2 position,
            Vector2 size,
            float duration,
            Ease ease)
        {
            sequence.Join(_panelRoots[viewIndex].DOAnchorPos(position, duration).SetEase(ease));
            sequence.Join(_panelRoots[viewIndex].DOSizeDelta(size, duration).SetEase(ease));
            sequence.Join(_revealRoots[viewIndex].DOSizeDelta(size, duration).SetEase(ease));
            sequence.Join(_artRoots[viewIndex].DOSizeDelta(size, duration).SetEase(ease));
        }

        private int FindUnusedViewIndex(int firstUsed, int secondUsed)
        {
            for (int i = 0; i < _panelRoots.Length; i++)
            {
                if (i != firstUsed && i != secondUsed)
                {
                    return i;
                }
            }

            throw new InvalidOperationException("Opening comic requires three reusable panel views.");
        }

        private bool ValidateBindings()
        {
            const int requiredViewCount = 3;
            return _panels != null && _panels.Length == 8 &&
                   _panelRoots != null && _panelRoots.Length == requiredViewCount &&
                   _revealRoots != null && _revealRoots.Length == requiredViewCount &&
                   _artRoots != null && _artRoots.Length == requiredViewCount &&
                   _panelImages != null && _panelImages.Length == requiredViewCount &&
                   _panelCanvasGroups != null && _panelCanvasGroups.Length == requiredViewCount &&
                   _formCanvasGroup != null && _promptText != null;
        }

        private void CancelAnimations()
        {
            _lifetimeCts?.Cancel();
            _lifetimeCts?.Dispose();
            _lifetimeCts = null;
            StopPromptPulse();
            DOTween.Kill(gameObject);
            _formCanvasGroup?.DOKill();
            _promptText?.DOKill();
            KillTweens(_panelRoots);
            KillTweens(_revealRoots);
            KillTweens(_artRoots);
            KillTweens(_panelCanvasGroups);
            _playback?.CancelAdvance();
        }

        private static void KillTweens<T>(T[] targets) where T : Component
        {
            if (targets == null)
            {
                return;
            }

            foreach (T target in targets)
            {
                target?.DOKill();
            }
        }
    }
}
