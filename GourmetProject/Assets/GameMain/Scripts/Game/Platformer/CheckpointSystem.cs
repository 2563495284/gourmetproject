using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GourmetProject.Game.Platformer
{
    /// <summary>
    /// 检查点系统（设计文档第七节）：首次触碰激活并设为重生点 + 能量回满；任意触碰回满能量；
    /// 终点（灯塔金色光柱）触碰即胜利。检查点全程自带光圈，无视黑暗。
    /// </summary>
    public sealed class CheckpointSystem : MonoBehaviour
    {
        private sealed class Cp
        {
            public CheckpointData Data;
            public bool Activated;
            public Light2D Light;
            public SpriteRenderer Renderer;
            public Vector2 RespawnBottomLeft;
        }

        private static readonly Color GreyGlow = new Color(0.8f, 0.85f, 0.9f);
        private static readonly Color GreenGlow = new Color(0.35f, 1f, 0.45f);
        private static readonly Color GoldGlow = new Color(1f, 0.85f, 0.3f);

        private readonly List<Cp> _cps = new List<Cp>();
        private GameWorld _world;
        private LightingSystem _lighting;

        public Vector2 RespawnPoint { get; private set; }

        public void Build(GameWorld world, LightingSystem lighting, LevelData data)
        {
            _world = world;
            _lighting = lighting;
            RespawnPoint = data.StartPos;

            Sprite inactiveSprite = Art.Load(Art.CheckpointInactive);
            Sprite endpointSprite = Art.Load(Art.CheckpointEndpoint);

            foreach (CheckpointData cd in data.Checkpoints)
            {
                var cp = new Cp
                {
                    Data = cd,
                    Activated = false,
                    RespawnBottomLeft = new Vector2(
                        cd.Pos.x - GameConst.PlayerWidth * 0.5f,
                        cd.Pos.y - GameConst.Px(32f)),
                };

                Sprite sprite = cd.IsEndpoint ? endpointSprite : inactiveSprite;
                cp.Renderer = WorldRender.Create(cd.IsEndpoint ? "Endpoint" : "Checkpoint", sprite, cd.Pos, transform, 2);

                if (cd.IsEndpoint)
                {
                    cp.Light = _lighting.CreatePointLight("EndpointLight", cd.Pos, GoldGlow, GameConst.CheckpointEndpointRadius, 1.1f);
                }
                else
                {
                    cp.Light = _lighting.CreatePointLight("CheckpointLight", cd.Pos, GreyGlow, GameConst.CheckpointInactiveRadius, 0.7f);
                }

                _cps.Add(cp);
            }
        }

        /// <summary>
        /// 继续游戏：预先激活前 <paramref name="count"/> 个普通检查点（恢复视觉与重生点），
        /// 返回最后一个被激活检查点的重生坐标（无则返回起点）。
        /// </summary>
        public Vector2 PreActivate(int count)
        {
            int done = 0;
            for (int i = 0; i < _cps.Count && done < count; i++)
            {
                Cp cp = _cps[i];
                if (cp.Data.IsEndpoint || cp.Activated) continue;

                cp.Activated = true;
                RespawnPoint = cp.RespawnBottomLeft;
                ApplyActivatedVisual(cp);
                done++;
            }

            return RespawnPoint;
        }

        private static void ApplyActivatedVisual(Cp cp)
        {
            cp.Renderer.sprite = Art.Load(Art.CheckpointActive);
            if (cp.Light != null)
            {
                cp.Light.color = GreenGlow;
                cp.Light.pointLightOuterRadius = GameConst.CheckpointActiveRadius;
                cp.Light.pointLightInnerRadius = GameConst.CheckpointActiveRadius * 0.2f;
                cp.Light.intensity = 1.0f;
            }
        }

        public void Tick(Vector2 playerCenter)
        {
            for (int i = 0; i < _cps.Count; i++)
            {
                Cp cp = _cps[i];
                float trigger = cp.Data.IsEndpoint ? GameConst.CheckpointTriggerRadius : GameConst.CheckpointActiveRadius;
                if (Vector2.Distance(playerCenter, cp.Data.Pos) > trigger) continue;

                if (cp.Data.IsEndpoint)
                {
                    _world.OnVictory();
                    return;
                }

                // 任意触碰 → 能量回满。
                _world.Energy.Refill();

                if (!cp.Activated)
                {
                    cp.Activated = true;
                    RespawnPoint = cp.RespawnBottomLeft;
                    ApplyActivatedVisual(cp);

                    // 首次激活普通检查点 → 暂停对局并弹出 3 选 1 技能选择。
                    _world.OnCheckpointActivated();
                }
            }
        }
    }
}
