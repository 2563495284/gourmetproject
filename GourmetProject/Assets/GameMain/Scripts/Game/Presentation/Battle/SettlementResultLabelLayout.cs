using System;
using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    internal readonly struct ResultLabelLayoutOccurrence
    {
        public ResultLabelLayoutOccurrence(
            SettlementEffectGroup group,
            DishPieceView target,
            int targetKey)
        {
            Group = group;
            Target = target;
            TargetKey = targetKey;
        }

        public SettlementEffectGroup Group { get; }

        public DishPieceView Target { get; }

        public int TargetKey { get; }
    }

    internal readonly struct ResultLabelLayoutRequest
    {
        public ResultLabelLayoutRequest(
            int targetKey,
            Vector3 baseAnchor,
            Vector3 targetPosition,
            Vector3 sourcePosition,
            bool hasSource)
        {
            TargetKey = targetKey;
            BaseAnchor = baseAnchor;
            TargetPosition = targetPosition;
            SourcePosition = sourcePosition;
            HasSource = hasSource;
        }

        public int TargetKey { get; }

        public Vector3 BaseAnchor { get; }

        public Vector3 TargetPosition { get; }

        public Vector3 SourcePosition { get; }

        public bool HasSource { get; }
    }

    internal readonly struct ResultLabelLayoutPlacement
    {
        public ResultLabelLayoutPlacement(Vector3 position, float verticalDirection)
        {
            Position = position;
            VerticalDirection = verticalDirection < 0f ? -1f : 1f;
        }

        public Vector3 Position { get; }

        public float VerticalDirection { get; }
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

        private const float DirectionDeadZone = 0.02f;
        private const float ViewportCenterBand = 0.05f;

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
            var laneCounts = new Dictionary<LaneKey, int>();
            var targetGroups = new Dictionary<int, List<int>>();

            for (int i = 0; i < requests.Count; i++)
            {
                ResultLabelLayoutRequest request = requests[i];
                ResolveDirections(
                    camera,
                    request,
                    i,
                    out float horizontalDirection,
                    out float verticalDirection);

                var laneKey = new LaneKey(request.TargetKey, verticalDirection);
                laneCounts.TryGetValue(laneKey, out int laneIndex);
                laneCounts[laneKey] = laneIndex + 1;

                float horizontalOffset = Mathf.Min(
                    MaximumHorizontalOffsetRatio * width,
                    (FirstHorizontalOffsetRatio
                        + HorizontalStaggerRatio * laneIndex) * width);
                float verticalOffset = (
                    FirstVerticalOffsetRatio
                    + VerticalPitchRatio * laneIndex) * height;
                Vector3 rawPosition = request.BaseAnchor
                    + horizontalAxis * (horizontalDirection * horizontalOffset)
                    + verticalAxis * (verticalDirection * verticalOffset);
                placements[i] = new ResultLabelLayoutPlacement(
                    rawPosition,
                    verticalDirection);

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
                        placements,
                        group.Value,
                        width,
                        height,
                        safePadding);
                }
            }

            return new ResultLabelLayoutPlan(placements);
        }

        private static void ResolveDirections(
            Camera camera,
            ResultLabelLayoutRequest request,
            int detailIndex,
            out float horizontalDirection,
            out float verticalDirection)
        {
            bool hasTargetViewport = TryGetViewportPoint(
                camera,
                request.TargetPosition,
                out Vector3 targetViewport);
            Vector3 sourceViewport = default;
            bool hasSourceViewport = request.HasSource
                && TryGetViewportPoint(
                    camera,
                    request.SourcePosition,
                    out sourceViewport);

            float relativeX;
            float relativeY;
            float deadZone;
            if (hasTargetViewport && hasSourceViewport)
            {
                relativeX = sourceViewport.x - targetViewport.x;
                relativeY = sourceViewport.y - targetViewport.y;
                deadZone = DirectionDeadZone;
            }
            else if (request.HasSource)
            {
                Vector3 relativeWorld = request.SourcePosition
                    - request.TargetPosition;
                relativeX = relativeWorld.x;
                relativeY = relativeWorld.y;
                deadZone = 0.0001f;
            }
            else
            {
                relativeX = 0f;
                relativeY = 0f;
                deadZone = DirectionDeadZone;
            }

            if (Mathf.Abs(relativeX) > deadZone)
            {
                horizontalDirection = -Mathf.Sign(relativeX);
            }
            else
            {
                horizontalDirection = ResolveFallbackHorizontalDirection(
                    hasTargetViewport,
                    targetViewport.x,
                    request.TargetPosition.x,
                    request.TargetKey,
                    detailIndex);
            }

            if (Mathf.Abs(relativeY) > deadZone)
            {
                verticalDirection = -Mathf.Sign(relativeY);
                return;
            }

            float targetVerticalPosition = hasTargetViewport
                ? targetViewport.y - 0.5f
                : request.TargetPosition.y;
            if (targetVerticalPosition > ViewportCenterBand)
            {
                verticalDirection = -1f;
                return;
            }

            if (targetVerticalPosition < -ViewportCenterBand)
            {
                verticalDirection = 1f;
                return;
            }

            if (Mathf.Abs(relativeX) > deadZone)
            {
                // 来源在左边时标签放上方，来源在右边时放下方。
                verticalDirection = relativeX < 0f ? 1f : -1f;
                return;
            }

            verticalDirection = StableAlternatingDirection(
                request.TargetKey,
                detailIndex);
        }

        private static float ResolveFallbackHorizontalDirection(
            bool hasTargetViewport,
            float targetViewportX,
            float targetWorldX,
            int targetKey,
            int detailIndex)
        {
            float centeredX = hasTargetViewport
                ? targetViewportX - 0.5f
                : targetWorldX;
            if (centeredX > ViewportCenterBand)
            {
                return -1f;
            }

            if (centeredX < -ViewportCenterBand)
            {
                return 1f;
            }

            return StableAlternatingDirection(targetKey, detailIndex);
        }

        private static float StableAlternatingDirection(int targetKey, int detailIndex)
        {
            return ((targetKey ^ detailIndex) & 1) == 0 ? 1f : -1f;
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
                    placement.VerticalDirection);
            }
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

        private readonly struct LaneKey : IEquatable<LaneKey>
        {
            public LaneKey(int targetKey, float verticalDirection)
            {
                TargetKey = targetKey;
                VerticalDirection = verticalDirection < 0f ? -1 : 1;
            }

            private int TargetKey { get; }

            private int VerticalDirection { get; }

            public bool Equals(LaneKey other)
            {
                return TargetKey == other.TargetKey
                    && VerticalDirection == other.VerticalDirection;
            }

            public override bool Equals(object obj)
            {
                return obj is LaneKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (TargetKey * 397) ^ VerticalDirection;
                }
            }
        }
    }
}
