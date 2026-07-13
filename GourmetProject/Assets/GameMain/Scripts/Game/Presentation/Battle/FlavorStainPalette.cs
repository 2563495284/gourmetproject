using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 风味脏印调色板：flavorId -> 脏印颜色（rgb=色，a=强度）的表现层硬编码映射。
    /// 后续可迁移到配置表(tbflavor 增 color 字段)；这里先落地，未知 id 不画。
    /// </summary>
    internal static class FlavorStainPalette
    {
        private static readonly Dictionary<string, Color> Colors = new()
        {
            { "t_sour", new Color(0.36f, 0.82f, 0.30f, 0.85f) },   // 酸：绿
            { "t_rust", new Color(0.72f, 0.38f, 0.14f, 0.88f) },   // 锈：锈橙
            { "t_sweet", new Color(1.00f, 0.55f, 0.72f, 0.80f) },  // 甜：粉桃
            { "t_bitter", new Color(0.28f, 0.24f, 0.16f, 0.88f) }, // 苦：墨褐
            { "t_numb", new Color(0.60f, 0.40f, 0.78f, 0.82f) },   // 麻：花椒紫褐
            { "t_salty", new Color(0.90f, 0.90f, 0.86f, 0.75f) },  // 咸：浅灰白
            { "t_feast", new Color(1.00f, 0.82f, 0.28f, 0.82f) },  // 盛宴：金
            { "t_golden", new Color(1.00f, 0.82f, 0.28f, 0.85f) }, // 黄金：金
            { "t_spicy", new Color(1.00f, 0.23f, 0.19f, 0.88f) },  // 辣：红（配置未来补）
        };

        /// <summary>解析风味脏印颜色；未知 id 返回 false（不画脏印）。</summary>
        public static bool TryResolve(string flavorId, out Color color)
        {
            if (!string.IsNullOrEmpty(flavorId) && Colors.TryGetValue(flavorId, out color))
            {
                return true;
            }

            color = default;
            return false;
        }
    }
}
