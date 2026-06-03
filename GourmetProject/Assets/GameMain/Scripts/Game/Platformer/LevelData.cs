using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.Platformer
{
    /// <summary>斜坡朝向。Right = 左低右高，Left = 左高右低（对应设计文档 'r' / 'l'）。</summary>
    public enum SlopeDir
    {
        Right,
        Left,
    }

    /// <summary>怪物种类（首版仅蚊群、暗影，其余预留）。</summary>
    public enum MonsterKind
    {
        Mosquito,
        Shadow,
    }

    public struct SlopeData
    {
        public AABB Rect;       // 斜坡所占矩形区域（底边为平台高度）
        public SlopeDir Dir;
    }

    public struct CheckpointData
    {
        public Vector2 Pos;     // 触发中心
        public bool IsEndpoint; // 终点（灯塔）
    }

    public struct MonsterSpawn
    {
        public MonsterKind Kind;
        public Vector2 Pos;
    }

    /// <summary>
    /// 一局关卡的纯数据描述（世界单位，Y 轴向上，起点在底部）。由 LevelGenerator 程序化产出，
    /// LevelBuilder 负责把它实例化为可见 GameObject 与碰撞列表。
    /// </summary>
    public sealed class LevelData
    {
        public readonly List<AABB> Platforms = new List<AABB>();
        public readonly List<AABB> Walls = new List<AABB>();
        public readonly List<SlopeData> Slopes = new List<SlopeData>();
        public readonly List<AABB> Spikes = new List<AABB>();
        public readonly List<CheckpointData> Checkpoints = new List<CheckpointData>();
        public readonly List<MonsterSpawn> Spawns = new List<MonsterSpawn>();

        /// <summary>固体碰撞体（平台 + 墙），玩家分轴扫描用。斜坡单独处理。</summary>
        public readonly List<AABB> Solids = new List<AABB>();

        public Vector2 StartPos;     // 玩家初始位置（左下角）
        public float WorldHeight;    // 世界总高（顶端 Y）
        public float FallDeathY;     // 低于此 Y 判定坠落死亡
        public int PlatformCount;    // 主路径平台数量
    }
}
