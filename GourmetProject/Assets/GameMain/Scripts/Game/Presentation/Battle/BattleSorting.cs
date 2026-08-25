using UnityEngine;
using TMPro;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 经营挑战世界的渲染分层：用命名 Sorting Layer 划分大层级（与 ProjectSettings/TagManager 中的定义一致），
    /// 每层内再用很小的 order 做细分，避免散落的 sortingOrder magic number。
    /// 层级从后到前：Background → DiningTable → Pieces → WorldUI → Fx → PiecesFlying。
    /// </summary>
    internal static class BattleSorting
    {
        /// <summary>桌布等背景。</summary>
        public const string Background = "Background";

        /// <summary>餐桌格。</summary>
        public const string DiningTable = "DiningTable";

        /// <summary>已摆放的食物（阴影 + 本体）。</summary>
        public const string Pieces = "Pieces";

        /// <summary>场景内按钮与分数/提示文字等世界 UI。</summary>
        public const string WorldUi = "WorldUI";

        /// <summary>结算演出特效：飘字、光脉冲等。</summary>
        public const string Fx = "Fx";

        /// <summary>上菜飞行中的食物，临时压在所有静态层之上。</summary>
        public const string PiecesFlying = "PiecesFlying";

        /// <summary>玩家自由涂鸦笔迹，压在所有经营挑战内容之上（最顶层）。</summary>
        public const string Doodle = "Doodle";

        // —— 层内细分 order ——
        public const int OrderShadow = 0;
        public const int OrderBody = 10;
        public const int OrderTargetArrowLine = -2;
        public const int OrderTargetArrowHead = -1;
        public const int OrderButtonBg = 0;
        public const int OrderButtonLabel = 1;
        public const int OrderScoreFire = 5;
        public const int OrderDishBadge = 10;
        public const int OrderFloatingText = 10;
        public const int OrderScopeRegion = -100;

        /// <summary>把任意 Renderer（Sprite/Mesh/Line 等）归入指定 Sorting Layer 与层内 order。</summary>
        public static void Apply(Renderer renderer, string layer, int order = 0)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.sortingLayerName = layer;
            renderer.sortingOrder = order;
        }

        /// <summary>
        /// 把世界空间 TMP 主网格及多图集生成的全部子网格归入同一渲染层。
        /// 中文字形可能位于 Atlas 1+；此时只设置 text.renderer 会让 TMP_SubMesh 留在 Default 层。
        /// </summary>
        public static void Apply(TextMeshPro text, string layer, int order = 0)
        {
            if (text == null)
            {
                return;
            }

            text.ForceMeshUpdate(true, true);
            Apply(text.renderer, layer, order);

            TMP_SubMesh[] subMeshes = text.GetComponentsInChildren<TMP_SubMesh>(true);
            for (int i = 0; i < subMeshes.Length; i++)
            {
                Apply(subMeshes[i].renderer, layer, order);
            }
        }
    }
}
