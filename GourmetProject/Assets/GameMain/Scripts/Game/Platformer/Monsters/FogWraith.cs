using UnityEngine;

namespace GourmetProject.Game.Platformer.Monsters
{
    /// <summary>
    /// 雾灵（光驱赶型，蓝点，大型云雾）。关灯 → 大面积缓慢漂移，接触持续扣减能量（不致死，逼移动）；
    /// 开灯且近 → 消散远离。设计文档 5.3 / 15.5。
    /// </summary>
    public sealed class FogWraith : MonsterBase
    {
        private static readonly float Range = GameConst.Px(200f);
        private static readonly float DriftRadius = GameConst.Px(70f);
        private static readonly float DriftSpeed = GameConst.Px(45f);
        private static readonly float FleeSpeed = GameConst.Px(90f);
        private static readonly float ContactDist = GameConst.Px(48f);
        private static readonly float ShakeCd = 0.4f;

        private float _phase;
        private float _shakeCd;

        public override bool IsAttract => false;
        protected override Vector2 BodyScale => new Vector2(1.2f, 1.2f);

        protected override void Behave(float dt, Vector2 playerCenter, bool lighterOn, float distToPlayer)
        {
            _phase += dt;
            _shakeCd -= dt;

            if (lighterOn && distToPlayer < Range)
            {
                // 光下消散：远离玩家并淡出。
                Vector2 away = (Pos - playerCenter).normalized;
                Pos += away * FleeSpeed * dt;
                SetBodyAlpha(0.05f);
                return;
            }

            // 黑暗中大面积缓慢漂移（围绕出生点的 Lissajous 轨迹）。
            Vector2 target = Spawn + new Vector2(
                Mathf.Sin(_phase * 0.6f) * DriftRadius,
                Mathf.Cos(_phase * 0.4f) * DriftRadius * 0.6f);
            Pos = Vector2.MoveTowards(Pos, target, DriftSpeed * dt);
            SetBodyAlpha(0f);

            if (distToPlayer < ContactDist)
            {
                World.Energy.Drain(GameConst.FogWraithDrainPerSec * dt);
                if (_shakeCd <= 0f)
                {
                    _shakeCd = ShakeCd;
                    World.AddShake(GameConst.Px(24f));
                }
            }
        }
    }
}
