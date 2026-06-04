using UnityEngine;

namespace GourmetProject.Game.Platformer.Monsters
{
    /// <summary>
    /// 藤蔓（光驱赶型，蓝点，静止特殊体）。关灯 → 在平台表面蔓延生长，覆盖区踩上减速 50% 且无法起跳；
    /// 开灯且近 → 萎缩消失。设计文档 5.3 / 15.5。
    /// </summary>
    public sealed class Vine : MonsterBase
    {
        private static readonly float Range = GameConst.Px(150f);
        private static readonly float CoverHalfWidth = GameConst.Px(48f);
        private const float GrowRate = 0.4f;
        private const float ShrinkRate = 1.5f;
        private const float ActiveThreshold = 0.4f;

        private float _growth;

        public override bool IsAttract => false;

        protected override void Behave(float dt, Vector2 playerCenter, bool lighterOn, float distToPlayer)
        {
            // 静止：位置固定在出生点。
            Pos = Spawn;

            if (lighterOn && distToPlayer < Range)
            {
                _growth = Mathf.Max(0f, _growth - ShrinkRate * dt);
            }
            else
            {
                _growth = Mathf.Min(1f, _growth + GrowRate * dt);
            }

            // 生长沿平台横向铺开（缩放）+ 透明度。
            Body.transform.localScale = new Vector3(1f + _growth * 2.5f, 1f, 1f);
            SetBodyAlpha(_growth * 0.6f);

            // 覆盖判定：足够生长 + 玩家站在覆盖区。
            if (_growth >= ActiveThreshold
                && World.Player.Grounded
                && Mathf.Abs(playerCenter.x - Pos.x) < CoverHalfWidth + _growth * GameConst.Px(20f)
                && Mathf.Abs(playerCenter.y - Pos.y) < GameConst.Px(40f))
            {
                World.Player.ApplyGroundModifier(0.5f, true);
            }
        }
    }
}
