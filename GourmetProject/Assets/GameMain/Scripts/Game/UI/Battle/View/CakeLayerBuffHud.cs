using System.Collections.Generic;
using System.Linq;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>BattleForm 左上角的 buff 列表。目前承载欢乐蛋糕层数，后续可继续追加条目。</summary>
    public sealed class CakeLayerBuffHud : MonoBehaviour
    {
        private const string TipTitle = "欢乐蛋糕";

        [Header("Prefab Refs")]
        [SerializeField] private RectTransform _listRoot;
        [SerializeField] private GameObject _slot;
        [SerializeField] private Text _countText;
        [SerializeField] private TipHoverTrigger _hover;
        private string _desc = string.Empty;

        public void Bind(int layers, IReadOnlyList<CakeLayerBuffDef> buffs, ItemTipView tip)
        {
            if (_listRoot == null || _slot == null || _countText == null || _hover == null)
            {
                Debug.LogError($"{nameof(CakeLayerBuffHud)} prefab references are incomplete.", this);
                return;
            }

            if (layers <= 0)
            {
                Hide();
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
