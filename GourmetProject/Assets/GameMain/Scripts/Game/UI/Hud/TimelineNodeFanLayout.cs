using System;
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

    /// <summary>同日节点扇形排布的可调参数，由 TimelineDayNodeGroupView.prefab 在 Inspector 上授权。</summary>
    [Serializable]
    public struct TimelineNodeFanSettings
    {
        [Tooltip("节点相对日期点的基础抬升高度")]
        public float BaseHeight;

        [Tooltip("相邻节点的理想水平步距")]
        public float Step;

        [Tooltip("整组节点的最大水平跨度")]
        public float MaximumSpan;

        [Tooltip("扇形中央相对两端的额外抬升")]
        public float ArcHeight;

        [Tooltip("最两侧节点的倾角，单位度")]
        public float MaximumAngle;

        public static TimelineNodeFanSettings Default => new TimelineNodeFanSettings
        {
            BaseHeight = 20f,
            Step = 29f,
            MaximumSpan = 124f,
            ArcHeight = 9f,
            MaximumAngle = 7f,
        };
    }

    /// <summary>
    /// 同日节点的重叠扇形算法。布局始终以日期点为中心，不因首尾日期做边缘偏移。
    /// </summary>
    public static class TimelineNodeFanLayout
    {
        public static TimelineNodeFanPose Calculate(
            int index,
            int count,
            in TimelineNodeFanSettings settings)
        {
            count = Mathf.Max(1, count);
            index = Mathf.Clamp(index, 0, count - 1);

            float span = Mathf.Min(
                Mathf.Max(0f, settings.MaximumSpan),
                Mathf.Max(0f, settings.Step) * (count - 1));
            float normalized = count > 1
                ? index / (float)(count - 1) * 2f - 1f
                : 0f;
            float x = normalized * span * 0.5f;
            float y = settings.BaseHeight + (1f - Mathf.Abs(normalized)) * settings.ArcHeight;
            return new TimelineNodeFanPose(
                new Vector2(x, y),
                -normalized * settings.MaximumAngle);
        }
    }
}
