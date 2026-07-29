using UnityEngine;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>同日节点气泡的纯布局结果。</summary>
    public readonly struct TimelineNodeFanPose
    {
        public TimelineNodeFanPose(Vector2 position, float angle)
        {
            Position = position;
            Angle = angle;
        }

        public Vector2 Position { get; }

        public float Angle { get; }
    }

    /// <summary>
    /// 同日节点的重叠扇形算法。布局始终以日期点为中心，不因首尾日期做边缘偏移。
    /// </summary>
    public static class TimelineNodeFanLayout
    {
        private const float BaseHeight = 20f;
        private const float PreferredStep = 29f;
        private const float MaximumSpan = 124f;
        private const float ArcHeight = 9f;
        private const float MaximumAngle = 7f;

        public static TimelineNodeFanPose Calculate(int index, int count)
        {
            count = Mathf.Max(1, count);
            index = Mathf.Clamp(index, 0, count - 1);

            float span = Mathf.Min(MaximumSpan, PreferredStep * (count - 1));
            float normalized = count > 1
                ? index / (float)(count - 1) * 2f - 1f
                : 0f;
            float x = normalized * span * 0.5f;
            float y = BaseHeight + (1f - Mathf.Abs(normalized)) * ArcHeight;
            return new TimelineNodeFanPose(
                new Vector2(x, y),
                -normalized * MaximumAngle);
        }
    }
}
