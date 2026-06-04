using UnityEngine;

namespace GourmetProject.Game.Platformer.Chunks
{
    /// <summary>预制段角色：起点教学 / 难度段 / 休息段（检查点） / 终点段（灯塔）。设计文档 13.2 整体结构。</summary>
    public enum ChunkRole
    {
        Start,
        Normal,
        Rest,
        End,
    }

    /// <summary>
    /// 预制段元信息（挂在 chunk 预制体根节点）。关卡由若干预制段按 Entry/Exit 锚点拼接而成，
    /// 地形/斜坡/地刺/怪物/检查点全部直接摆在预制段内，运行时由 <see cref="LevelAssembler"/> 读取拼接。
    /// </summary>
    public sealed class ChunkInfo : MonoBehaviour
    {
        [Tooltip("预制段角色：决定它在整体结构中的位置")]
        public ChunkRole Role = ChunkRole.Normal;

        [Tooltip("难度等级 1-5（仅 Normal 段有意义，设计文档 13.2）")]
        [Range(1, 5)]
        public int Difficulty = 1;

        [Tooltip("主题标签：拼接时尽量避免连续同标签（设计文档 13.3）")]
        public string ThemeTag = "";

        [Tooltip("入口锚点（与上一段出口对齐）。为空时用根节点。")]
        public Transform Entry;

        [Tooltip("出口锚点（与下一段入口对齐）。为空时用根节点。")]
        public Transform Exit;

        /// <summary>入口锚点的本地坐标（相对根节点）。</summary>
        public Vector3 EntryLocal => Entry != null ? Entry.localPosition : Vector3.zero;

        /// <summary>出口锚点的本地坐标（相对根节点）。</summary>
        public Vector3 ExitLocal => Exit != null ? Exit.localPosition : Vector3.zero;
    }
}
