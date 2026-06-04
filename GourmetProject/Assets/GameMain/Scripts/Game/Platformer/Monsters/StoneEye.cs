using UnityEngine;

namespace GourmetProject.Game.Platformer.Monsters
{
    /// <summary>
    /// 石瞳（混合型，蓝点）。开灯 → 石化静止可见（无害）；关灯 → 苏醒追击，黑暗中接触即死。
    /// 石化/苏醒两种外观由 Animator 状态（Stone/Awake）切换。设计文档 5.4 / 15.5。
    /// </summary>
    public sealed class StoneEye : MonsterBase
    {
        public const string StateStone = "Stone";
        public const string StateAwake = "Awake";

        private static readonly float ChaseSpeed = GameConst.Px(90f);
        private static readonly float KillDist = GameConst.Px(20f);

        public override bool IsAttract => false;

        protected override void Behave(float dt, Vector2 playerCenter, bool lighterOn, float distToPlayer)
        {
            if (lighterOn)
            {
                // 石化：静止、作为石像可见。
                PlayAnim(StateStone);
                SetBodyAlpha(1f);
                return;
            }

            // 苏醒追击：黑暗中本体不可见（蓝点预警），向玩家移动，接触即死。
            PlayAnim(StateAwake);
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
