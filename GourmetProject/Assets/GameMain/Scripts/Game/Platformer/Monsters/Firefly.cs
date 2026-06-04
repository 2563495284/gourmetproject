using UnityEngine;

namespace GourmetProject.Game.Platformer.Monsters
{
    /// <summary>
    /// 萤火群（光吸引型，红点，微型）。开灯且近 → 聚集到光源周围并自发光，形成光团遮盖玩家自身光圈；
    /// 关灯 → 分散回出生点。设计文档 5.2 / 15.5。
    /// </summary>
    public sealed class Firefly : MonsterBase
    {
        private static readonly float Range = GameConst.Px(150f);
        private static readonly float GatherSpeed = GameConst.Px(120f);
        private static readonly float CoverDist = GameConst.Px(60f);

        private float _phase;

        public override bool IsAttract => true;
        protected override Vector2 BodyScale => new Vector2(0.5f, 0.5f);
        protected override Sprite BodySprite() => Art.Load(Art.Firefly);

        protected override void Behave(float dt, Vector2 playerCenter, bool lighterOn, float distToPlayer)
        {
            _phase += dt * 5f;

            if (lighterOn && distToPlayer < Range)
            {
                // 聚集到玩家略上方并绕动。
                Vector2 target = playerCenter + new Vector2(Mathf.Sin(_phase) * GameConst.Px(14f), GameConst.Px(18f));
                Pos = Vector2.MoveTowards(Pos, target, GatherSpeed * dt);
                SetBodyAlpha(0.95f);

                if (distToPlayer < CoverDist)
                {
                    float strength = Mathf.InverseLerp(CoverDist, GameConst.Px(12f), distToPlayer);
                    World.AddVisionOcclusion(0.35f * strength);
                }
            }
            else
            {
                Pos += (Spawn - Pos) * 0.9f * dt
                       + new Vector2(Mathf.Sin(_phase) * GameConst.Px(5f), Mathf.Cos(_phase * 0.8f) * GameConst.Px(4f)) * dt;
                SetBodyAlpha(0f);
            }
        }
    }
}
