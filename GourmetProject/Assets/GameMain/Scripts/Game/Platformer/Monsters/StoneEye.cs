using UnityEngine;

namespace GourmetProject.Game.Platformer.Monsters
{
    /// <summary>
    /// 石瞳（混合型，蓝点）。开灯 → 石化静止可见（无害）；关灯 → 苏醒追击，黑暗中接触即死。
    /// 设计文档 5.4 / 15.5。
    /// </summary>
    public sealed class StoneEye : MonsterBase
    {
        private static readonly float ChaseSpeed = GameConst.Px(90f);
        private static readonly float KillDist = GameConst.Px(20f);

        private Sprite _closed;
        private Sprite _open;

        public override bool IsAttract => false;
        protected override Vector2 BodyScale => new Vector2(1f, 2f); // 中型 16×32
        protected override Sprite BodySprite() => Art.Load(Art.StoneEyeClosed);

        public override void Init(GameWorld world, Vector2 spawn)
        {
            base.Init(world, spawn);
            _closed = Art.Load(Art.StoneEyeClosed);
            _open = Art.Load(Art.StoneEyeOpen);
        }

        protected override void Behave(float dt, Vector2 playerCenter, bool lighterOn, float distToPlayer)
        {
            if (lighterOn)
            {
                // 石化：静止、作为石像可见。
                if (Body.sprite != _closed) Body.sprite = _closed;
                SetBodyAlpha(1f);
                return;
            }

            // 苏醒追击：黑暗中本体不可见（蓝点预警），向玩家移动，接触即死。
            if (Body.sprite != _open) Body.sprite = _open;
            SetBodyAlpha(0f);

            Vector2 toPlayer = (playerCenter - Pos).normalized;
            Pos += toPlayer * ChaseSpeed * dt;

            if (distToPlayer < KillDist)
            {
                World.RequestDie(DeathCause.Monster);
            }
        }
    }
}
