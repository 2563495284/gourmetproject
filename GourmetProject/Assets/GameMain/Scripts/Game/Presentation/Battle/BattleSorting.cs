using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 战斗世界的渲染分层：用命名 Sorting Layer 划分大层级（与 ProjectSettings/TagManager 中的定义一致），
    /// 每层内再用很小的 order 做细分，避免散落的 sortingOrder magic number。
    /// 层级从后到前：Background → DiningTable → Pieces → WorldUI → Fx → PiecesFlying。
    /// </summary>
    internal static class BattleSorting
    {
        /// <summary>桌布等背景。</summary>
        public const string Background = "Background";

        /// <summary>餐桌格子。</summary>
        public const string DiningTable = "DiningTable";

        /// <summary>已摆放的菜品（阴影 + 本体）。</summary>
        public const string Pieces = "Pieces";

        /// <summary>场景内按钮与分数/提示文字等世界 UI。</summary>
        public const string WorldUi = "WorldUI";

        /// <summary>结算演出特效：飘字、光脉冲等。</summary>
        public const string Fx = "Fx";

        /// <summary>上菜飞行中的菜品，临时压在所有静态层之上。</summary>
        public const string PiecesFlying = "PiecesFlying";

        /// <summary>玩家自由涂鸦笔迹，压在所有战斗内容之上（最顶层）。</summary>
        public const string Doodle = "Doodle";

        // —— 层内细分 order ——
        public const int OrderShadow = 0;
        public const int OrderBody = 10;
        public const int OrderButtonBg = 0;
        public const int OrderButtonLabel = 1;
        public const int OrderScoreFire = 5;
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
    }
}
