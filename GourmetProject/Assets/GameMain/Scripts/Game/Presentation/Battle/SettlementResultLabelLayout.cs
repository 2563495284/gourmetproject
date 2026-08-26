using System;
using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    internal readonly struct ResultLabelLayoutOccurrence
    {
        public ResultLabelLayoutOccurrence(
            DishPieceView target,
            int targetKey)
        {
            Target = target;
            TargetKey = targetKey;
        }

        public DishPieceView Target { get; }

        public int TargetKey { get; }
    }

    internal readonly struct ResultLabelLayoutRequest
    {
        public ResultLabelLayoutRequest(
            int targetKey,
            Vector3 baseAnchor)
        {
            TargetKey = targetKey;
            BaseAnchor = baseAnchor;
        }

        public int TargetKey { get; }

        public Vector3 BaseAnchor { get; }
    }

    internal readonly struct ResultLabelLayoutPlacement
    {
        public ResultLabelLayoutPlacement(
            Vector3 position,
            float verticalDirection,
            int stackIndex)
        {
            Position = position;
            VerticalDirection = Mathf.Approximately(verticalDirection, 0f)
                ? 0f
                : verticalDirection < 0f ? -1f : 1f;
            StackIndex = Mathf.Max(0, stackIndex);
        }

        public Vector3 Position { get; }

        public float VerticalDirection { get; }

        public int StackIndex { get; }
    }

    internal sealed class ResultLabelLayoutPlan
    {
        private readonly ResultLabelLayoutPlacement[] _placements;

        public ResultLabelLayoutPlan(ResultLabelLayoutPlacement[] placements)
        {
            _placements = placements ?? Array.Empty<ResultLabelLayoutPlacement>();
        }

        public int Count => _placements.Length;

        public ResultLabelLayoutPlacement this[int index] => _placements[index];
    }

    internal static class SettlementResultLabelLayout
    {
        internal const float DefaultViewportPadding = 0.035f;
        internal const float FirstHorizontalOffsetRatio = 0.12f;
        internal const float HorizontalStaggerRatio = 0.06f;
        internal const float MaximumHorizontalOffsetRatio = 0.30f;
        internal const float FirstVerticalOffsetRatio = 0.45f;
        internal const float VerticalPitchRatio = 0.80f;

        public static ResultLabelLayoutPlan ResolveBatch(
            Camera camera,
            IReadOnlyList<ResultLabelLayoutRequest> requests,
            Vector2 footprintWorld,
            float viewportPadding = DefaultViewportPadding)
        {
            if (requests == null || requests.Count == 0)
            {
                return new ResultLabelLayoutPlan(
                    Array.Empty<ResultLabelLayoutPlacement>());
            }

            float width = Mathf.Max(0.0001f, Mathf.Abs(footprintWorld.x));
            float height = Mathf.Max(0.0001f, Mathf.Abs(footprintWorld.y));
            Vector3 horizontalAxis = camera != null
                ? camera.transform.right.normalized
                : Vector3.right;
            Vector3 verticalAxis = camera != null
                ? camera.transform.up.normalized
                : Vector3.up;
            var placements = new ResultLabelLayoutPlacement[requests.Count];
            var targetGroups = new Dictionary<int, List<int>>();
            var targetOccurrenceCounts = new Dictionary<int, int>();

            for (int i = 0; i < requests.Count; i++)
            {
                ResultLabelLayoutRequest request = requests[i];
                targetOccurrenceCounts.TryGetValue(
                    request.TargetKey,
                    out int targetOccurrenceIndex);
                targetOccurrenceCounts[request.TargetKey] =
                    targetOccurrenceIndex + 1;
                if (targetOccurrenceIndex == 0)
                {
                    // 每个目标的第一条完全锁定原演出锚点，不参与位置漂移。
                    placements[i] = new ResultLabelLayoutPlacement(
                        request.BaseAnchor,
                        0f,
                        0);
                    continue;
                }

                int offsetIndex = targetOccurrenceIndex - 1;

                float horizontalOffset = Mathf.Min(
                    MaximumHorizontalOffsetRatio * width,
                    (FirstHorizontalOffsetRatio
                        + HorizontalStaggerRatio * offsetIndex) * width);
                float verticalOffset = (
                    FirstVerticalOffsetRatio
                    + VerticalPitchRatio * offsetIndex) * height;
                Vector3 rawPosition = request.BaseAnchor
                    + horizontalAxis * horizontalOffset
                    - verticalAxis * verticalOffset;
                placements[i] = new ResultLabelLayoutPlacement(
                    rawPosition,
                    -1f,
                    targetOccurrenceIndex);

                if (!targetGroups.TryGetValue(
                        request.TargetKey,
                        out List<int> groupIndices))
                {
                    groupIndices = new List<int>();
                    targetGroups.Add(request.TargetKey, groupIndices);
                }

                groupIndices.Add(i);
            }

            if (camera != null)
            {
                float safePadding = Mathf.Clamp(viewportPadding, 0f, 0.45f);
                foreach (KeyValuePair<int, List<int>> group in targetGroups)
                {
                    FitTargetGroupInsideViewport(
                        camera,
                        requests,
                        placements,
                        group.Value,
                        width,
                        height,
                        safePadding);
                }
            }

            return new ResultLabelLayoutPlan(placements);
        }

        private static bool TryGetViewportPoint(
            Camera camera,
            Vector3 worldPosition,
            out Vector3 viewport)
        {
            viewport = default;
            if (camera == null || !IsFinite(worldPosition))
            {
                return false;
            }

            viewport = camera.WorldToViewportPoint(worldPosition);
            return viewport.z > 0f && IsFinite(viewport);
        }

        private static void FitTargetGroupInsideViewport(
            Camera camera,
            IReadOnlyList<ResultLabelLayoutRequest> requests,
            ResultLabelLayoutPlacement[] placements,
            IReadOnlyList<int> groupIndices,
            float footprintWidth,
            float footprintHeight,
            float safePadding)
        {
            if (groupIndices == null || groupIndices.Count == 0)
            {
                return;
            }

            float groupMinX = float.PositiveInfinity;
            float groupMaxX = float.NegativeInfinity;
            float groupMinY = float.PositiveInfinity;
            float groupMaxY = float.NegativeInfinity;
            for (int i = 0; i < groupIndices.Count; i++)
            {
                Vector3 position = placements[groupIndices[i]].Position;
                if (!TryGetFootprintViewportBounds(
                        camera,
                        position,
                        footprintWidth,
                        footprintHeight,
                        out Rect bounds))
                {
                    return;
                }

                groupMinX = Mathf.Min(groupMinX, bounds.xMin);
                groupMaxX = Mathf.Max(groupMaxX, bounds.xMax);
                groupMinY = Mathf.Min(groupMinY, bounds.yMin);
                groupMaxY = Mathf.Max(groupMaxY, bounds.yMax);
            }

            float translationX = FitGroupTranslation(
                groupMinX,
                groupMaxX,
                safePadding,
                1f - safePadding);
            float translationY = FitGroupTranslation(
                groupMinY,
                groupMaxY,
                safePadding,
                1f - safePadding);
            translationX = KeepOnlyOutwardTranslation(
                camera,
                requests,
                placements,
                groupIndices,
                translationX,
                useHorizontalAxis: true);
            translationY = KeepOnlyOutwardTranslation(
                camera,
                requests,
                placements,
                groupIndices,
                translationY,
                useHorizontalAxis: false);
            if (Mathf.Approximately(translationX, 0f)
                && Mathf.Approximately(translationY, 0f))
            {
                return;
            }

            for (int i = 0; i < groupIndices.Count; i++)
            {
                int placementIndex = groupIndices[i];
                ResultLabelLayoutPlacement placement = placements[placementIndex];
                Vector3 viewport = camera.WorldToViewportPoint(placement.Position);
                if (viewport.z <= 0f || !IsFinite(viewport))
                {
                    continue;
                }

                Vector3 resolved = camera.ViewportToWorldPoint(new Vector3(
                    viewport.x + translationX,
                    viewport.y + translationY,
                    viewport.z));
                placements[placementIndex] = new ResultLabelLayoutPlacement(
                    resolved,
                    placement.VerticalDirection,
                    placement.StackIndex);
            }
        }

        private static float KeepOnlyOutwardTranslation(
            Camera camera,
            IReadOnlyList<ResultLabelLayoutRequest> requests,
            ResultLabelLayoutPlacement[] placements,
            IReadOnlyList<int> groupIndices,
            float requestedTranslation,
            bool useHorizontalAxis)
        {
            if (Mathf.Approximately(requestedTranslation, 0f))
            {
                return 0f;
            }

            for (int i = 0; i < groupIndices.Count; i++)
            {
                int placementIndex = groupIndices[i];
                if (!TryGetViewportPoint(
                        camera,
                        requests[placementIndex].BaseAnchor,
                        out Vector3 anchorViewport)
                    || !TryGetViewportPoint(
                        camera,
                        placements[placementIndex].Position,
                        out Vector3 placementViewport))
                {
                    return 0f;
                }

                float outwardOffset = useHorizontalAxis
                    ? placementViewport.x - anchorViewport.x
                    : placementViewport.y - anchorViewport.y;
                if (outwardOffset * requestedTranslation < 0f)
                {
                    // 安全区修正不能抵消原始的远离偏移，更不能跨回原锚点另一侧。
                    return 0f;
                }
            }

            return requestedTranslation;
        }

        private static bool TryGetFootprintViewportBounds(
            Camera camera,
            Vector3 center,
            float width,
            float height,
            out Rect bounds)
        {
            bounds = default;
            if (!TryGetViewportPoint(camera, center, out _))
            {
                return false;
            }

            Vector3 horizontal = camera.transform.right.normalized * (width * 0.5f);
            Vector3 vertical = camera.transform.up.normalized * (height * 0.5f);
            Vector3[] corners =
            {
                center - horizontal - vertical,
                center - horizontal + vertical,
                center + horizontal - vertical,
                center + horizontal + vertical,
            };
            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 viewport = camera.WorldToViewportPoint(corners[i]);
                if (viewport.z <= 0f || !IsFinite(viewport))
                {
                    return false;
                }

                minX = Mathf.Min(minX, viewport.x);
                maxX = Mathf.Max(maxX, viewport.x);
                minY = Mathf.Min(minY, viewport.y);
                maxY = Mathf.Max(maxY, viewport.y);
            }

            bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
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
