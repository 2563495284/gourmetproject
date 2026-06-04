using UnityEngine;

namespace GourmetProject.Game.Platformer.Monsters
{
    /// <summary>
    /// 怪物基类。通用迷雾预警光点层（设计文档 5.1）：本体默认隐于黑暗，处于玩家视野范围内时
    /// 渲染对应颜色光点（光吸引型=红点，光驱赶型=蓝点）。具体 AI 由子类 Behave 实现。
    /// </summary>
    public abstract class MonsterBase : MonoBehaviour
    {
        protected GameWorld World;
        protected Vector2 Spawn;
        protected SpriteRenderer Body;
        protected SpriteRenderer Dot;
        protected Vector2 Pos;

        /// <summary>true = 光吸引型（红点），false = 光驱赶型（蓝点）。混合型按主要威胁态归类。</summary>
        public abstract bool IsAttract { get; }

        /// <summary>本体精灵缩放（大型/特殊怪物可重写）。</summary>
        protected virtual Vector2 BodyScale => Vector2.one;

        public virtual void Init(GameWorld world, Vector2 spawn)
        {
            World = world;
            Spawn = spawn;
            Pos = spawn;

            Body = WorldRender.Create("Body", BodySprite(), spawn, transform, 5);
            Body.transform.localScale = new Vector3(BodyScale.x, BodyScale.y, 1f);
            SetBodyAlpha(0f);

            Sprite dotSprite = Art.Load(IsAttract ? Art.RedDot : Art.BlueDot);
            Dot = WorldRender.CreateUnlit("Dot", dotSprite, spawn, transform, 50);
            Dot.enabled = false;

            transform.position = new Vector3(spawn.x, spawn.y, 0f);
        }

        protected abstract Sprite BodySprite();

        public void Tick(float dt, Vector2 playerCenter, bool lighterOn, float visionRadius)
        {
            float dist = Vector2.Distance(Pos, playerCenter);
            bool inVision = dist <= visionRadius;

            Behave(dt, playerCenter, lighterOn, dist);

            transform.position = new Vector3(Pos.x, Pos.y, 0f);

            // 迷雾光点：在视野内且本体未被点亮时显示。
            Dot.enabled = inVision && Body.color.a < 0.25f && !World.Lighting.RevealAll;
        }

        protected abstract void Behave(float dt, Vector2 playerCenter, bool lighterOn, float distToPlayer);

        protected void SetBodyAlpha(float a)
        {
            Color c = Body.color;
            c.a = a;
            Body.color = c;
        }
    }
}
