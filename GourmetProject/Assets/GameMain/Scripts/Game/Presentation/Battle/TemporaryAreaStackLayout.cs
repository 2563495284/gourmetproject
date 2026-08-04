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
    /// 临时桌横向压叠的纯布局计算。所有食物使用统一缩放，并保持固定的可见宽度比例；
    /// 数量增多或总宽超限时缩小整组，不再把水平间距压到近似重合。
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
            float stepUnscaled = maxWidth * Mathf.Clamp01(visibleRatio);
            float minRelativeEdge = float.MaxValue;
            float maxRelativeEdge = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                float centerOffset = i * stepUnscaled;
                minRelativeEdge = Mathf.Min(
                    minRelativeEdge,
                    centerOffset - safeFootprints[i].x * 0.5f);
                maxRelativeEdge = Mathf.Max(
                    maxRelativeEdge,
                    centerOffset + safeFootprints[i].x * 0.5f);
            }

            float groupWidthUnscaled = Mathf.Max(0.0001f, maxRelativeEdge - minRelativeEdge);
            float scale = Mathf.Min(
                1f,
                Mathf.Min(
                    contentRect.width / groupWidthUnscaled,
                    contentRect.height / maxHeight)
                * padding);
            scale = Mathf.Max(0.0001f, scale);

            float step = stepUnscaled * scale;
            float groupCenterOffset = (minRelativeEdge + maxRelativeEdge) * 0.5f * scale;
            float start = contentRect.center.x - groupCenterOffset;
            var slots = new TemporaryAreaStackSlot[count];
            for (int i = 0; i < count; i++)
            {
                slots[i] = new TemporaryAreaStackSlot(
                    new Vector2(start + i * step, contentRect.center.y),
                    scale);
            }

            return slots;
        }
    }

    /// <summary>
    /// 临时桌重叠命中解析：集合顺序与绘制顺序一致，末尾（最新、最右）优先。
    /// </summary>
    internal static class TemporaryAreaPointerHit
    {
        public static bool IsTopmostAt(
            DishPieceView candidate,
            IReadOnlyList<DishPieceView> orderedPieces,
            Vector2 world,
            DishPieceView draggingPiece = null)
        {
            if (candidate == null || orderedPieces == null)
            {
                return false;
            }

            if (draggingPiece != null)
            {
                return candidate == draggingPiece;
            }

            for (int i = orderedPieces.Count - 1; i >= 0; i--)
            {
                DishPieceView piece = orderedPieces[i];
                if (piece != null
                    && piece.gameObject.activeInHierarchy
                    && piece.ContainsWorldPoint(world))
                {
                    return piece == candidate;
                }
            }

            return false;
        }
    }
}
