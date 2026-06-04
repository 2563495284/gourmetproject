using UnityEngine;

namespace GourmetProject.Game.Platformer.Monsters
{
    /// <summary>
    /// 飞蛾（光吸引型，红点）。开灯且近 → 缓慢直线飘向光源，贴附后遮挡玩家视野；
    /// 关灯 → 飘回出生点。设计文档 5.2 / 15.5。
    /// </summary>
    public sealed class Moth : MonsterBase
    {
        private static readonly float Range = GameConst.Px(150f);
        private static readonly float Speed = GameConst.Px(55f);
        private static readonly float AttachDist = GameConst.Px(45f);

        public override bool IsAttract => true;
        protected override Vector2 BodyScale => new Vector2(1.3f, 1.3f);

        protected override void Behave(float dt, Vector2 playerCenter, bool lighterOn, float distToPlayer)
        {
            if (lighterOn && distToPlayer < Range)
            {
                Vector2 toPlayer = (playerCenter - Pos).normalized;
                Pos += toPlayer * Speed * dt;
                SetBodyAlpha(0.85f);

                // 贴附在光源前遮挡视野：越近遮挡越强。
                if (distToPlayer < AttachDist)
                {
                    float strength = Mathf.InverseLerp(AttachDist, GameConst.Px(10f), distToPlayer);
                    World.AddVisionOcclusion(0.5f * strength);
                }
            }
            else
            {
                Pos += (Spawn - Pos) * 0.8f * dt;
                SetBodyAlpha(0f);
            }
        }
    }
}
