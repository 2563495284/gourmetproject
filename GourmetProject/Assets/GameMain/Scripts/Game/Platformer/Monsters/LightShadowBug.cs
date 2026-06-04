using UnityEngine;

namespace GourmetProject.Game.Platformer.Monsters
{
    /// <summary>
    /// 光影虫（混合型，红点）。开灯且近 → 透明无害但吸光充能；关灯 → 若已充满则发光引爆（范围伤害），
    /// 否则保持充能静置。设计文档 5.4 / 15.5。
    /// </summary>
    public sealed class LightShadowBug : MonsterBase
    {
        private static readonly float Range = GameConst.Px(160f);
        private static readonly float BlastRadius = GameConst.Px(70f);
        private const float ChargeRate = 0.5f;   // 满充约 2s
        private const float FuseTime = 0.8f;

        private float _charge;
        private float _fuse = -1f;   // <0 未进入引爆倒计时

        public override bool IsAttract => true;
        protected override Sprite BodySprite() => Art.Load(Art.LightShadowBug);

        protected override void Behave(float dt, Vector2 playerCenter, bool lighterOn, float distToPlayer)
        {
            if (lighterOn && distToPlayer < Range)
            {
                // 吸光充能：透明无害。
                _charge = Mathf.Min(1f, _charge + ChargeRate * dt);
                _fuse = -1f;
                SetBodyAlpha(0.12f);
                return;
            }

            // 黑暗中：充满则发光引爆。
            if (_charge >= 1f)
            {
                if (_fuse < 0f) _fuse = FuseTime;
                _fuse -= dt;
                // 引爆前发光（闪烁加剧）。
                float glow = Mathf.Lerp(0.4f, 1f, 1f - Mathf.Clamp01(_fuse / FuseTime));
                SetBodyAlpha(glow);

                if (_fuse <= 0f)
                {
                    if (distToPlayer < BlastRadius)
                    {
                        World.RequestDie(DeathCause.Monster);
                    }
                    World.AddShake(GameConst.Px(80f));
                    _charge = 0f;
                    _fuse = -1f;
                    SetBodyAlpha(0f);
                }
            }
            else
            {
                // 未充满：静置（保留已有充能）。
                SetBodyAlpha(0f);
            }
        }
    }
}
