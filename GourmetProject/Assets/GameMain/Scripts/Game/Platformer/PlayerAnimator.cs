using UnityEngine;

namespace GourmetProject.Game.Platformer
{
    /// <summary>
    /// 玩家逐帧动画（代码驱动，对应资源清单 2.1 / 4.2 的帧率与状态机）。
    /// 按 PlayerController 状态在 Idle/Run/Jump 间切换，并叠加打火机姿态层。
    /// </summary>
    public sealed class PlayerAnimator : MonoBehaviour
    {
        private SpriteRenderer _sr;
        private PlayerController _player;
        private EnergySystem _energy;

        private Sprite[] _idle, _run, _jump, _wallslide, _lighterIdle, _lighterRun;
        private float _timer;
        private int _frame;

        public void Init(SpriteRenderer sr, PlayerController player, EnergySystem energy)
        {
            _sr = sr;
            _player = player;
            _energy = energy;

            _idle = SpriteSheet.LoadFrames("Sprites/Characters/player_idle_sheet", 16, 32);
            _run = SpriteSheet.LoadFrames("Sprites/Characters/player_run_sheet", 16, 32);
            _jump = SpriteSheet.LoadFrames("Sprites/Characters/player_jump_sheet", 16, 32);
            _wallslide = SpriteSheet.LoadFrames("Sprites/Characters/player_wallslide_sheet", 16, 32);
            _lighterIdle = SpriteSheet.LoadFrames("Sprites/Characters/player_lighter_idle_sheet", 16, 32);
            _lighterRun = SpriteSheet.LoadFrames("Sprites/Characters/player_lighter_run_sheet", 16, 32);
        }

        private void LateUpdate()
        {
            if (_sr == null || _player == null) return;

            bool grounded = _player.Grounded;
            bool lighter = _energy != null && _energy.LighterOn;
            float speed = Mathf.Abs(_player.Velocity.x);

            Sprite[] clip;
            float fps;

            if (!grounded)
            {
                // 跳跃：帧0=上升, 帧1=下落（非循环，按 vy 选帧）。
                clip = _jump;
                fps = 0f;
                SetClip(clip, fps);
                if (clip != null && clip.Length >= 2)
                {
                    _sr.sprite = _player.Velocity.y > 0f ? clip[0] : clip[1];
                }
                return;
            }

            if (speed > 1.0f)
            {
                clip = lighter && _lighterRun.Length > 0 ? _lighterRun : _run;
                fps = 10f;
            }
            else
            {
                clip = lighter && _lighterIdle.Length > 0 ? _lighterIdle : _idle;
                fps = 8f;
            }

            SetClip(clip, fps);
        }

        private void SetClip(Sprite[] clip, float fps)
        {
            if (clip == null || clip.Length == 0) return;

            if (fps <= 0f)
            {
                _frame = 0;
                _sr.sprite = clip[0];
                return;
            }

            _timer += Time.deltaTime;
            float frameDur = 1f / fps;
            while (_timer >= frameDur)
            {
                _timer -= frameDur;
                _frame++;
            }
            _sr.sprite = clip[_frame % clip.Length];
        }
    }
}
