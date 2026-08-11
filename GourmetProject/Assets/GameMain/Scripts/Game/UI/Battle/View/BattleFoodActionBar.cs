using System;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Runtime.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 经营挑战态操作条：菜谱入口、结算按钮，以及 STS2 风格的绘制 / 擦除 / 清空 / 显隐图标工具栏。
    /// </summary>
    public sealed class BattleFoodActionBar : MonoBehaviour
    {
        private static readonly Color InactiveToolColor = new Color(1f, 1f, 1f, 0.5f);
        private static readonly Color DrawActiveColor = new Color(0.34f, 0.77f, 1f, 1f);
        private static readonly Color EraseActiveColor = new Color(1f, 0.34f, 0.34f, 1f);
        private static readonly Color ClearColor = new Color(1f, 1f, 1f, 0.72f);
        private static readonly Color VisibleColor = Color.white;
        private static readonly Color HiddenColor = new Color(1f, 1f, 1f, 0.28f);

        [SerializeField] private Button _eatButton;
        [SerializeField] private Button _recipeInfoButton;
        [SerializeField] private TMP_Text _recipeInfoText;
        [SerializeField] private RawImage _doodleCanvas;
        [SerializeField] private Button _doodleDrawButton;
        [SerializeField] private Button _doodleEraseButton;
        [SerializeField] private Button _doodleClearButton;
        [SerializeField] private Button _doodleToggleButton;
        [SerializeField] private Image _doodleDrawIcon;
        [SerializeField] private Image _doodleEraseIcon;
        [SerializeField] private Image _doodleClearIcon;
        [SerializeField] private Image _doodleToggleIcon;
        [SerializeField] private Sprite _doodleDrawSprite;
        [SerializeField] private Sprite _doodleDrawGlowSprite;
        [SerializeField] private Sprite _doodleEraseSprite;
        [SerializeField] private Sprite _doodleEraseGlowSprite;
        [SerializeField] private Sprite _doodleClearSprite;

        private CanvasGroup _formInteractionGroup;
        private CanvasGroup _toolsInteractionGroup;
        private bool _hasRecipeInspectAction;
        private int? _recipeCountPresentationOverride;

        public RectTransform SettleRect =>
            _eatButton != null ? _eatButton.transform as RectTransform : transform as RectTransform;
        public RectTransform RecipeInfoButtonRect =>
            _recipeInfoButton != null ? _recipeInfoButton.transform as RectTransform : null;

        public void Bind(
            Action onEat,
            Action onRecipeInspect,
            Action onDoodleDraw,
            Action onDoodleErase,
            Action onDoodleClear,
            Action onDoodleToggle)
        {
            UIButtonSoundFeedback.Install(_eatButton);
            UIButtonSoundFeedback.Install(_recipeInfoButton);
            UIButtonSoundFeedback.Install(_doodleDrawButton);
            UIButtonSoundFeedback.Install(_doodleEraseButton);
            UIButtonSoundFeedback.Install(_doodleClearButton);
            UIButtonSoundFeedback.Install(_doodleToggleButton);

            Wire(_eatButton, onEat);
            Wire(_recipeInfoButton, onRecipeInspect);
            Wire(_doodleDrawButton, onDoodleDraw);
            Wire(_doodleEraseButton, onDoodleErase);
            Wire(_doodleClearButton, onDoodleClear);
            Wire(_doodleToggleButton, onDoodleToggle);
            _hasRecipeInspectAction = onRecipeInspect != null;
        }

        public void SetRecipeCountPresentationOverride(int? count)
        {
            _recipeCountPresentationOverride = count.HasValue
                ? Mathf.Max(0, count.Value)
                : null;
        }

        public void SetVisible(bool visible)
        {
            if (!visible)
            {
                SetInteractionLocked(false);
            }

            if (_doodleCanvas != null && _doodleCanvas.gameObject.activeSelf != visible)
            {
                _doodleCanvas.gameObject.SetActive(visible);
            }

            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
            }
        }

        public void Refresh(bool food, BattleSession session, BattleWorldController world)
        {
            bool doodleReady = food && world != null;
            BattleDoodleTool tool = world != null ? world.DoodleTool : BattleDoodleTool.None;
            bool toolActive = doodleReady && tool != BattleDoodleTool.None;
            SetInteractionLocked(toolActive);
            RefreshRecipeInfo(food, session);

            if (_eatButton != null)
            {
                _eatButton.interactable = food
                    && session != null
                    && !session.IsSettled
                    && (world == null || !world.IsFoodInteractionBusy)
                    && !toolActive;
            }

            // 涂鸦工具栏在绘制期间仍是唯一可交互区域：画笔和橡皮需要能直接互相切换，
            // 清空与显隐也不应要求玩家先退出当前工具。
            SetInteractable(_doodleDrawButton, doodleReady);
            SetInteractable(_doodleEraseButton, doodleReady);
            SetInteractable(_doodleClearButton, doodleReady);
            SetInteractable(_doodleToggleButton, doodleReady);

            if (world != null)
            {
                world.BindDoodleOutput(_doodleCanvas);
            }
            else if (_doodleCanvas != null)
            {
                _doodleCanvas.texture = null;
                _doodleCanvas.enabled = false;
            }

            bool visible = world != null && world.IsDoodleVisible;
            SetToolVisual(
                _doodleDrawIcon,
                tool == BattleDoodleTool.Draw,
                _doodleDrawSprite,
                _doodleDrawGlowSprite,
                DrawActiveColor);
            SetToolVisual(
                _doodleEraseIcon,
                tool == BattleDoodleTool.Erase,
                _doodleEraseSprite,
                _doodleEraseGlowSprite,
                EraseActiveColor);

            if (_doodleClearIcon != null)
            {
                _doodleClearIcon.sprite = _doodleClearSprite;
                _doodleClearIcon.color = ClearColor;
            }

            if (_doodleToggleIcon != null)
            {
                _doodleToggleIcon.color = visible ? VisibleColor : HiddenColor;
            }
        }

        private void RefreshRecipeInfo(bool food, BattleSession session)
        {
            int placeable = 0;
            int blocked = 0;
            RecipeSlot slot = session != null && session.Slots.Count > 0
                ? session.Slots[0]
                : null;
            if (session != null && slot != null)
            {
                for (int i = 0; i < slot.Entries.Count; i++)
                {
                    if (session.CanFitRecipeEntry(0, i))
                    {
                        placeable++;
                    }
                    else
                    {
                        blocked++;
                    }
                }
            }

            if (_recipeInfoText != null)
            {
                _recipeInfoText.text = _recipeCountPresentationOverride.HasValue
                    ? $"{_recipeCountPresentationOverride.Value} 份"
                    : $"{placeable}<color=#35B84A>✓</color> {blocked}<color=#E33A3A>×</color>";
            }

            SetInteractable(
                _recipeInfoButton,
                food && session != null && _hasRecipeInspectAction);
        }

        private void OnDisable()
        {
            SetInteractionLocked(false);
        }

        private void SetInteractionLocked(bool locked)
        {
            EnsureInteractionGroups();
            if (_formInteractionGroup != null)
            {
                _formInteractionGroup.interactable = !locked;
            }

            if (_toolsInteractionGroup != null)
            {
                _toolsInteractionGroup.interactable = true;
                _toolsInteractionGroup.blocksRaycasts = true;
                _toolsInteractionGroup.ignoreParentGroups = true;
            }
        }

        private void EnsureInteractionGroups()
        {
            if (_formInteractionGroup == null)
            {
                BattleForm form = GetComponentInParent<BattleForm>(true);
                _formInteractionGroup = form != null ? form.GetComponent<CanvasGroup>() : null;
            }

            if (_toolsInteractionGroup == null)
            {
                Transform tools = transform.Find("DoodleTools");
                _toolsInteractionGroup = tools != null ? tools.GetComponent<CanvasGroup>() : null;
            }
        }

        private static void SetToolVisual(Image icon, bool active, Sprite normal, Sprite glow, Color activeColor)
        {
            if (icon == null)
            {
                return;
            }

            icon.sprite = active && glow != null ? glow : normal;
            icon.color = active ? activeColor : InactiveToolColor;
        }

        private static void SetInteractable(Selectable selectable, bool interactable)
        {
            if (selectable != null)
            {
                selectable.interactable = interactable;
            }
        }

        private static void Wire(Button button, Action callback)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => callback?.Invoke());
        }
    }
}
