using System.Collections;
using System.Collections.Generic;
using GourmetProject.Game.Platformer.Chunks;
using GourmetProject.Game.Platformer.Monsters;
using GourmetProject.Game.Roguelike;
using UnityEngine;

namespace GourmetProject.Game.Platformer
{
    /// <summary>
    /// 《光影》对局中枢：构建关卡/玩家/光照/怪物/检查点，统一驱动更新顺序，处理死亡重生、
    /// 无敌、胜利与相机跟随。HUD 通过 <see cref="Current"/> 读取状态。
    /// </summary>
    public sealed class GameWorld : MonoBehaviour
    {
        public static GameWorld Current { get; private set; }

        public PlayerController Player { get; private set; }
        public LightingSystem Lighting { get; private set; }
        public CheckpointSystem Checkpoints { get; private set; }
        public EnergySystem Energy { get; private set; } = new EnergySystem();

        public float FallDeathY => _level.FallDeathY;

        public bool ControlEnabled { get; private set; } = true;
        public bool IsDead { get; private set; }
        public bool IsVictory { get; private set; }

        /// <summary>对局是否因技能选择等弹窗而暂停（暂停时跳过玩家/怪物/检查点更新）。</summary>
        public bool IsPaused { get; private set; }

        /// <summary>当前 build 的技能运行时视图（供玩家/光照/怪物按语义查询）。</summary>
        public SkillRuntimeState Skills => _skills?.Runtime;
        public float InvincibleRemaining { get; private set; }
        public bool IsInvincible => InvincibleRemaining > 0f;

        public float EnergyFraction => Energy.Fraction;
        public float Progress { get; private set; }

        /// <summary>本局达到过的最高进度（0..1），用于局外成长结算。</summary>
        public float MaxProgress { get; private set; }

        /// <summary>本帧视野遮挡强度（0..1）：飞蛾贴附 / 萤火群光团遮盖玩家光圈，由怪物累加，光照系统消费。</summary>
        public float VisionOcclusion { get; private set; }
        private float _visionOcclusionAccum;

        /// <summary>怪物施加视野遮挡（取累加，本帧上限钳制）。</summary>
        public void AddVisionOcclusion(float strength) => _visionOcclusionAccum += Mathf.Max(0f, strength);
        public Vector2 DebugPos => Player != null ? Player.Center : Vector2.zero;
        public Vector2 DebugVel => Player != null ? Player.Velocity : Vector2.zero;
        public float DebugLightRadius => Energy.LighterOn ? Energy.LighterRadius : GameConst.DefaultVisionRadius;

        private LevelData _level;
        private MonsterManager _monsters;
        private RunSkillController _skills;
        private ParallaxBackground _background;
        private VisionFogOverlay _fogOverlay;
        private Camera _cam;
        private SpriteRenderer _playerRenderer;
        private SpriteRenderer _invRing;
        private readonly List<Camera> _disabledCameras = new List<Camera>();

        private float _startY;
        private float _endY;
        private float _shake;
        private DeathCause _lastCause;
        private LayerMask _spikeMask;

        private void Awake()
        {
            Current = this;
            BuildWorld();
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            foreach (Camera c in _disabledCameras)
            {
                if (c != null) c.enabled = true;
            }
        }

        private void BuildWorld()
        {
            // —— 局外成长：注入初始能量上限加成（设计文档 13.10）——
            Energy.SetMaxBonus(MetaProfile.Current.EnergyCapBonus);

            // —— 关卡：预制段拼接（地形/碰撞/怪物/检查点直接来自预制段）——
            _spikeMask = LayerMask.GetMask(WorldRender.LayerSpike);
            _level = LevelAssembler.Assemble(transform);
            // 预制段刚实例化，立即同步物理变换，保证玩家首帧 BoxCast 读到地形碰撞体。
            Physics2D.SyncTransforms();
            _startY = _level.StartPos.y;
            _endY = _level.Checkpoints.Count > 0 ? _level.Checkpoints[_level.Checkpoints.Count - 1].Pos.y : _level.WorldHeight;

            // —— 相机 ——
            SetupCamera();

            // —— 远景背景（分层，纵向固定；Unlit 月光雾）——
            // _background = new ParallaxBackground();
            // _background.Build(transform, _cam, _level);

            // —— 光照 ——
            Lighting = gameObject.AddComponent<LightingSystem>();

            // —— 玩家（Player.prefab：SpriteRenderer + Animator + 物理 + 控制器）——
            GameObject playerGo = InstantiatePlayer();
            _playerRenderer = playerGo.GetComponent<SpriteRenderer>();
            Player = playerGo.GetComponent<PlayerController>();
            Player.Init(this, _level.StartPos);
            var playerAnimator = playerGo.GetComponent<PlayerAnimator>();
            if (playerAnimator != null)
            {
                playerAnimator.Init(playerGo.GetComponent<Animator>(), Player, Energy);
            }

            // 无敌环（不受光照，始终可见），默认隐藏。
            _invRing = WorldRender.CreateUnlit("InvRing", Art.Load(Art.InvincibilityRing), Vector2.zero, playerGo.transform, 40);
            _invRing.enabled = false;

            Lighting.Init(playerGo.transform, Energy);

            // —— 检查点 ——
            Checkpoints = gameObject.AddComponent<CheckpointSystem>();
            Checkpoints.Build(this, Lighting, _level);

            // —— 怪物（设计师摆进 chunk，拼接时已收集为实例）——
            _monsters = gameObject.AddComponent<MonsterManager>();
            _monsters.Register(this, _level.Monsters);

            // —— HUD ——
            gameObject.AddComponent<GameplayHud>().Init(this);

            // —— 肉鸽技能（图鉴 + 本局状态）——
            _skills = new RunSkillController();
            if (RunSession.HasPendingLoad)
            {
                _skills.RestoreFrom(RunSession.PendingLoad);
                int activated = _skills.ActivatedCheckpointCount;
                if (activated > 0)
                {
                    Vector2 respawn = Checkpoints.PreActivate(activated);
                    Player.SetPosition(respawn);
                }
                RunSession.Clear();
            }

            SnapCameraToPlayer();
        }

