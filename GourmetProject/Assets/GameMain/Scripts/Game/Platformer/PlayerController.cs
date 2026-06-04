using UnityEngine;

namespace GourmetProject.Game.Platformer
{
    /// <summary>
    /// 玩家控制器：Kinematic Rigidbody2D + BoxCollider2D，手动积分 + 分轴 BoxCast 扫描（先 X 后 Y）+
    /// 子步进防穿透。碰撞对象是 Terrain 层的 Unity 碰撞体（预制段内 TilemapCollider2D / PolygonCollider2D 坡）。
    /// 实现重力、地面/空中加速、跳跃（土狼时间 + 输入缓冲 + 可变高度）、斜坡吸附与下滑、wall slide / wall jump。
    /// 参数严格取自设计文档 6.2 / 6.3 / 6.4。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class PlayerController : MonoBehaviour
    {
        private GameWorld _world;
        private Rigidbody2D _rb;
        private BoxCollider2D _col;
        private LayerMask _terrainMask;

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

        // 空中剩余可用跳跃次数（由二段跳技能赋予；落地刷新）。
        private int _airJumps;

        // 怪物威胁状态。
        private float _stun;                  // 硬直剩余时间（趋光飞鳞撞击 / 回声蝠眩晕）
        private float _groundSpeedMul = 1f;   // 本帧地面移速倍率（藤蔓减速），每帧消费后复位 1
        private bool _blockJump;        // 本帧禁止起跳（藤蔓覆盖），每帧消费后复位

        private const float SubStep = 0.4f;
        private const float Skin = 0.02f;            // 扫描收缩 + 贴边留白，防穿透/卡死
        private const float GroundNormalMinY = 0.5f; // 法线 Y ≥ 此值视为可站立地面/坡，否则视为墙
        private const float SlopeMaxStep = 1.0f;     // 沿坡上/下吸附每帧最大垂直步进（世界单位）

        /// <summary>是否处于硬直/眩晕（HUD、动画可查询）。</summary>
        public bool Stunned => _stun > 0f;

        public Vector2 Velocity => new Vector2(_vx, _vy);
        public bool Grounded => _grounded;
        public int Facing => _facing;

        /// <summary>是否正贴墙（供动画 wallslide 状态查询）。</summary>
        public bool OnWall => _onWall != 0;
        public Vector2 Center => new Vector2(_pos.x + GameConst.PlayerWidth * 0.5f, _pos.y + GameConst.PlayerHeight * 0.5f);

        public void Init(GameWorld world, Vector2 startBottomLeft)
        {
            _world = world;
            _terrainMask = LayerMask.GetMask(WorldRender.LayerTerrain);

            EnsurePhysics();
            SetPosition(startBottomLeft);
        }

        private void EnsurePhysics()
        {
            int playerLayer = LayerMask.NameToLayer(WorldRender.LayerPlayer);
            if (playerLayer >= 0) gameObject.layer = playerLayer;

            _rb = GetComponent<Rigidbody2D>();
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.simulated = true;
            _rb.useFullKinematicContacts = true;
            _rb.gravityScale = 0f;

            _col = GetComponent<BoxCollider2D>();
            _col.size = new Vector2(GameConst.PlayerWidth, GameConst.PlayerHeight);
            _col.offset = Vector2.zero; // 以角色中心对齐（transform 设为中心，见 SyncTransform）
        }

        /// <summary>施加外部冲量（怪物位移干扰等）。</summary>
        public void ApplyExternalImpulse(Vector2 v)
        {
            _vx += v.x;
            _vy += v.y;
        }

        /// <summary>施加硬直/眩晕（趋光飞鳞撞击 0.3s、回声蝠声波 0.5s）。取最大值，不缩短已有硬直。</summary>
        public void ApplyStun(float duration)
        {
            if (duration > _stun) _stun = duration;
        }

        /// <summary>本帧地面行为修正（藤蔓覆盖：减速 + 禁跳）。需每帧调用，未调用则恢复默认。</summary>
        public void ApplyGroundModifier(float speedMul, bool blockJump)
        {
            _groundSpeedMul = Mathf.Min(_groundSpeedMul, speedMul);
            _blockJump |= blockJump;
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

            // —— 硬直 / 眩晕：清空操作意图 ——
            if (_stun > 0f)
            {
                _stun -= dt;
                control = false;
            }

            float moveX = control ? GameInput.MoveX : 0f;

            // —— 跳跃缓冲 / 土狼计时 ——
            if (control && GameInput.JumpPressed) _jumpBuffer = GameConst.JumpBuffer;
            _jumpBuffer -= dt;
            if (_grounded) _coyote = GameConst.CoyoteTime; else _coyote -= dt;

            // —— 水平加速（藤蔓覆盖时地面移速打折）——
            float accel = _grounded ? GameConst.GroundAccel : GameConst.AirAccel;
            float speedMul = _grounded ? _groundSpeedMul : 1f;
            float target = moveX * GameConst.MaxRunSpeed * speedMul;
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
                if ((_grounded || _coyote > 0f) && !_blockJump)
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
                else if (_airJumps > 0)
                {
                    // 二段跳（技能：ms_double_jump）。
                    _vy = GameConst.JumpSpeed;
                    _airJumps--;
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

            // —— 斜坡吸附：grounded 且非上升时，沿坡上/下贴合 ——
            if ((wasGrounded || _grounded) && _vy <= 0.01f)
            {
                SnapToGround();
            }

            // 落地刷新空中跳跃次数。
            if (_grounded) _airJumps = AirJumpCapacity();

            if (Mathf.Abs(moveX) > 0.01f) _facing = moveX > 0f ? 1 : -1;

            SyncTransform();

            // —— 死亡判定：坠落 / 地刺 ——
            if (_pos.y < _world.FallDeathY)
            {
                _world.RequestDie(DeathCause.Fall);
                return;
            }

            if (control && _world.OverlapsSpike(BoxCenter, CastSize))
            {
                _world.RequestDie(DeathCause.Spike);
            }

            if (control && GameInput.SuicidePressed)
            {
                _world.RequestDie(DeathCause.Suicide);
            }

            // 复位本帧地面修正（怪物每帧重新施加，未施加则恢复默认）。
            _groundSpeedMul = 1f;
            _blockJump = false;
        }

        // —— 扫描原语 ——

        private Vector2 BoxCenter => new Vector2(_pos.x + GameConst.PlayerWidth * 0.5f, _pos.y + GameConst.PlayerHeight * 0.5f);
        private Vector2 CastSize => new Vector2(
            Mathf.Max(0.01f, GameConst.PlayerWidth - Skin * 2f),
            Mathf.Max(0.01f, GameConst.PlayerHeight - Skin * 2f));

        private RaycastHit2D Cast(Vector2 dir, float distance)
        {
            return Physics2D.BoxCast(BoxCenter, CastSize, 0f, dir, distance + Skin, _terrainMask);
        }

        private void MoveX(float amount)
        {
            if (amount == 0f) return;
            Vector2 dir = amount > 0f ? Vector2.right : Vector2.left;
            float dist = Mathf.Abs(amount);
            RaycastHit2D hit = Cast(dir, dist);

            if (hit.collider != null)
            {
                // 陡壁阻挡；可站立坡放行（由 SnapToGround 处理垂直贴合）。
                if (hit.normal.y < GroundNormalMinY)
                {
                    float move = Mathf.Max(0f, hit.distance - Skin);
                    _pos.x += dir.x * move;
                    _vx = 0f;
                    _onWall = amount > 0f ? 1 : -1;
                    return;
                }
            }
            _pos.x += amount;
        }

        private void MoveY(float amount)
        {
            if (amount == 0f) return;
            Vector2 dir = amount > 0f ? Vector2.up : Vector2.down;
            float dist = Mathf.Abs(amount);
            RaycastHit2D hit = Cast(dir, dist);

            if (hit.collider != null)
            {
                float move = Mathf.Max(0f, hit.distance - Skin);
                _pos.y += dir.y * move;
                if (amount > 0f)
                {
                    _vy = 0f; // 撞顶
                }
                else
                {
                    _vy = 0f;
                    _grounded = true;
                    SetSlopeFromNormal(hit.normal);
                }
                return;
            }
            _pos.y += amount;
        }

        /// <summary>沿地面/坡贴合：从抬高处向下扫描，吸附到坡面（上坡上移、下坡下移），步进上限 SlopeMaxStep。</summary>
        private void SnapToGround()
        {
            Vector2 start = BoxCenter + Vector2.up * SlopeMaxStep;
            float dist = SlopeMaxStep * 2f;
            RaycastHit2D hit = Physics2D.BoxCast(start, CastSize, 0f, Vector2.down, dist + Skin, _terrainMask);
            if (hit.collider == null) return;
            if (hit.normal.y < GroundNormalMinY) return; // 陡壁不吸附

            // 用接触点高度作为坡面顶；把角色底边对齐到该点（上/下坡都正确，无累积漂移）。
            float surfaceY = hit.point.y;
            float diff = surfaceY - _pos.y;
            if (diff > SlopeMaxStep || diff < -SlopeMaxStep) return;

            if (Mathf.Abs(diff) > 0.0001f) _pos.y = surfaceY;
            _grounded = true;
            SetSlopeFromNormal(hit.normal);
            if (_vy < 0f) _vy = 0f;
        }

        private void SetSlopeFromNormal(Vector2 normal)
        {
            if (normal.x > 0.05f) _onSlope = 1;
            else if (normal.x < -0.05f) _onSlope = -1;
            else _onSlope = 0;
        }

        // 当前 build 赋予的空中额外跳跃次数（二段跳=1，未持有=0）。
        private int AirJumpCapacity()
        {
            var skills = _world != null ? _world.Skills : null;
            return skills != null && skills.HasDoubleJump ? 1 : 0;
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
