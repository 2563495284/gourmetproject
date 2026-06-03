using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.Platformer
{
    /// <summary>
    /// 玩家控制器：自定义分轴 AABB 扫描物理（不使用 Rigidbody），子步进防穿透。
    /// 实现重力、地面/空中加速、跳跃（土狼时间 + 输入缓冲 + 可变高度）、斜坡吸附与下滑、
    /// wall slide / wall jump。参数严格取自设计文档 6.2 / 6.3 / 6.4。
    /// </summary>
    public sealed class PlayerController : MonoBehaviour
    {
        private GameWorld _world;

        // 左下角坐标（世界单位）。
        private Vector2 _pos;
        private float _vx;
        private float _vy;

        private bool _grounded;
        private int _onSlope;     // -1 左下滑 / +1 右下滑 / 0 无
        private int _onWall;      // -1 墙在左 / +1 墙在右 / 0 无
        private float _coyote;
        private float _jumpBuffer;
        private int _facing = 1;

        private const float SubStep = 0.4f;

        public Vector2 Velocity => new Vector2(_vx, _vy);
        public bool Grounded => _grounded;
        public int Facing => _facing;
        public AABB Box => new AABB(_pos.x, _pos.y, GameConst.PlayerWidth, GameConst.PlayerHeight);
        public Vector2 Center => new Vector2(_pos.x + GameConst.PlayerWidth * 0.5f, _pos.y + GameConst.PlayerHeight * 0.5f);

        public void Init(GameWorld world, Vector2 startBottomLeft)
        {
            _world = world;
            SetPosition(startBottomLeft);
        }

        /// <summary>施加外部冲量（怪物位移干扰等）。</summary>
        public void ApplyExternalImpulse(Vector2 v)
        {
            _vx += v.x;
            _vy += v.y;
        }

        public void SetPosition(Vector2 bottomLeft)
        {
            _pos = bottomLeft;
            _vx = 0f;
            _vy = 0f;
            _grounded = false;
            _onSlope = 0;
            _onWall = 0;
            _coyote = 0f;
            _jumpBuffer = 0f;
            SyncTransform();
        }

        /// <summary>由 GameWorld 统一驱动，保证更新顺序确定。</summary>
        public void Tick()
        {
            if (_world == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            bool control = _world.ControlEnabled;
            float moveX = control ? GameInput.MoveX : 0f;

            // —— 跳跃缓冲 / 土狼计时 ——
            if (control && GameInput.JumpPressed) _jumpBuffer = GameConst.JumpBuffer;
            _jumpBuffer -= dt;
            if (_grounded) _coyote = GameConst.CoyoteTime; else _coyote -= dt;

            // —— 水平加速 ——
            float accel = _grounded ? GameConst.GroundAccel : GameConst.AirAccel;
            float target = moveX * GameConst.MaxRunSpeed;
            _vx = Mathf.MoveTowards(_vx, target, accel * dt);

            // —— 斜坡下滑力 ——
            if (_onSlope != 0 && _grounded)
            {
                bool activeMove = Mathf.Abs(moveX) > 0.01f;
                float slide = activeMove ? GameConst.SlopeSlideActive : GameConst.SlopeSlideStanding;
                _vx += _onSlope * slide * dt;
                _vx = Mathf.Clamp(_vx, -GameConst.MaxRunSpeed * 1.6f, GameConst.MaxRunSpeed * 1.6f);
            }

            // —— 跳跃 / Wall Jump ——
            if (control && _jumpBuffer > 0f)
            {
                if (_grounded || _coyote > 0f)
                {
                    _vy = GameConst.JumpSpeed;
                    _grounded = false;
                    _coyote = 0f;
                    _jumpBuffer = 0f;
                }
                else if (_onWall != 0)
                {
                    _vy = GameConst.WallJumpSpeedY;
                    _vx = -_onWall * GameConst.WallJumpSpeedX;
                    _onWall = 0;
                    _jumpBuffer = 0f;
                }
            }

            // 可变跳跃高度：松开跳键且仍上升 → 截断。
            if (control && GameInput.JumpReleased && _vy > 0f)
            {
                _vy *= 0.5f;
            }

            // —— 重力 ——
            _vy -= GameConst.Gravity * dt;

            // Wall slide：贴墙下落限速（按住 F 则吸附不下滑）。
            if (_onWall != 0 && !_grounded && _vy < 0f)
            {
                if (control && GameInput.WallStickHeld)
                {
                    _vy = 0f;
                }
                else if (_vy < -GameConst.WallSlideMaxFall)
                {
                    _vy = -GameConst.WallSlideMaxFall;
                }
            }

            if (_vy < -GameConst.TerminalFall) _vy = -GameConst.TerminalFall;

            // —— 子步进移动 + 分轴扫描 ——
            _onWall = 0;
            bool wasGrounded = _grounded;
            _grounded = false;
            _onSlope = 0;

            float dx = _vx * dt;
            float dy = _vy * dt;
            int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) / SubStep);
            steps = Mathf.Max(1, steps);
            float sx = dx / steps;
            float sy = dy / steps;

            for (int s = 0; s < steps; s++)
            {
                MoveX(sx);
                MoveY(sy);
            }

            SnapToSlope();

            if (Mathf.Abs(moveX) > 0.01f) _facing = moveX > 0f ? 1 : -1;

            SyncTransform();

            // —— 死亡判定：坠落 / 地刺 ——
            if (_pos.y < _world.FallDeathY)
            {
                _world.RequestDie(DeathCause.Fall);
                return;
            }

            if (control && _world.OverlapsSpike(Box))
            {
                _world.RequestDie(DeathCause.Spike);
            }

            if (control && GameInput.SuicidePressed)
            {
                _world.RequestDie(DeathCause.Suicide);
            }

            // 抑制未使用告警（wasGrounded 仅保留语义）。
            _ = wasGrounded;
        }

        private void MoveX(float amount)
        {
            if (amount == 0f) return;
            _pos.x += amount;
            AABB box = Box;
            List<AABB> solids = _world.Solids;
            for (int i = 0; i < solids.Count; i++)
            {
                AABB s = solids[i];
                if (!box.Overlaps(s)) continue;
                if (amount > 0f)
                {
                    _pos.x = s.MinX - GameConst.PlayerWidth;
                    _onWall = 1;
                }
                else
                {
                    _pos.x = s.MaxX;
                    _onWall = -1;
                }
                _vx = 0f;
                box = Box;
            }
        }

        private void MoveY(float amount)
        {
            if (amount == 0f) return;
            _pos.y += amount;
            AABB box = Box;
            List<AABB> solids = _world.Solids;
            for (int i = 0; i < solids.Count; i++)
            {
                AABB s = solids[i];
                if (!box.Overlaps(s)) continue;
                if (amount > 0f)
                {
                    _pos.y = s.MinY - GameConst.PlayerHeight;
                    _vy = 0f;
                }
                else
                {
                    _pos.y = s.MaxY;
                    _vy = 0f;
                    _grounded = true;
                }
                box = Box;
            }
        }

        /// <summary>底部中心采样斜坡顶面，精确吸附并设置下滑方向。</summary>
        private void SnapToSlope()
        {
            List<SlopeData> slopes = _world.Slopes;
            float cx = _pos.x + GameConst.PlayerWidth * 0.5f;
            for (int i = 0; i < slopes.Count; i++)
            {
                SlopeData sl = slopes[i];
                AABB r = sl.Rect;
                if (cx < r.MinX || cx > r.MaxX) continue;

                float t = Mathf.Clamp01((cx - r.MinX) / r.Width);
                float top = sl.Dir == SlopeDir.Right
                    ? r.MinY + r.Height * t
                    : r.MinY + r.Height * (1f - t);

                // 玩家底部接近或穿入斜坡顶面，且不是明显向上运动 → 吸附。
                if (_pos.y <= top + GameConst.Px(6f) && _pos.y >= r.MinY - GameConst.Px(12f) && _vy <= 0.01f)
                {
                    _pos.y = top;
                    _vy = 0f;
                    _grounded = true;
                    _onSlope = sl.Dir == SlopeDir.Right ? -1 : 1;
                }
            }
        }

        private void SyncTransform()
        {
            transform.position = new Vector3(
                _pos.x + GameConst.PlayerWidth * 0.5f,
                _pos.y + GameConst.PlayerHeight * 0.5f,
                0f);

            Vector3 sc = transform.localScale;
            sc.x = Mathf.Abs(sc.x) * _facing;
            transform.localScale = sc;
        }
    }
}
