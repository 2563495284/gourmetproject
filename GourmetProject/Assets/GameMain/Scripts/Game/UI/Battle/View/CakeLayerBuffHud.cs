using System.Collections.Generic;
using System.Linq;
using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>BattleForm 左上角的 buff 列表。目前承载欢乐蛋糕层数，后续可继续追加条目。</summary>
    public sealed class CakeLayerBuffHud : MonoBehaviour
    {
        private const string TipTitle = "欢乐蛋糕";

        [Header("Prefab Refs")]
        [SerializeField] private RectTransform _listRoot;
        [SerializeField] private GameObject _slot;
        [SerializeField] private TMP_Text _countText;
        [SerializeField] private TipHoverTrigger _hover;
        private string _desc = string.Empty;
        private GameObject _halfDaySlot;
        private TMP_Text _halfDayCountText;
        private TipHoverTrigger _halfDayHover;

        public void Bind(int layers, IReadOnlyList<CakeLayerBuffDef> buffs, ItemTipView tip)
        {
            if (_listRoot == null || _slot == null || _countText == null || _hover == null)
            {
                Debug.LogError($"{nameof(CakeLayerBuffHud)} prefab references are incomplete.", this);
                return;
            }

            _countText.text = layers.ToString();
            _desc = buffs == null
                ? string.Empty
                : string.Join("\n", buffs
                    .Where(def => def != null && !string.IsNullOrWhiteSpace(def.Desc))
                    .OrderBy(def => def.Order)
                    .Select(def => def.Desc.Trim()));

            if (tip != null)
            {
                _hover.SetTip(tip, () => tip.Bind(TipTitle, _desc));
            }
            else
            {
                _hover.ClearTip();
            }
            _slot.SetActive(true);
        }

        public void Hide()
        {
            if (_hover != null)
            {
                _hover.ClearTip();
            }

            _slot?.SetActive(false);
        }

        public void BindHalfDayCost(int stacks, ItemTipView tip)
        {
            EnsureHalfDaySlot();
            if (_halfDaySlot == null)
            {
                return;
            }

            if (stacks <= 0)
            {
                _halfDayHover?.ClearTip();
                _halfDaySlot.SetActive(false);
                return;
            }

            if (_halfDayCountText != null)
            {
                _halfDayCountText.text = stacks.ToString();
            }

            if (_halfDayHover != null)
            {
                if (tip != null)
                {
                    _halfDayHover.SetTip(
                        tip,
                        () => tip.Bind("半日券", $"下一次日常行动耗时减半\n剩余次数：{stacks}"));
                }
                else
                {
                    _halfDayHover.ClearTip();
                }
            }

            _halfDaySlot.SetActive(true);
        }

        private void EnsureHalfDaySlot()
        {
            if (_halfDaySlot != null || _slot == null || _listRoot == null)
            {
                return;
            }

            _halfDaySlot = Instantiate(_slot, _listRoot);
            _halfDaySlot.name = "HalfDayCostBuff";
            _halfDayCountText = _halfDaySlot.transform.Find("LayerCount")?.GetComponent<TMP_Text>();
            _halfDayHover = _halfDaySlot.GetComponent<TipHoverTrigger>();

            Transform iconRoot = _halfDaySlot.transform.Find("IconPlaceholder");
            Image icon = iconRoot != null ? iconRoot.GetComponent<Image>() : null;
            if (icon != null)
            {
                ItemDefinition item = ItemDefinition.Get(
                    GameApp.Config.Tables,
                    "item_active_half_next_action_cost",
                    cfg.ItemKind.Active);
                Sprite sprite = ContentIconLoader.LoadItem(item);
                if (sprite != null)
                {
                    icon.sprite = sprite;
                }
                else
                {
                    icon.color = new Color(1f, 0.78f, 0.25f, 1f);
                }
            }

            _halfDaySlot.SetActive(false);
        }
    }
}
