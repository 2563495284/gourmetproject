using UnityEngine;

namespace GourmetProject.Game.Platformer.Monsters
{
    /// <summary>
    /// 伏击蛛（光驱赶型，蓝点，中型）。开灯 → 静止悬挂可见；关灯且玩家经过下方 → 垂直下落，
    /// 黑暗中接触即死；落到底部或开灯后缩回。设计文档 5.3 / 15.5。
    /// </summary>
    public sealed class AmbushSpider : MonsterBase
    {
        private static readonly float Range = GameConst.Px(220f);
        private static readonly float TriggerHalfWidth = GameConst.Px(20f);
        private static readonly float DropSpeed = GameConst.Px(520f);
        private static readonly float RetractSpeed = GameConst.Px(120f);
        private static readonly float MaxDrop = GameConst.Px(160f);
        private static readonly float KillDist = GameConst.Px(20f);

        private enum State { Hanging, Dropping, Retracting }
        private State _state = State.Hanging;

        public override bool IsAttract => false;

        protected override void Behave(float dt, Vector2 playerCenter, bool lighterOn, float distToPlayer)
        {
            bool lit = lighterOn && distToPlayer < Range;

            if (lit)
            {
                // 光下：静止可见，缩回悬挂点。
                SetBodyAlpha(1f);
                Pos.y = Mathf.MoveTowards(Pos.y, Spawn.y, RetractSpeed * dt);
                Pos.x = Spawn.x;
                _state = State.Hanging;
                return;
            }

            SetBodyAlpha(0f); // 黑暗中本体不可见（蓝点预警）。

            switch (_state)
            {
                case State.Hanging:
                    Pos = Spawn;
                    bool playerBelow = Mathf.Abs(playerCenter.x - Pos.x) < TriggerHalfWidth && playerCenter.y < Pos.y;
                    if (playerBelow) _state = State.Dropping;
                    break;

                case State.Dropping:
                    Pos.y -= DropSpeed * dt;
                    if (distToPlayer < KillDist)
                    {
                        World.RequestDie(DeathCause.Monster);
                    }
                    if (Pos.y <= Spawn.y - MaxDrop) _state = State.Retracting;
                    break;

                case State.Retracting:
                    Pos.y = Mathf.MoveTowards(Pos.y, Spawn.y, RetractSpeed * dt);
                    if (Pos.y >= Spawn.y - 0.001f) _state = State.Hanging;
                    break;
            }
        }
    }
}
