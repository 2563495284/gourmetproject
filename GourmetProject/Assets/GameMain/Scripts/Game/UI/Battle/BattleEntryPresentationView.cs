using System;
using DG.Tweening;
using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Gameplay.Scoring;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Battle
{
    /// <summary>
    /// 经营挑战揭幕前的目标展示。根节点透明但全屏拦截输入，视觉内容只落在中部遮罩范围内。
    /// </summary>
    public sealed class BattleEntryPresentationView : MonoBehaviour, IPointerClickHandler
    {
        private const float TargetEnterDuration = 0.18f;
        private const float RuleEnterDelay = 0.06f;
        private const float RuleEnterDuration = 0.20f;
        private const float NormalHoldDuration = 1.03f;
        private const float RuleHoldDuration = 1.02f;
        private const float NormalExitDuration = 0.16f;
        private const float RuleExitDuration = 0.18f;

        [Header("Prefab hierarchy")]
        [SerializeField] private RectTransform _overlay;
        [SerializeField] private CanvasGroup _inputGroup;
        [SerializeField] private CanvasGroup _contentGroup;
        [SerializeField] private RectTransform _targetGroup;
        [SerializeField] private TMP_Text _targetScoreText;
        [SerializeField] private RectTransform _ruleGroup;
        [SerializeField] private CanvasGroup _ruleGroupCanvas;
        [SerializeField] private Image _ruleIcon;
        [SerializeField] private TMP_Text _ruleNameText;
        [SerializeField] private TMP_Text _ruleDescriptionText;

        [Header("Designer assets")]
        [SerializeField] private TimelineAxisTheme _timelineTheme;

        private static readonly Vector2 NormalTargetPosition = Vector2.zero;
        private static readonly Vector2 RuleTargetPosition = new Vector2(0f, 205f);

        private Action _skipHandler;
        private bool _isBound;
        private bool _prepared;
        private bool _hasRule;
        private bool _isPlaying;
        private bool _skipConsumed;

        internal string DisplayedTargetScore => _targetScoreText?.text ?? string.Empty;
        internal bool ShowsRule => _ruleGroup != null && _ruleGroup.gameObject.activeSelf;
        internal string DisplayedRuleName => _ruleNameText?.text ?? string.Empty;
        internal string DisplayedRuleDescription => _ruleDescriptionText?.text ?? string.Empty;
        internal Sprite DisplayedRuleIcon => _ruleIcon != null ? _ruleIcon.sprite : null;
        internal bool BlocksInput => _inputGroup != null && _inputGroup.blocksRaycasts;

        public void EnsureBuilt()
        {
            if (_isBound)
            {
                _overlay.SetAsLastSibling();
                return;
            }

            if (!HasCompletePrefabBindings(out string missingBinding))
            {
                throw new MissingReferenceException(
                    $"BattleEntryPresentationView requires prefab binding '{missingBinding}'. " +
                    "Edit BattleEntryPresentationOverlay.prefab instead of constructing UI at runtime.");
            }

            _isBound = true;
            ResetImmediate();
        }

        public void Prepare(int requiredScore, cfg.BossDebuff rule)
        {
            EnsureBuilt();
            _prepared = true;
            _hasRule = rule != null;
            _targetScoreText.text =
                $"<sprite name=\"dish_value_icon\"> {ScoreNumberFormatter.Format(requiredScore)}";
            _targetGroup.anchoredPosition = _hasRule ? RuleTargetPosition : NormalTargetPosition;
            _ruleGroup.gameObject.SetActive(_hasRule);

            if (!_hasRule)
            {
                _ruleIcon.sprite = null;
                _ruleNameText.text = string.Empty;
                _ruleDescriptionText.text = string.Empty;
                return;
            }

            _ruleIcon.sprite = _timelineTheme.ResolveIcon(
                TimelineAxisIconKeys.Boss(rule.Id),
                ActionDisplayKind.Boss);
            _ruleNameText.text = rule.Name ?? string.Empty;
            SemanticDescriptionFormatter.Set(_ruleDescriptionText, rule.Desc);
        }

        public Tween BuildPresentationTween()
        {
            EnsureBuilt();
            if (!_prepared)
            {
                return null;
            }

            _contentGroup.alpha = 0f;
            _targetGroup.localScale = Vector3.one * 0.90f;
            _ruleGroup.gameObject.SetActive(_hasRule);
            _ruleGroup.localScale = Vector3.one * 0.92f;
            _ruleGroupCanvas.alpha = 0f;
            _isPlaying = false;
            _skipConsumed = false;

            Sequence sequence = DOTween.Sequence().SetUpdate(true);
            sequence.AppendCallback(BeginPlayback);
            sequence.Append(_contentGroup.DOFade(1f, TargetEnterDuration).SetEase(Ease.OutSine));
            sequence.Join(_targetGroup.DOScale(1f, TargetEnterDuration).SetEase(Ease.OutCubic));

            if (_hasRule)
            {
                sequence.AppendInterval(RuleEnterDelay);
                sequence.Append(_ruleGroupCanvas.DOFade(1f, RuleEnterDuration).SetEase(Ease.OutSine));
                sequence.Join(_ruleGroup.DOScale(1f, RuleEnterDuration).SetEase(Ease.OutBack));
                sequence.AppendInterval(RuleHoldDuration);
                sequence.Append(_contentGroup.DOFade(0f, RuleExitDuration).SetEase(Ease.InSine));
            }
            else
            {
                sequence.AppendInterval(NormalHoldDuration);
                sequence.Append(_contentGroup.DOFade(0f, NormalExitDuration).SetEase(Ease.InSine));
            }

            sequence.AppendCallback(EndPlayback);
            sequence.OnKill(EndPlayback);
            return sequence;
        }

        public void BindSkipHandler(Action handler)
        {
            _skipHandler = handler;
            if (handler == null)
            {
                ResetImmediate();
            }
        }

        public void ResetImmediate()
        {
            _skipHandler = null;
            _skipConsumed = false;
            _isPlaying = false;
            _prepared = false;
            _hasRule = false;

            if (_inputGroup != null)
            {
                _inputGroup.alpha = 1f;
                _inputGroup.interactable = false;
                _inputGroup.blocksRaycasts = false;
            }

            if (_contentGroup != null)
            {
                _contentGroup.alpha = 0f;
            }

            if (_targetGroup != null)
            {
                _targetGroup.anchoredPosition = NormalTargetPosition;
                _targetGroup.localScale = Vector3.one;
            }

            if (_targetScoreText != null)
            {
                _targetScoreText.text = string.Empty;
            }

            if (_ruleGroupCanvas != null)
            {
                _ruleGroupCanvas.alpha = 0f;
            }

            if (_ruleGroup != null)
            {
                _ruleGroup.localScale = Vector3.one;
                _ruleGroup.gameObject.SetActive(false);
            }

            if (_ruleIcon != null)
            {
                _ruleIcon.sprite = null;
            }

            if (_ruleNameText != null)
            {
                _ruleNameText.text = string.Empty;
            }

            if (_ruleDescriptionText != null)
            {
                _ruleDescriptionText.text = string.Empty;
            }

            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!_isPlaying || _skipConsumed || _skipHandler == null)
            {
                return;
            }

            _skipConsumed = true;
            _isPlaying = false;
            Action skip = _skipHandler;
            skip.Invoke();
        }

        private void BeginPlayback()
        {
            gameObject.SetActive(true);
            _overlay.SetAsLastSibling();
            _inputGroup.alpha = 1f;
            _inputGroup.interactable = true;
            _inputGroup.blocksRaycasts = true;
            _isPlaying = true;
            _skipConsumed = false;
        }

        private void EndPlayback()
        {
            _isPlaying = false;
            if (_inputGroup != null)
            {
                _inputGroup.interactable = false;
                _inputGroup.blocksRaycasts = false;
            }

            if (_contentGroup != null)
            {
                _contentGroup.alpha = 0f;
            }

            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        private bool HasCompletePrefabBindings(out string missingBinding)
        {
            (UnityEngine.Object value, string name)[] bindings =
            {
                (_overlay, nameof(_overlay)),
                (_inputGroup, nameof(_inputGroup)),
                (_contentGroup, nameof(_contentGroup)),
                (_targetGroup, nameof(_targetGroup)),
                (_targetScoreText, nameof(_targetScoreText)),
                (_ruleGroup, nameof(_ruleGroup)),
                (_ruleGroupCanvas, nameof(_ruleGroupCanvas)),
                (_ruleIcon, nameof(_ruleIcon)),
                (_ruleNameText, nameof(_ruleNameText)),
                (_ruleDescriptionText, nameof(_ruleDescriptionText)),
                (_timelineTheme, nameof(_timelineTheme)),
            };

            foreach ((UnityEngine.Object value, string name) in bindings)
            {
                if (value == null)
                {
                    missingBinding = name;
                    return false;
                }
            }

            missingBinding = string.Empty;
            return true;
        }
    }
}
