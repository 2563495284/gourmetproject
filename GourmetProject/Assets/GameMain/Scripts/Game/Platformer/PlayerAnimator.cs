using UnityEngine;

namespace GourmetProject.Game.Platformer
{
    /// <summary>
    /// 玩家动画驱动器：不再逐帧切 Sprite，而是按 <see cref="PlayerController"/> 状态驱动 Animator
    /// （状态名见 <c>States</c>，由 AnimationAuthoring 生成的 AnimatorController 提供对应 AnimationClip）。
    /// 打火机姿态作为独立状态变体（LighterIdle/LighterRun）。
    /// </summary>
    public sealed class PlayerAnimator : MonoBehaviour
    {
        // 与 AnimatorController 中的状态名严格一致（AnimationAuthoring 生成）。
        public const string StateIdle = "Idle";
        public const string StateRun = "Run";
        public const string StateJumpUp = "JumpUp";
        public const string StateJumpDown = "JumpDown";
        public const string StateWallslide = "Wallslide";
        public const string StateLighterIdle = "LighterIdle";
        public const string StateLighterRun = "LighterRun";

        private Animator _animator;
        private PlayerController _player;
        private EnergySystem _energy;
        private int _currentHash;

        public void Init(Animator animator, PlayerController player, EnergySystem energy)
        {
            _animator = animator;
            _player = player;
            _energy = energy;
            _currentHash = 0;
        }

        private void LateUpdate()
        {
            if (_animator == null || _player == null) return;

            bool grounded = _player.Grounded;
            bool lighter = _energy != null && _energy.LighterOn;
            float speed = Mathf.Abs(_player.Velocity.x);

            string state;
            if (!grounded)
            {
                if (_player.OnWall) state = StateWallslide;
                else state = _player.Velocity.y > 0f ? StateJumpUp : StateJumpDown;
            }
            else if (speed > 1.0f)
            {
                state = lighter ? StateLighterRun : StateRun;
            }
            else
            {
                state = lighter ? StateLighterIdle : StateIdle;
            }

            Play(state);
        }

        private void Play(string state)
        {
            int hash = Animator.StringToHash(state);
            if (hash == _currentHash) return;
            _currentHash = hash;
            _animator.Play(hash, 0, 0f);
        }
    }
}
