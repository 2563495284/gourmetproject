using System;
using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    internal readonly struct TemporaryAreaStackSlot
    {
        public TemporaryAreaStackSlot(Vector2 center, float scale)
        {
            Center = center;
            Scale = scale;
        }

        public Vector2 Center { get; }

        public float Scale { get; }
    }

    /// <summary>
    /// 临时桌横向压叠的纯布局计算。所有菜使用统一缩放，优先让相邻菜露出约一半；
    /// 数量增多时只压缩水平间距，不再缩小单份预览。
    /// </summary>
    internal static class TemporaryAreaStackLayout
    {
        public static TemporaryAreaStackSlot[] Calculate(
            Rect contentRect,
            IReadOnlyList<Vector2> footprintSizes,
            float piecePaddingRatio,
            float visibleRatio)
        {
            int count = footprintSizes?.Count ?? 0;
            if (count == 0)
            {
                return Array.Empty<TemporaryAreaStackSlot>();
            }

            float safeWidth = Mathf.Max(0.0001f, contentRect.width);
            float safeHeight = Mathf.Max(0.0001f, contentRect.height);
            float maxWidth = 0.0001f;
            float maxHeight = 0.0001f;
            var safeFootprints = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                Vector2 footprint = footprintSizes[i];
                footprint.x = Mathf.Max(0.0001f, footprint.x);
                footprint.y = Mathf.Max(0.0001f, footprint.y);
                safeFootprints[i] = footprint;
                maxWidth = Mathf.Max(maxWidth, footprint.x);
                maxHeight = Mathf.Max(maxHeight, footprint.y);
            }

            float padding = Mathf.Clamp(piecePaddingRatio, 0.01f, 1f);
            float scale = Mathf.Min(
                1f,
                Mathf.Min(safeWidth / maxWidth, safeHeight / maxHeight) * padding);
            scale = Mathf.Max(0.0001f, scale);

            var scaledWidths = new float[count];
            float maxScaledWidth = 0f;
            for (int i = 0; i < count; i++)
            {
                scaledWidths[i] = safeFootprints[i].x * scale;
                maxScaledWidth = Mathf.Max(maxScaledWidth, scaledWidths[i]);
            }

            if (count == 1)
            {
                return new[]
                {
                    new TemporaryAreaStackSlot(contentRect.center, scale),
                };
            }

            float idealStep = maxScaledWidth * Mathf.Clamp01(visibleRatio);
            float step = FindLargestFittingStep(contentRect, scaledWidths, idealStep);
            GetStartRange(contentRect, scaledWidths, step, out float minStart, out float maxStart);

            float minRelativeEdge = float.MaxValue;
            float maxRelativeEdge = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                float centerOffset = i * step;
                minRelativeEdge = Mathf.Min(minRelativeEdge, centerOffset - scaledWidths[i] * 0.5f);
                maxRelativeEdge = Mathf.Max(maxRelativeEdge, centerOffset + scaledWidths[i] * 0.5f);
            }

            float centeredStart = contentRect.center.x - (minRelativeEdge + maxRelativeEdge) * 0.5f;
            float start = Mathf.Clamp(centeredStart, minStart, maxStart);
            var slots = new TemporaryAreaStackSlot[count];
            for (int i = 0; i < count; i++)
            {
                slots[i] = new TemporaryAreaStackSlot(
                    new Vector2(start + i * step, contentRect.center.y),
                    scale);
            }

            return slots;
        }

        private static float FindLargestFittingStep(
            Rect contentRect,
            IReadOnlyList<float> widths,
            float desiredStep)
        {
            if (desiredStep <= 0f || Fits(contentRect, widths, desiredStep))
            {
                return Mathf.Max(0f, desiredStep);
            }

            float low = 0f;
            float high = desiredStep;
            for (int i = 0; i < 24; i++)
            {
                float candidate = (low + high) * 0.5f;
                if (Fits(contentRect, widths, candidate))
                {
                    low = candidate;
                }
                else
                {
                    high = candidate;
                }
            }

            return low;
        }

        private static bool Fits(Rect contentRect, IReadOnlyList<float> widths, float step)
        {
            GetStartRange(contentRect, widths, step, out float minStart, out float maxStart);
            return minStart <= maxStart + 0.0001f;
        }

        private static void GetStartRange(
            Rect contentRect,
            IReadOnlyList<float> widths,
            float step,
            out float minStart,
            out float maxStart)
        {
            minStart = float.MinValue;
            maxStart = float.MaxValue;
            for (int i = 0; i < widths.Count; i++)
            {
                float centerOffset = i * step;
                float halfWidth = widths[i] * 0.5f;
                minStart = Mathf.Max(minStart, contentRect.xMin + halfWidth - centerOffset);
                maxStart = Mathf.Min(maxStart, contentRect.xMax - halfWidth - centerOffset);
            }
        }
    }
}