        private const string PlayerPrefabPath = "Prefabs/Player";

        /// <summary>实例化玩家 prefab；缺失时兜底用代码搭建（保证可运行）。返回的 GameObject 已挂好渲染/物理/控制器。</summary>
        private GameObject InstantiatePlayer()
        {
            var prefab = Resources.Load<GameObject>(PlayerPrefabPath);
            if (prefab != null)
            {
                GameObject go = Instantiate(prefab, transform);
                go.name = "Player";
                go.transform.localScale = Vector3.one;
                return go;
            }

            Debug.LogWarning("[GameWorld] 未找到 Player.prefab，使用代码兜底搭建玩家（无 Animator）。请运行 Tools/光影/重建动画资源。");
            var fallback = new GameObject("Player");
            fallback.transform.SetParent(transform, false);
            var sr = fallback.AddComponent<SpriteRenderer>();
            sr.sprite = Art.Load(Art.PlayerIdle);
            sr.sharedMaterial = WorldRender.LitMaterial;
            sr.sortingOrder = 10;
            fallback.transform.localScale = Vector3.one;
            fallback.AddComponent<PlayerController>();
            return fallback;
        }

        private void SetupCamera()
        {
            // 禁用其它相机（Launch 场景的 Main Camera 等），避免叠加加载时多相机冲突。
            foreach (Camera c in Camera.allCameras)
            {
                if (c != null && c.enabled)
                {
                    c.enabled = false;
                    _disabledCameras.Add(c);
                }
            }

            var camGo = new GameObject("GameplayCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = 9f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = GameConst.AtmosphereCameraBgNight;
            _cam.transform.position = new Vector3(_level.StartPos.x, _level.StartPos.y, -10f);
            _cam.tag = "MainCamera";

            _fogOverlay = camGo.AddComponent<VisionFogOverlay>();
            _fogOverlay.Build(_cam);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // 技能选择等弹窗期间冻结对局（UI 仍可交互）。
            if (IsPaused) return;

            if (GameInput.RevealTogglePressed)
            {
                Lighting.RevealAll = !Lighting.RevealAll;
                ApplyAtmosphereReveal(Lighting.RevealAll);
            }

            // 能量（打火机）。
            bool wantLighter = ControlEnabled && !IsDead && GameInput.LighterHeld;
            Energy.Tick(dt, wantLighter);

            // 玩家物理。
            if (Player != null) Player.Tick();

            // 怪物（先清空本帧视野遮挡累加，怪物 Behave 内按需累加）。
            _visionOcclusionAccum = 0f;
            float visionRadius = Energy.LighterOn ? Energy.LighterRadius : GameConst.DefaultVisionRadius;
            _monsters.Tick(dt, Player.Center, Energy.LighterOn, visionRadius);
            VisionOcclusion = Mathf.Clamp01(_visionOcclusionAccum);

            // 检查点 / 终点。
            if (ControlEnabled) Checkpoints.Tick(Player.Center);

            // 进度。
            if (_endY > _startY)
            {
                Progress = Mathf.Clamp01((Player.Center.y - _startY) / (_endY - _startY));
                if (Progress > MaxProgress) MaxProgress = Progress;
            }

            // 无敌计时 + 闪烁。
            if (InvincibleRemaining > 0f)
            {
                InvincibleRemaining -= dt;
                _invRing.enabled = true;
                float freq = InvincibleRemaining < 0.5f ? 14f : 5f;
                float a = (Mathf.Sin(Time.time * freq * Mathf.PI * 2f) > 0f) ? 1f : 0.35f;
                SetPlayerAlpha(a);
                _invRing.transform.rotation = Quaternion.Euler(0f, 0f, Time.time * 180f);
                if (InvincibleRemaining <= 0f)
                {
                    _invRing.enabled = false;
                    SetPlayerAlpha(1f);
                }
            }
        }

        private void LateUpdate()
        {
            if (_cam == null || Player == null) return;

            _shake = Mathf.MoveTowards(_shake, 0f, Time.deltaTime * GameConst.Px(120f));

            float aspect = _cam.aspect;
            float halfW = _cam.orthographicSize * aspect;
            float minX = _level.WorldMinX;
            float maxX = _level.WorldMaxX;
            float targetX = Mathf.Clamp(Player.Center.x, minX + halfW, maxX - halfW);
            if (maxX - minX < halfW * 2f) targetX = (minX + maxX) * 0.5f;
            float targetY = Player.Center.y + GameConst.Px(40f);

            Vector3 cur = _cam.transform.position;
            Vector3 desired = new Vector3(targetX, targetY, -10f);
            Vector3 follow = Vector3.Lerp(cur, desired, 1f - Mathf.Exp(-12f * Time.deltaTime));

            Vector2 shakeOff = _shake > 0f
                ? new Vector2(Random.Range(-_shake, _shake), Random.Range(-_shake, _shake))
                : Vector2.zero;

            _cam.transform.position = new Vector3(follow.x + shakeOff.x, follow.y + shakeOff.y, -10f);

            _background?.Tick(_cam.transform.position);

            if (_fogOverlay != null && Lighting != null)
            {
                _fogOverlay.Sync(Player.Center, Lighting.RevealAll, VisionOcclusion, Energy);
            }
        }

        private void ApplyAtmosphereReveal(bool reveal)
        {
            if (_cam != null)
            {
                _cam.backgroundColor = reveal
                    ? GameConst.AtmosphereCameraBgReveal
                    : GameConst.AtmosphereCameraBgNight;
            }

            _background?.SetRevealAll(reveal);
        }

        private void SnapCameraToPlayer()
        {
            if (_cam == null || Player == null) return;
            float aspect = _cam.aspect;
            float halfW = _cam.orthographicSize * aspect;
            float minX = _level.WorldMinX;
            float maxX = _level.WorldMaxX;
            float targetX = Mathf.Clamp(Player.Center.x, minX + halfW, maxX - halfW);
            if (maxX - minX < halfW * 2f) targetX = (minX + maxX) * 0.5f;
            _cam.transform.position = new Vector3(targetX, Player.Center.y + GameConst.Px(40f), -10f);
        }

        // —— 对外回调 ——

        public bool OverlapsSpike(Vector2 center, Vector2 size)
        {
            // 地刺为 Spike 层触发体，用物理重叠查询（略收缩避免贴边误判）。
            var querySize = new Vector2(Mathf.Max(0.01f, size.x - 0.1f), Mathf.Max(0.01f, size.y - 0.1f));
            return Physics2D.OverlapBox(center, querySize, 0f, _spikeMask) != null;
        }

        public void AddShake(float amount)
        {
            _shake = Mathf.Max(_shake, amount);
        }

        /// <summary>暂停/恢复对局（技能选择弹窗期间使用）。</summary>
        public void SetPaused(bool paused)
        {
            IsPaused = paused;
            ControlEnabled = !paused && !IsDead && !IsVictory;
        }

        /// <summary>由检查点系统在普通检查点首次激活时调用：暂停对局并弹出 3 选 1 技能界面。</summary>
        public void OnCheckpointActivated()
        {
            if (_skills == null) return;
            SetPaused(true);
            _skills.BeginCheckpointDraft(() => SetPaused(false));
        }

        public void RequestDie(DeathCause cause)
        {
            if (IsDead || IsVictory) return;
            if (IsInvincible && cause != DeathCause.Suicide) return;

            _lastCause = cause;
            IsDead = true;
            ControlEnabled = false;
            Energy.Tick(0f, false);
            AddShake(GameConst.Px(120f));
            StartCoroutine(RespawnRoutine());
        }

        public void OnVictory()
        {
            if (IsVictory) return;
            IsVictory = true;
            ControlEnabled = false;
        }

        private IEnumerator RespawnRoutine()
        {
            yield return new WaitForSeconds(GameConst.RespawnDelay);

            Player.SetPosition(Checkpoints.RespawnPoint);
            Energy.Refill();
            IsDead = false;
            ControlEnabled = true;
            InvincibleRemaining = GameConst.InvincibleDuration;
            SnapCameraToPlayer();
            _ = _lastCause;
        }

        private void SetPlayerAlpha(float a)
        {
            if (_playerRenderer == null) return;
            Color c = _playerRenderer.color;
            c.a = a;
            _playerRenderer.color = c;
        }
    }
}
