using System.Collections.Generic;
using GourmetProject.Game.Platformer.Monsters;
using UnityEngine;

namespace GourmetProject.Game.Platformer
{
    /// <summary>怪物种类（设计文档 5.2 光吸引 / 5.3 光驱赶 / 5.4 混合）。</summary>
    public enum MonsterKind
    {
        // 光吸引型（黑暗中=红点）
        Mosquito,       // 蚊群
        Moth,           // 飞蛾（贴附遮挡视野）
        LightEater,     // 光食虫（附着吸能）
        LightScale,     // 趋光飞鳞（俯冲撞击 + 硬直）
        Firefly,        // 萤火群（光团遮盖光圈）

        // 光驱赶型（黑暗中=蓝点）
        Shadow,         // 暗影（黑暗接触秒杀）
        Vine,           // 藤蔓（覆盖平台减速禁跳）
        AmbushSpider,   // 伏击蛛（底部伏击下落）
        FogWraith,      // 雾灵（区域扣能逼移动）
        EchoBat,        // 回声蝠（声波眩晕）

        // 混合型
        StoneEye,       // 石瞳（光化石/暗追击秒杀）
        LightShadowBug, // 光影虫（吸光充能/暗中引爆）
    }

    public struct CheckpointData
    {
        public Vector2 Pos;     // 触发中心
        public bool IsEndpoint; // 终点（灯塔）
    }

    /// <summary>
    /// 一局关卡的元数据描述（世界单位，Y 轴向上，起点在底部）。由 <c>LevelAssembler</c> 拼接预制段时填充：
    /// 地形/碰撞由预制段内 Tilemap(Collider) 承载，这里只保留检查点/怪物 spawn/起点/边界等运行时需要的数据。
    /// </summary>
    public sealed class LevelData
    {
        public readonly List<CheckpointData> Checkpoints = new List<CheckpointData>();

        /// <summary>关卡内由设计师摆放、拼接时收集到的怪物实例（已就位，待 MonsterManager 注册驱动）。</summary>
        public readonly List<MonsterBase> Monsters = new List<MonsterBase>();

        public Vector2 StartPos;     // 玩家初始位置（左下角）
        public float WorldHeight;    // 世界总高（顶端 Y）
        public float FallDeathY;     // 低于此 Y 判定坠落死亡
        public int PlatformCount;    // 拼接的预制段数量（保留语义）

        // —— 世界水平边界（预制段宽高可不同，相机据此 clamp）——
        public float WorldMinX;
        public float WorldMaxX;
        public float WorldWidth => Mathf.Max(0f, WorldMaxX - WorldMinX);
    }
}
