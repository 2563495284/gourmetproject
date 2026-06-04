using UnityEngine;

namespace GourmetProject.Game.Platformer.Monsters
{
    /// <summary>
    /// 回声蝠（光驱赶型，蓝点）。关灯且近 → 周期性发出可见声波圈，声波扫过玩家造成眩晕 + 暴露位置；
    /// 开灯 → 远离。设计文档 5.3 / 15.5。
    /// </summary>
    public sealed class EchoBat : MonsterBase
    {
        private static readonly float Range = GameConst.Px(220f);
        private static readonly float FleeSpeed = GameConst.Px(110f);
        private static readonly float WaveSpeed = GameConst.Px(180f);
        private static readonly float MaxWaveRadius = GameConst.Px(170f);
        private const float EmitCooldown = 2.6f;

        private SpriteRenderer _ring;
        private float _emitCd = 1f;
        private float _waveRadius = -1f;   // <0 表示未发波
        private float _prevWaveRadius;
        private bool _hitThisWave;
        private float _phase;

        public override bool IsAttract => false;
        protected override Sprite BodySprite() => Art.Load(Art.EchoBat);

        public override void Init(GameWorld world, Vector2 spawn)
        {
            base.Init(world, spawn);
            _ring = WorldRender.CreateUnlit("SonicRing", Art.Load(Art.InvincibilityRing), spawn, transform, 49);
            _ring.transform.localPosition = Vector3.zero; // 跟随蝠本体（父级），声波以本体为中心。
            _ring.color = new Color(0.4f, 0.7f, 1f, 0f);
            _ring.enabled = false;
        }

        protected override void Behave(float dt, Vector2 playerCenter, bool lighterOn, float distToPlayer)
        {
            _phase += dt;

            if (lighterOn && distToPlayer < Range)
            {
                // 光下远离，取消声波。
                Vector2 away = (Pos - playerCenter).normalized;
                Pos += away * FleeSpeed * dt;
                SetBodyAlpha(0.2f);
                StopWave();
                return;
            }

            // 黑暗中小幅游弋。
            Pos = Vector2.MoveTowards(Pos, Spawn, GameConst.Px(20f) * dt)
                  + new Vector2(Mathf.Sin(_phase * 2f) * GameConst.Px(10f), 0f) * dt;
            SetBodyAlpha(0f);

            if (distToPlayer >= Range)
            {
                StopWave();
                return;
            }

            if (_waveRadius < 0f)
            {
                _emitCd -= dt;
                if (_emitCd <= 0f)
                {
                    _emitCd = EmitCooldown;
                    _waveRadius = 0f;
                    _prevWaveRadius = 0f;
                    _hitThisWave = false;
                    _ring.enabled = true;
                }
            }
            else
            {
                _prevWaveRadius = _waveRadius;
                _waveRadius += WaveSpeed * dt;
                UpdateRingVisual();

                // 声波扫过玩家（环半径越过玩家距离）→ 眩晕 + 暴露。
                if (!_hitThisWave && _prevWaveRadius < distToPlayer && distToPlayer <= _waveRadius)
                {
                    _hitThisWave = true;
                    World.Player.ApplyStun(GameConst.EchoStunDuration);
                    World.AddShake(GameConst.Px(40f));
                }

                if (_waveRadius >= MaxWaveRadius) StopWave();
            }
        }

        private void UpdateRingVisual()
        {
            float diameterUnits = _waveRadius * 2f;
            Sprite s = _ring.sprite;
            float baseSize = s != null && s.bounds.size.x > 0f ? s.bounds.size.x : 1f;
            float scale = diameterUnits / baseSize;
            _ring.transform.localScale = new Vector3(scale, scale, 1f);
            float fade = 1f - Mathf.Clamp01(_waveRadius / MaxWaveRadius);
            _ring.color = new Color(0.4f, 0.7f, 1f, 0.6f * fade);
        }

        private void StopWave()
        {
            _waveRadius = -1f;
            if (_ring != null) _ring.enabled = false;
        }
    }
}
