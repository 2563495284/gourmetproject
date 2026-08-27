using System.Collections.Generic;
using System.Linq;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using TMPro;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>BattleForm 左上角的 buff 列表，仅承载实际战斗 Buff（当前为欢乐蛋糕层数）。</summary>
    public sealed class CakeLayerBuffHud : MonoBehaviour
    {
        private const string TipTitle = "欢乐蛋糕";

        [Header("Prefab Refs")]
        [SerializeField] private RectTransform _listRoot;
        [SerializeField] private GameObject _slot;
        [SerializeField] private TMP_Text _countText;
        [SerializeField] private TipHoverTrigger _hover;
        private string _desc = string.Empty;

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
    }
}
