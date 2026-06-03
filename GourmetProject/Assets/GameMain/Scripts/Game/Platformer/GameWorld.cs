using System.Collections;
using System.Collections.Generic;
using GourmetProject.Game.Platformer.Monsters;
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

        public List<AABB> Solids => _level.Solids;
        public List<SlopeData> Slopes => _level.Slopes;
        public float FallDeathY => _level.FallDeathY;

        public bool ControlEnabled { get; private set; } = true;
        public bool IsDead { get; private set; }
        public bool IsVictory { get; private set; }
        public float InvincibleRemaining { get; private set; }
        public bool IsInvincible => InvincibleRemaining > 0f;

        public float EnergyFraction => Energy.Fraction;
        public float Progress { get; private set; }
        public Vector2 DebugPos => Player != null ? Player.Center : Vector2.zero;
        public Vector2 DebugVel => Player != null ? Player.Velocity : Vector2.zero;
        public float DebugLightRadius => Energy.LighterOn ? Energy.LighterRadius : GameConst.DefaultVisionRadius;

        private LevelData _level;
        private MonsterManager _monsters;
        private Camera _cam;
        private SpriteRenderer _playerRenderer;
        private SpriteRenderer _invRing;
        private readonly List<Camera> _disabledCameras = new List<Camera>();

        private float _startY;
        private float _endY;
        private float _shake;
        private DeathCause _lastCause;

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
            // —— 关卡数据 + 可见体 ——
            _level = LevelGenerator.Generate();
            LevelBuilder.Build(_level, transform);
            _startY = _level.StartPos.y;
            _endY = _level.Checkpoints.Count > 0 ? _level.Checkpoints[_level.Checkpoints.Count - 1].Pos.y : _level.WorldHeight;

            // —— 相机 ——
            SetupCamera();

            // —— 光照 ——
            Lighting = gameObject.AddComponent<LightingSystem>();

            // —— 玩家 ——
            var playerGo = new GameObject("Player");
            playerGo.transform.SetParent(transform, false);
            _playerRenderer = playerGo.AddComponent<SpriteRenderer>();
            _playerRenderer.sprite = Art.Load(Art.PlayerIdle);
            _playerRenderer.sharedMaterial = WorldRender.LitMaterial;
            _playerRenderer.sortingOrder = 10;
            playerGo.transform.localScale = Vector3.one;
            Player = playerGo.AddComponent<PlayerController>();
            Player.Init(this, _level.StartPos);
            playerGo.AddComponent<PlayerAnimator>().Init(_playerRenderer, Player, Energy);

            // 无敌环（不受光照，始终可见），默认隐藏。
            _invRing = WorldRender.CreateUnlit("InvRing", Art.Load(Art.InvincibilityRing), Vector2.zero, playerGo.transform, 40);
            _invRing.enabled = false;

            Lighting.Init(playerGo.transform, Energy);

            // —— 检查点 ——
            Checkpoints = gameObject.AddComponent<CheckpointSystem>();
            Checkpoints.Build(this, Lighting, _level);

            // —— 怪物 ——
            _monsters = gameObject.AddComponent<MonsterManager>();
            _monsters.Build(this, _level);

            // —— HUD ——
            gameObject.AddComponent<GameplayHud>().Init(this);

            SnapCameraToPlayer();
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
            _cam.backgroundColor = new Color(0.015f, 0.02f, 0.04f);
            _cam.transform.position = new Vector3(_level.StartPos.x, _level.StartPos.y, -10f);
            _cam.tag = "MainCamera";
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (GameInput.RevealTogglePressed)
            {
                Lighting.RevealAll = !Lighting.RevealAll;
            }

            // 能量（打火机）。
            bool wantLighter = ControlEnabled && !IsDead && GameInput.LighterHeld;
            Energy.Tick(dt, wantLighter);

            // 玩家物理。
            if (Player != null) Player.Tick();

            // 怪物。
            float visionRadius = Energy.LighterOn ? Energy.LighterRadius : GameConst.DefaultVisionRadius;
            _monsters.Tick(dt, Player.Center, Energy.LighterOn, visionRadius);

            // 检查点 / 终点。
            if (ControlEnabled) Checkpoints.Tick(Player.Center);

            // 进度。
            if (_endY > _startY)
            {
                Progress = Mathf.Clamp01((Player.Center.y - _startY) / (_endY - _startY));
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
            float targetX = Mathf.Clamp(Player.Center.x, halfW, GameConst.WorldWidth - halfW);
            if (GameConst.WorldWidth < halfW * 2f) targetX = GameConst.WorldWidth * 0.5f;
            float targetY = Player.Center.y + GameConst.Px(40f);

            Vector3 cur = _cam.transform.position;
            Vector3 desired = new Vector3(targetX, targetY, -10f);
            Vector3 follow = Vector3.Lerp(cur, desired, 1f - Mathf.Exp(-12f * Time.deltaTime));

            Vector2 shakeOff = _shake > 0f
                ? new Vector2(Random.Range(-_shake, _shake), Random.Range(-_shake, _shake))
                : Vector2.zero;

            _cam.transform.position = new Vector3(follow.x + shakeOff.x, follow.y + shakeOff.y, -10f);
        }

        private void SnapCameraToPlayer()
        {
            if (_cam == null || Player == null) return;
            float aspect = _cam.aspect;
            float halfW = _cam.orthographicSize * aspect;
            float targetX = Mathf.Clamp(Player.Center.x, halfW, GameConst.WorldWidth - halfW);
            _cam.transform.position = new Vector3(targetX, Player.Center.y + GameConst.Px(40f), -10f);
        }

        // —— 对外回调 ——

        public bool OverlapsSpike(in AABB box)
        {
            for (int i = 0; i < _level.Spikes.Count; i++)
            {
                if (box.Overlaps(_level.Spikes[i])) return true;
            }
            return false;
        }

        public void AddShake(float amount)
        {
            _shake = Mathf.Max(_shake, amount);
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
