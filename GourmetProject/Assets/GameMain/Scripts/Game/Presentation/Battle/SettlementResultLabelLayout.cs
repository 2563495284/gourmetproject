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
            float horizontalOverlapRatio,
            float viewportPadding = DefaultViewportPadding)
        {
            int count = Mathf.Max(1, slot.Count);
            int index = Mathf.Clamp(slot.Index, 0, count - 1);
            float width = Mathf.Max(0.0001f, footprintWorld.x);

            if (count <= 1)
            {
                return baseAnchor;
            }

            float horizontalPitch = width
                * (1f - Mathf.Clamp(horizontalOverlapRatio, 0f, 0.75f));
            float centeredIndex = index - (count - 1) * 0.5f;
            Vector3 rawPosition = baseAnchor
                + Vector3.right * (centeredIndex * horizontalPitch);
            if (camera == null)
            {
                return rawPosition;
            }

            Vector3 baseViewport = camera.WorldToViewportPoint(baseAnchor);
            if (baseViewport.z <= 0f || !IsFinite(baseViewport))
            {
                return rawPosition;
            }

            float viewportWidth = ViewportDistance(
                camera,
                baseAnchor,
                camera.transform.right * width);
            if (viewportWidth <= 0.000001f)
            {
                return rawPosition;
            }

            float safePadding = Mathf.Clamp(viewportPadding, 0f, 0.45f);
            float minCenterX = safePadding + viewportWidth * 0.5f;
            float maxCenterX = 1f - safePadding - viewportWidth * 0.5f;
            if (minCenterX > maxCenterX)
            {
                return rawPosition;
            }

            Vector3 rawViewport = camera.WorldToViewportPoint(rawPosition);
            float groupMinX = float.PositiveInfinity;
            float groupMaxX = float.NegativeInfinity;
            for (int candidateIndex = 0; candidateIndex < count; candidateIndex++)
            {
                float candidateCenteredIndex = candidateIndex - (count - 1) * 0.5f;
                Vector3 candidatePosition = baseAnchor
                    + Vector3.right * (candidateCenteredIndex * horizontalPitch);
                Vector3 candidateViewport = camera.WorldToViewportPoint(
                    candidatePosition);
                groupMinX = Mathf.Min(groupMinX, candidateViewport.x);
                groupMaxX = Mathf.Max(groupMaxX, candidateViewport.x);
            }

            float translationX = FitGroupTranslation(
                groupMinX,
                groupMaxX,
                minCenterX,
                maxCenterX);

            Vector3 resolvedViewport = new(
                rawViewport.x + translationX,
                rawViewport.y,
                baseViewport.z);
            Vector3 resolved = camera.ViewportToWorldPoint(resolvedViewport);
            resolved.y = baseAnchor.y;
            resolved.z = baseAnchor.z;
            return resolved;
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
