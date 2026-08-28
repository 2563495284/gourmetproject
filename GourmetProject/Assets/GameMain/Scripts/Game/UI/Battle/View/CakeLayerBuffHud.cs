using System.Collections.Generic;
using System.Linq;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>BattleForm 左上角的 buff 列表，仅承载实际战斗 Buff（当前为欢乐蛋糕层数）。</summary>
    public sealed class CakeLayerBuffHud : MonoBehaviour
    {
        private const string TipTitle = "欢乐蛋糕";
        private const float CountOutlineWidth = 0.45f;
        private const float CountGraphicOutlineDistance = 1.5f;
        private static readonly Color32 CountFaceColor = new(255, 255, 255, 255);
        private static readonly Color32 CountOutlineColor = new(0, 0, 0, 255);

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

            ApplyCountTextStyle(_countText);
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

        internal static void ApplyCountTextStyle(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            // fontMaterial forces a private runtime instance, so the HUD can enable the
            // outline shader variant without mutating other users of the font material.
            Material material = text.fontMaterial;
            text.color = CountFaceColor;
            text.outlineColor = CountOutlineColor;
            text.outlineWidth = CountOutlineWidth;

            // Keep a pixel-based UI outline as well. This survives TMP material swaps and
            // guarantees a visible stroke at the HUD's small 22 px display size.
            Outline graphicOutline = text.GetComponent<Outline>();
            if (graphicOutline == null)
            {
                graphicOutline = text.gameObject.AddComponent<Outline>();
            }
            graphicOutline.effectColor = CountOutlineColor;
            graphicOutline.effectDistance = new Vector2(
                CountGraphicOutlineDistance,
                -CountGraphicOutlineDistance);
            graphicOutline.useGraphicAlpha = true;
            graphicOutline.enabled = true;

            if (material != null)
            {
                material.EnableKeyword(ShaderUtilities.Keyword_Outline);
                material.SetColor(ShaderUtilities.ID_FaceColor, CountFaceColor);
                material.SetColor(ShaderUtilities.ID_OutlineColor, CountOutlineColor);
                material.SetFloat(ShaderUtilities.ID_OutlineWidth, CountOutlineWidth);
            }

            text.UpdateMeshPadding();
            text.SetMaterialDirty();
            text.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
        }
    }
}
