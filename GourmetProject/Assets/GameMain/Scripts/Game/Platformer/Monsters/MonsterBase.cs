using UnityEngine;

namespace GourmetProject.Game.Platformer.Monsters
{
    /// <summary>
    /// 怪物基类。本体（Body）/ 迷雾预警光点（Dot）/ 动画器（BodyAnimator）改为预制段内序列化引用，
    /// 由设计师把 Monster_*.prefab 直接摆进 chunk。本体帧动画走 Animator+AnimationClip；
    /// 迷雾隐显仍由 alpha 控制（与 Animator 帧驱动互不冲突）。具体 AI 由子类 <see cref="Behave"/> 实现。
    /// </summary>
    public abstract class MonsterBase : MonoBehaviour
    {
        [SerializeField] protected SpriteRenderer Body;
        [SerializeField] protected SpriteRenderer Dot;
        [SerializeField] protected Animator BodyAnimator;

        protected GameWorld World;
        protected Vector2 Spawn;
        protected Vector2 Pos;

        private int _animHash;

        /// <summary>true = 光吸引型（红点），false = 光驱赶型（蓝点）。混合型按主要威胁态归类。</summary>
        public abstract bool IsAttract { get; }

        /// <summary>本体精灵缩放（大型/特殊怪物可重写）。</summary>
        protected virtual Vector2 BodyScale => Vector2.one;

        /// <summary>由 MonsterManager 注册时调用。出生点取自预制段内的世界坐标（设计师摆放）。</summary>
        public virtual void Init(GameWorld world)
        {
            World = world;
            Spawn = transform.position;
            Pos = Spawn;

            // 兜底：预制段未挂引用时退化为代码创建（保持可运行）。
            if (Body == null)
            {
                Body = WorldRender.Create("Body", null, Spawn, transform, 5);
            }
            if (BodyAnimator == null)
            {
                BodyAnimator = Body.GetComponent<Animator>();
            }
            Body.transform.localScale = new Vector3(BodyScale.x, BodyScale.y, 1f);
            SetBodyAlpha(0f);

            if (Dot == null)
            {
                Sprite dotSprite = Art.Load(IsAttract ? Art.RedDot : Art.BlueDot);
                Dot = WorldRender.CreateUnlit("Dot", dotSprite, Spawn, transform, 50);
            }
            Dot.enabled = false;

            transform.position = new Vector3(Spawn.x, Spawn.y, 0f);
        }

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

        /// <summary>切换本体动画状态（仅在状态变化时触发，避免每帧重置循环）。</summary>
        protected void PlayAnim(string state)
        {
            if (BodyAnimator == null) return;
            int hash = Animator.StringToHash(state);
            if (hash == _animHash) return;
            _animHash = hash;
            BodyAnimator.Play(hash, 0, 0f);
        }
    }
}
