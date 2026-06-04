using UnityEngine;

namespace GourmetProject.Game.Platformer.Monsters
{
    /// <summary>
    /// 光食虫（光吸引型，红点）。开灯且近 → 沿平台表面爬向光源，附着后每秒吸取能量；
    /// 关灯 → 静止。设计文档 5.2 / 15.5。
    /// </summary>
    public sealed class LightEater : MonsterBase
    {
        private static readonly float Range = GameConst.Px(160f);
        private static readonly float CrawlSpeed = GameConst.Px(60f);
        private static readonly float AttachDist = GameConst.Px(22f);

        public override bool IsAttract => true;
        protected override Sprite BodySprite() => Art.Load(Art.LightEater);

        protected override void Behave(float dt, Vector2 playerCenter, bool lighterOn, float distToPlayer)
        {
            if (lighterOn && distToPlayer < Range)
            {
                // 贴地爬行：只在水平方向逼近，y 锁定出生表面。
                float dir = Mathf.Sign(playerCenter.x - Pos.x);
                Pos.x += dir * CrawlSpeed * dt;
                Pos.y = Spawn.y;
                SetBodyAlpha(0.9f);

                if (distToPlayer < AttachDist)
                {
                    World.Energy.Drain(GameConst.LightEaterDrainPerSec * dt);
                }
            }
            else
            {
                Pos.x = Mathf.MoveTowards(Pos.x, Spawn.x, CrawlSpeed * 0.5f * dt);
                Pos.y = Spawn.y;
                SetBodyAlpha(0f);
            }
        }
    }
}
