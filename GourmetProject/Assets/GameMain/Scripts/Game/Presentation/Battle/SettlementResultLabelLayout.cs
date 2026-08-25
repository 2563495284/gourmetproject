using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    internal readonly struct ResultLabelLayoutSlot
    {
        public ResultLabelLayoutSlot(int index, int count)
        {
            Count = Mathf.Max(1, count);
            Index = Mathf.Clamp(index, 0, Count - 1);
        }

        public int Index { get; }

        public int Count { get; }
    }

    internal sealed class ResultLabelSlotAllocator
    {
        private readonly Dictionary<int, int> _counts = new();
        private readonly Dictionary<int, int> _nextIndices = new();

        public void Add(int targetKey)
        {
            _counts.TryGetValue(targetKey, out int count);
            _counts[targetKey] = count + 1;
        }

        public ResultLabelLayoutSlot Take(int targetKey)
        {
            if (!_counts.TryGetValue(targetKey, out int count) || count <= 0)
            {
                return new ResultLabelLayoutSlot(0, 1);
            }

            _nextIndices.TryGetValue(targetKey, out int index);
            _nextIndices[targetKey] = index + 1;
            return new ResultLabelLayoutSlot(index, count);
        }
    }

    internal static class SettlementResultLabelLayout
    {
        internal const float DefaultViewportPadding = 0.035f;

        public static Vector3 Resolve(
            Camera camera,
            Vector3 baseAnchor,
            ResultLabelLayoutSlot slot,
            Vector2 footprintWorld,
            float rowGapWorld,
            float columnGapWorld,
            float viewportPadding = DefaultViewportPadding)
        {
            int count = Mathf.Max(1, slot.Count);
            int index = Mathf.Clamp(slot.Index, 0, count - 1);
            float width = Mathf.Max(0.0001f, footprintWorld.x);
            float height = Mathf.Max(0.0001f, footprintWorld.y);
            float rowGap = Mathf.Max(0f, rowGapWorld);

            if (count <= 1)
            {
                return baseAnchor;
            }

            if (camera == null)
            {
                return baseAnchor + Vector3.down * (index * (height + rowGap));
            }

            Vector3 baseViewport = camera.WorldToViewportPoint(baseAnchor);
            if (baseViewport.z <= 0f || !IsFinite(baseViewport))
            {
                return baseAnchor + Vector3.down * (index * (height + rowGap));
            }

            float viewportWidth = ViewportDistance(
                camera,
                baseAnchor,
                camera.transform.right * width);
            float viewportHeight = ViewportDistance(
                camera,
                baseAnchor,
                camera.transform.up * height);
            float rowPitch = viewportHeight + ViewportDistance(
                camera,
                baseAnchor,
                camera.transform.up * rowGap);
            float columnPitch = viewportWidth + ViewportDistance(
                camera,
                baseAnchor,
                camera.transform.right * Mathf.Max(0f, columnGapWorld));
            if (viewportWidth <= 0.000001f
                || viewportHeight <= 0.000001f
                || rowPitch <= 0.000001f
                || columnPitch <= 0.000001f)
            {
                return baseAnchor + Vector3.down * (index * (height + rowGap));
            }

            float safePadding = Mathf.Clamp(viewportPadding, 0f, 0.45f);
            float minCenterX = safePadding + viewportWidth * 0.5f;
            float maxCenterX = 1f - safePadding - viewportWidth * 0.5f;
            float minCenterY = safePadding + viewportHeight * 0.5f;
            float maxCenterY = 1f - safePadding - viewportHeight * 0.5f;
            if (minCenterX > maxCenterX || minCenterY > maxCenterY)
            {
                return baseAnchor + Vector3.down * (index * (height + rowGap));
            }

            float layoutBaseX = Mathf.Clamp(baseViewport.x, minCenterX, maxCenterX);
            float layoutBaseY = Mathf.Clamp(baseViewport.y, minCenterY, maxCenterY);
            int rowsPerColumn = Mathf.Max(
                1,
                Mathf.FloorToInt((layoutBaseY - minCenterY) / rowPitch) + 1);
            rowsPerColumn = Mathf.Min(rowsPerColumn, count);
            int columnCount = Mathf.CeilToInt((float)count / rowsPerColumn);
            float inwardDirection = layoutBaseX >= 0.5f ? -1f : 1f;

            float lastColumnX = layoutBaseX
                + inwardDirection * (columnCount - 1) * columnPitch;
            float groupMinX = Mathf.Min(layoutBaseX, lastColumnX);
            float groupMaxX = Mathf.Max(layoutBaseX, lastColumnX);
            float groupMinY = layoutBaseY - (rowsPerColumn - 1) * rowPitch;
            float groupMaxY = layoutBaseY;
            float translationX = FitGroupTranslation(
                groupMinX,
                groupMaxX,
                minCenterX,
                maxCenterX);
            float translationY = FitGroupTranslation(
                groupMinY,
                groupMaxY,
                minCenterY,
                maxCenterY);

            int column = index / rowsPerColumn;
            int row = index % rowsPerColumn;
            Vector3 resolvedViewport = new(
                layoutBaseX + inwardDirection * column * columnPitch + translationX,
                layoutBaseY - row * rowPitch + translationY,
                baseViewport.z);
            return camera.ViewportToWorldPoint(resolvedViewport);
        }

        private static float ViewportDistance(
            Camera camera,
            Vector3 anchor,
            Vector3 worldOffset)
        {
            Vector3 from = camera.WorldToViewportPoint(anchor);
            Vector3 to = camera.WorldToViewportPoint(anchor + worldOffset);
            return Vector2.Distance(from, to);
        }

        private static float FitGroupTranslation(
            float groupMin,
            float groupMax,
            float safeMin,
            float safeMax)
        {
            float groupSize = groupMax - groupMin;
            float safeSize = safeMax - safeMin;
            if (groupSize > safeSize)
            {
                return (safeMin + safeMax - groupMin - groupMax) * 0.5f;
            }

            if (groupMin < safeMin)
            {
                return safeMin - groupMin;
            }

            return groupMax > safeMax ? safeMax - groupMax : 0f;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
