namespace GourmetProject.Game.Platformer
{
    /// <summary>
    /// 《光影》核心数值常量。设计文档以像素为单位，本工程渲染 PPU=16，
    /// 这里统一把像素值转换为世界单位（1 unit = 16 px）一次，玩法层只用世界单位。
    /// 物理参数严格对应设计文档 6.2 / 6.3 / 6.4。
    /// </summary>
    public static class GameConst
    {
        /// <summary>每像素对应的世界单位（PPU = 16）。</summary>
        public const float PxToUnit = 1f / 16f;

        /// <summary>把像素值转换为世界单位。</summary>
        public static float Px(float px) => px * PxToUnit;

        // —— 物理（世界单位 / 秒）——
        public const float Gravity = 1700f * PxToUnit;        // 106.25
        public const float JumpSpeed = 660f * PxToUnit;       // 41.25
        public const float MaxRunSpeed = 230f * PxToUnit;     // 14.375
        public const float GroundAccel = 1600f * PxToUnit;    // 100
        public const float AirAccel = 800f * PxToUnit;        // 50
        public const float TerminalFall = 900f * PxToUnit;    // 56.25
        public const float CoyoteTime = 0.10f;
        public const float JumpBuffer = 0.12f;

        // —— Wall slide / jump ——
        public const float WallSlideMaxFall = 110f * PxToUnit;
        public const float WallJumpSpeedY = 627f * PxToUnit;
        public const float WallJumpSpeedX = 320f * PxToUnit;
        public const float WallStickDistance = 2f * PxToUnit;

        // —— 斜坡下滑力 ——
        public const float SlopeSlideStanding = 720f * PxToUnit;
        public const float SlopeSlideActive = 360f * PxToUnit;

        // —— 角色尺寸（16×32 px）——
        public const float PlayerWidth = 16f * PxToUnit;      // 1
        public const float PlayerHeight = 32f * PxToUnit;     // 2

        // —— 网格 / 关卡 ——
        public const float Tile = 16f * PxToUnit;             // 1
        public const float VerticalStep = 80f * PxToUnit;     // 5
        public const float HorizontalGap = 64f * PxToUnit;    // 4
        public const float WorldWidth = 1280f * PxToUnit;     // 80

        // —— 光照（半径，世界单位）——
        public const float DefaultVisionRadius = 95f * PxToUnit;   // 5.9375
        public const float LighterBaseRadius = 230f * PxToUnit;    // 14.375
        public const float CheckpointInactiveRadius = 55f * PxToUnit;
        public const float CheckpointActiveRadius = 80f * PxToUnit;
        public const float CheckpointEndpointRadius = 130f * PxToUnit;

        // —— 能量 ——
        public const float EnergyMax = 100f;
        public const float LighterDrainPerSec = 17f;   // %/s

        // —— 检查点 / 死亡 ——
        public const float CheckpointTriggerRadius = 40f * PxToUnit;
        public const float RespawnDelay = 1.0f;
        public const float InvincibleDuration = 2.0f;

        // —— 怪物威胁数值 ——
        public const float LightEaterDrainPerSec = 8f;    // 光食虫附着每秒吸取能量 %
        public const float FogWraithDrainPerSec = 10f;    // 雾灵接触每秒扣减能量 %（不致死，逼移动）
        public const float ScaleStunDuration = 0.3f;      // 趋光飞鳞撞击硬直
        public const float EchoStunDuration = 0.5f;       // 回声蝠声波眩晕

        // —— 局外成长（设计文档 13.10）——
        public const float MetaEnergyCapPerLevel = 10f;   // 每级初始能量上限 +10
        public const int MetaEnergyCapMaxLevel = 3;       // 能量上限最多 +30（上限明确）
    }
}
