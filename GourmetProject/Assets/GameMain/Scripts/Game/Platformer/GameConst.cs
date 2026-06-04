using UnityEngine;

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

        // —— 光照（半径，世界单位；设计文档 4.1 默认视野 95px）——
        public const float DefaultVisionRadius = 95f * PxToUnit;    // 5.9375
        public const float LighterBaseRadius = 230f * PxToUnit;    // 14.375
        public const float VisionLightIntensity = 1.75f;
        public const float CheckpointInactiveRadius = 55f * PxToUnit;
        public const float CheckpointActiveRadius = 80f * PxToUnit;
        public const float CheckpointEndpointRadius = 130f * PxToUnit;

        // —— 战争迷雾遮罩（VisionFogOverlay）：圆内全透明，圆外软渐变进黑雾 ——
        /// <summary>软边宽度 = 视野半径 × 本系数（屏幕高度归一化空间）。</summary>
        public const float VisionFogSoftEdgeFraction = 0.28f;
        public static readonly Color VisionFogColor = new Color(0.02f, 0.03f, 0.05f, 1f);
        /// <summary>圈内点光：内径比 + 衰减，让脚下地形可读。</summary>
        public const float VisionLightInnerRatio = 0.72f;
        public const float VisionLightFalloff = 0.5f;
        public const float LighterLightInnerRatio = 0.88f;
        public const float LighterLightFalloff = 0.55f;

        // —— 氛围：月光底 + 逗号全图照亮（与 LightingSystem / ParallaxBackground 同步）——
        public const float GlobalMoonIntensity = 0.48f;
        public const float GlobalRevealIntensity = 1.35f;
        public static readonly Color GlobalMoonColor = new Color(0.72f, 0.78f, 0.88f);
        public static readonly Color GlobalRevealColor = new Color(0.92f, 0.94f, 1f);
        public static readonly Color AtmosphereCameraBgNight = new Color(0.06f, 0.08f, 0.12f);
        public static readonly Color AtmosphereCameraBgReveal = new Color(0.14f, 0.16f, 0.20f);
        public static readonly Color[] ParallaxLayerColorsNight =
        {
            new Color(0.42f, 0.45f, 0.50f), // 天空：最亮，承担「月光雾」
            new Color(0.30f, 0.33f, 0.36f),
            new Color(0.26f, 0.28f, 0.31f),
            new Color(0.20f, 0.22f, 0.25f),
        };
        public static readonly Color[] ParallaxLayerColorsReveal =
        {
            new Color(0.58f, 0.60f, 0.64f),
            new Color(0.42f, 0.44f, 0.47f),
            new Color(0.36f, 0.38f, 0.41f),
            new Color(0.30f, 0.32f, 0.35f),
        };

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
