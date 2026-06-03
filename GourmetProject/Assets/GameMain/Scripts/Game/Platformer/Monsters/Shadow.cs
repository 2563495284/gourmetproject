using UnityEngine;

namespace GourmetProject.Game.Platformer.Monsters
{
    /// <summary>
    /// 暗影（光驱赶型，蓝点）。关灯且近 → 悄然靠近，黑暗中接触即秒杀；
    /// 开灯且近 → 逃逸并透明化（alpha 0.18）；其余 → 在出生点附近游荡。设计文档 5.3 / 15.5。
    /// </summary>
    public sealed class Shadow : MonsterBase
    {
        private static readonly float SenseRange = GameConst.Px(200f);
        private static readonly float ApproachSpeed = GameConst.Px(70f);
        private static readonly float FleeSpeed = GameConst.Px(120f);
        private static readonly float KillDist = GameConst.Px(18f);
        private float _phase;

        public override bool IsAttract => false;

        protected override Sprite BodySprite() => Art.Load(Art.Shadow);

        protected override void Behave(float dt, Vector2 playerCenter, bool lighterOn, float distToPlayer)
        {
            _phase += dt;

            if (lighterOn && distToPlayer < SenseRange)
            {
                // 光下逃逸 + 透明化。
                Vector2 away = (Pos - playerCenter).normalized;
                Pos += away * FleeSpeed * dt;
                SetBodyAlpha(0.18f);
                return;
            }

            if (!lighterOn && distToPlayer < SenseRange)
            {
                // 黑暗中靠近。
                Vector2 toPlayer = (playerCenter - Pos).normalized;
                Pos += toPlayer * ApproachSpeed * dt;
                SetBodyAlpha(0f);

                if (distToPlayer < KillDist)
                {
                    World.RequestDie(DeathCause.Shadow);
                }
                return;
            }

            // 游荡。
            Vector2 toSpawn = Spawn - Pos;
            Pos += toSpawn * 0.5f * dt + new Vector2(Mathf.Sin(_phase) * GameConst.Px(6f), 0f) * dt;
            SetBodyAlpha(0f);
        }
    }
}
