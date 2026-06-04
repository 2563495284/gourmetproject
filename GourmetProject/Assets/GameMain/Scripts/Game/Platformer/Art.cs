using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.Platformer
{
    /// <summary>
    /// 精灵资源加载缓存（Resources.Load）。路径相对 Resources/，对应资源清单文档第五节。
    /// </summary>
    public static class Art
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite Load(string resourcePath)
        {
            if (Cache.TryGetValue(resourcePath, out Sprite cached)) return cached;
            Sprite sprite = Resources.Load<Sprite>(resourcePath);
            Cache[resourcePath] = sprite;
            if (sprite == null)
            {
                Debug.LogWarning($"[Art] 未找到精灵: {resourcePath}");
            }
            return sprite;
        }

        // —— 常用路径 ——
        public const string PlayerIdle = "Sprites/Characters/player_idle";
        public const string PlayerLighter = "Sprites/Characters/player_lighter";
        public const string PlayerJump = "Sprites/Characters/player_jump";
        public const string PlayerWallslide = "Sprites/Characters/player_wallslide";

        public const string PlatformTile = "Sprites/Items/platform_tile";
        public const string WallTile = "Sprites/Items/wall_tile";
        public const string SlopeLeft = "Sprites/Items/slope_left";
        public const string SlopeRight = "Sprites/Items/slope_right";
        public const string Spikes = "Sprites/Items/spikes";
        public const string CheckpointInactive = "Sprites/Items/checkpoint_inactive";
        public const string CheckpointActive = "Sprites/Items/checkpoint_active";
        public const string CheckpointEndpoint = "Sprites/Items/checkpoint_endpoint";
        public const string Lighthouse = "Sprites/Items/lighthouse";

        // —— 怪物（Sprites/Characters/Monsters/）——
        public const string Mosquito = "Sprites/Characters/Monsters/mosquito";
        public const string Shadow = "Sprites/Characters/Monsters/shadow";
        public const string Moth = "Sprites/Characters/Monsters/moth";
        public const string LightEater = "Sprites/Characters/Monsters/light_eater";
        public const string LightScale = "Sprites/Characters/Monsters/light_scale";
        public const string Firefly = "Sprites/Characters/Monsters/firefly";
        public const string Vine = "Sprites/Characters/Monsters/vine";
        public const string AmbushSpider = "Sprites/Characters/Monsters/ambush_spider";
        public const string MistSpirit = "Sprites/Characters/Monsters/mist_spirit";
        public const string EchoBat = "Sprites/Characters/Monsters/echo_bat";
        public const string StoneEyeClosed = "Sprites/Characters/Monsters/stone_eye_closed";
        public const string StoneEyeOpen = "Sprites/Characters/Monsters/stone_eye_open";
        public const string LightShadowBug = "Sprites/Characters/Monsters/light_shadow_bug";

        public const string RedDot = "Sprites/UI/red_dot";
        public const string BlueDot = "Sprites/UI/blue_dot";
        public const string ParticleDust = "Sprites/UI/particle_dust";
        public const string InvincibilityRing = "Sprites/UI/invincibility_ring";

        // —— 远景背景（Limbo 风格分层）——
        public const string BgSky = "Sprites/Backgrounds/bg_sky";
        public const string BgFarMountains = "Sprites/Backgrounds/bg_far_mountains";
        public const string BgMidForest = "Sprites/Backgrounds/bg_mid_forest";
        public const string BgNearTrees = "Sprites/Backgrounds/bg_near_trees";
        public const string BgStar = "Sprites/Backgrounds/star_background";
        public const string BgMountain = "Sprites/Backgrounds/mountain_silhouette";
    }
}
