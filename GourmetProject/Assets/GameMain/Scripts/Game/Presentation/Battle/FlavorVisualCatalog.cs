using System;
using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    internal enum FlavorVisualKind
    {
        Sweet = 0,
        Sour = 1,
        Bitter = 2,
        Salty = 3,
        Numb = 4,
        Umami = 5,
        Spicy = 6,
    }

    internal readonly struct FlavorVisualDescriptor
    {
        public FlavorVisualDescriptor(string id, string displayName, FlavorVisualKind kind, Color color)
        {
            Id = id;
            DisplayName = displayName;
            Kind = kind;
            Color = color;
        }

        public string Id { get; }

        public string DisplayName { get; }

        public FlavorVisualKind Kind { get; }

        public Color Color { get; }

        public int Bit => 1 << (int)Kind;
    }

    /// <summary>
    /// 风味样机使用的稳定视觉目录。顺序同时决定混合分区、轮廓段与粒子相位；
    /// 正式玩法数据仍由 GameplayDatabase 管理，本目录只服务表现层。
    /// </summary>
    internal static class FlavorVisualCatalog
    {
        public const int Capacity = 7;
        public const int AllMask = (1 << Capacity) - 1;

        private static readonly FlavorVisualDescriptor[] Ordered =
        {
            new("t_sweet", "甜", FlavorVisualKind.Sweet, new Color(1.00f, 0.38f, 0.67f, 1f)),
            new("t_sour", "酸", FlavorVisualKind.Sour, new Color(0.62f, 0.91f, 0.20f, 1f)),
            new("t_bitter", "苦", FlavorVisualKind.Bitter, new Color(0.34f, 0.29f, 0.12f, 1f)),
            new("t_salty", "咸", FlavorVisualKind.Salty, new Color(0.80f, 0.95f, 1.00f, 1f)),
            new("t_numb", "麻", FlavorVisualKind.Numb, new Color(0.62f, 0.34f, 0.94f, 1f)),
            new("t_fresh", "鲜", FlavorVisualKind.Umami, new Color(1.00f, 0.58f, 0.12f, 1f)),
            new("t_spicy", "辣", FlavorVisualKind.Spicy, new Color(1.00f, 0.20f, 0.09f, 1f)),
        };

        private static readonly Dictionary<string, FlavorVisualDescriptor> ById = BuildLookup();

        public static IReadOnlyList<FlavorVisualDescriptor> Descriptors => Ordered;

        public static bool TryResolve(string flavorId, out FlavorVisualDescriptor descriptor)
        {
            if (!string.IsNullOrEmpty(flavorId) && ById.TryGetValue(flavorId, out descriptor))
            {
                return true;
            }

            descriptor = default;
            return false;
        }

        public static int BuildMask(IReadOnlyList<string> flavorIds)
        {
            int mask = 0;
            if (flavorIds == null)
            {
                return mask;
            }

            for (int i = 0; i < flavorIds.Count; i++)
            {
                if (TryResolve(flavorIds[i], out FlavorVisualDescriptor descriptor))
                {
                    mask |= descriptor.Bit;
                }
            }

            return mask;
        }

        public static int CountBits(int mask)
        {
            mask &= AllMask;
            int count = 0;
            while (mask != 0)
            {
                mask &= mask - 1;
                count++;
            }

            return count;
        }

        public static List<string> FlavorIdsForMask(int mask, List<string> output = null)
        {
            output ??= new List<string>(Capacity);
            output.Clear();
            mask &= AllMask;
            for (int i = 0; i < Ordered.Length; i++)
            {
                if ((mask & Ordered[i].Bit) != 0)
                {
                    output.Add(Ordered[i].Id);
                }
            }

            return output;
        }

        public static Color ColorFor(FlavorVisualKind kind) => Ordered[(int)kind].Color;

        private static Dictionary<string, FlavorVisualDescriptor> BuildLookup()
        {
            var result = new Dictionary<string, FlavorVisualDescriptor>(Ordered.Length, StringComparer.Ordinal);
            for (int i = 0; i < Ordered.Length; i++)
            {
                result.Add(Ordered[i].Id, Ordered[i]);
            }

            return result;
        }
    }
}
