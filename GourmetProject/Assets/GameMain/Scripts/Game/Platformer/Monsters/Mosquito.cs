using UnityEngine;

namespace GourmetProject.Game.Platformer.Monsters
{
    /// <summary>
    /// 蚊群（光吸引型，红点）。开灯且近 → 扑光源绕圈，接触造成位移干扰 + 屏幕震动（不致死）；
    /// 关灯 → 正弦漂移回出生点。设计文档 5.2 / 15.5。
    /// </summary>
    public sealed class Mosquito : MonsterBase
    {
        private static readonly float AttractRange = GameConst.Px(170f);
        private static readonly float Speed = GameConst.Px(150f);
        private float _phase;
        private float _disturbCd;

        public override bool IsAttract => true;

        protected override Sprite BodySprite() => Art.Load(Art.Mosquito);

        protected override void Behave(float dt, Vector2 playerCenter, bool lighterOn, float distToPlayer)
        {
            _phase += dt * 6f;
            _disturbCd -= dt;

            if (lighterOn && distToPlayer < AttractRange)
            {
                // 扑向光源并绕圈。
                Vector2 toPlayer = (playerCenter - Pos).normalized;
                Vector2 orbit = new Vector2(-toPlayer.y, toPlayer.x) * Mathf.Sin(_phase) * 0.6f;
                Pos += (toPlayer + orbit) * Speed * dt;
                SetBodyAlpha(0.9f);

                if (distToPlayer < GameConst.Px(20f) && _disturbCd <= 0f)
                {
                    _disturbCd = 0.4f;
                    Vector2 push = (playerCenter - Pos).normalized * GameConst.Px(60f);
                    World.Player.ApplyExternalImpulse(new Vector2(push.x, Mathf.Abs(push.y) * 0.3f));
                    World.AddShake(GameConst.Px(40f));
                }
            }
            else
            {
                // 正弦漂移回出生点。
                Vector2 toSpawn = Spawn - Pos;
                Vector2 drift = new Vector2(Mathf.Sin(_phase) * GameConst.Px(8f), Mathf.Cos(_phase * 0.7f) * GameConst.Px(6f));
                Pos += (toSpawn * 0.9f * dt) + drift * dt;
                SetBodyAlpha(0f);
            }
        }
    }
}
