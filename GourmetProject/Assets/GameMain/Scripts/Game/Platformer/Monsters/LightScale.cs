using UnityEngine;

namespace GourmetProject.Game.Platformer.Monsters
{
    /// <summary>
    /// 趋光飞鳞（光吸引型，红点）。开灯且近 → 周期性快速俯冲撞击，命中造成击退 + 硬直；
    /// 关灯 → 缓慢巡游。设计文档 5.2 / 15.5。
    /// </summary>
    public sealed class LightScale : MonsterBase
    {
        private static readonly float Range = GameConst.Px(180f);
        private static readonly float PatrolSpeed = GameConst.Px(40f);
        private static readonly float DiveSpeed = GameConst.Px(360f);
        private static readonly float HitDist = GameConst.Px(22f);
        private const float DiveDuration = 0.35f;
        private const float DiveCooldown = 1.5f;

        private float _diveCd;
        private float _diveTimer;
        private Vector2 _diveDir;
        private bool _hitThisDive;
        private float _phase;

        public override bool IsAttract => true;

        protected override void Behave(float dt, Vector2 playerCenter, bool lighterOn, float distToPlayer)
        {
            _phase += dt;
            _diveCd -= dt;

            if (lighterOn && distToPlayer < Range)
            {
                SetBodyAlpha(0.85f);

                if (_diveTimer > 0f)
                {
                    _diveTimer -= dt;
                    Pos += _diveDir * DiveSpeed * dt;

                    if (!_hitThisDive && distToPlayer < HitDist)
                    {
                        _hitThisDive = true;
                        Vector2 knock = _diveDir * GameConst.Px(180f);
                        World.Player.ApplyExternalImpulse(new Vector2(knock.x, GameConst.JumpSpeed * 0.4f));
                        World.Player.ApplyStun(GameConst.ScaleStunDuration);
                        World.AddShake(GameConst.Px(60f));
                    }
                }
                else if (_diveCd <= 0f)
                {
                    _diveTimer = DiveDuration;
                    _diveCd = DiveCooldown;
                    _hitThisDive = false;
                    _diveDir = (playerCenter - Pos).normalized;
                }
                else
                {
                    // 蓄势：在出生点附近小幅游弋。
                    Pos += new Vector2(Mathf.Sin(_phase * 4f), Mathf.Cos(_phase * 3f)) * GameConst.Px(20f) * dt;
                }
            }
            else
            {
                _diveTimer = 0f;
                Pos += (Spawn - Pos) * 0.6f * dt
                       + new Vector2(Mathf.Sin(_phase), 0f) * PatrolSpeed * dt;
                SetBodyAlpha(0f);
            }
        }
    }
}
