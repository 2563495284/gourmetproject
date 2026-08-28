using System;
using System.Collections;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 落桌瞬间的一次性灰尘。使用 Unity ParticleSystem 复刻参考 Godot 粒子材质中的
    /// 随机散射、鼠标速度影响、重力、阻尼、随机缩放/旋转与颜色生命周期。
    /// </summary>
    public sealed class DishDropDustView : MonoBehaviour
    {
        [SerializeField] private ParticleSystem _particles;
        [SerializeField] private Vector2 _emissionAreaScale = new(0.68f, 0.24f);
        [SerializeField, Min(0f)] private float _spreadPerOccupiedCellRoot = 0.35f;
        [SerializeField] private float _pointerVelocityInfluence = 0.16f;
        private ParticleSystem.Particle[] _particleBuffer;
        private Action<DishDropDustView> _release;
        private float _baseStartSizeMultiplier;
        private float _baseVelocityXMultiplier;
        private float _baseVelocityYMultiplier;
        private bool _baseParametersCaptured;
        private bool _releaseRequested;

        public static void Play(
            DishDropDustView prefab,
            Transform parent,
            Vector3 centerWorld,
            Vector2 footprintWorldSize,
            int occupiedCellCount,
            Vector2 pointerVelocityWorld)
        {
            if (prefab == null)
            {
                Debug.LogError($"{nameof(DishDropDustView)} 无法播放：没有配置灰尘 Prefab。");
                return;
            }

            DishDropDustView view = Instantiate(prefab, parent);
            view.Play(
                centerWorld,
                footprintWorldSize,
                occupiedCellCount,
                pointerVelocityWorld,
                release: null);
        }

        internal void PrepareForReuse()
        {
            EnsureBaseParameters();
            StopAllCoroutines();
            _release = null;
            _releaseRequested = false;
        }

        internal void ResetForPool()
        {
            _release = null;
            _releaseRequested = true;
            StopAllCoroutines();
            if (_particles != null)
            {
                _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                RestoreBaseParameters();
            }
        }

        internal void Play(
            Vector3 centerWorld,
            Vector2 footprintWorldSize,
            int occupiedCellCount,
            Vector2 pointerVelocityWorld,
            Action<DishDropDustView> release)
        {
            EnsureBaseParameters();
            if (_particles == null)
            {
                _release = release;
                _releaseRequested = false;
                Debug.LogError(
                    $"{nameof(DishDropDustView)} Prefab 缺少已配置的 ParticleSystem 引用。",
                    this);
                RequestRelease();
                return;
            }

            // StopEmittingAndClear 在 stopAction=Callback 时也可能触发停止回调。
            // 清理上一轮期间先屏蔽归还，避免对象尚未开始新一轮播放就被送回池。
            ParticleSystem.MainModule main = _particles.main;
            main.stopAction = ParticleSystemStopAction.Callback;
            _releaseRequested = true;
            _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _release = release;
            _releaseRequested = false;

            transform.position = centerWorld;

            float cellReference = Mathf.Max(
                0.05f,
                Mathf.Min(
                    Mathf.Max(0.05f, footprintWorldSize.x),
                    Mathf.Max(0.05f, footprintWorldSize.y)));
            float occupiedSpread = 1f
                + Mathf.Max(0f, Mathf.Sqrt(Mathf.Max(1, occupiedCellCount)) - 1f)
                * Mathf.Max(0f, _spreadPerOccupiedCellRoot);
            // Prefab 中的 Shape、尺寸和速度均以 1 个餐桌格为基准；
            // 运行时只根据本次食物占格做整体适配，不创建或重配任何粒子模块。
            ParticleSystem.ShapeModule shape = _particles.shape;
            if (shape.enabled)
            {
                shape.scale = new Vector3(
                    Mathf.Max(0.05f, footprintWorldSize.x * _emissionAreaScale.x * occupiedSpread),
                    Mathf.Max(0.05f, footprintWorldSize.y * _emissionAreaScale.y * occupiedSpread),
                    0.01f);
            }

            main.startSizeMultiplier = _baseStartSizeMultiplier * cellReference;
            ParticleSystem.VelocityOverLifetimeModule velocity = _particles.velocityOverLifetime;
            if (velocity.enabled)
            {
                velocity.xMultiplier = _baseVelocityXMultiplier * cellReference * occupiedSpread;
                velocity.yMultiplier = _baseVelocityYMultiplier * cellReference * occupiedSpread;
            }

            _particles.Play();

            // Burst 由 Prefab 自然播放。不要在这里手动 Simulate：首帧推进会暂停一次性系统，
            // 使一次性粒子在真正渲染前结束。下一帧再叠加释放方向速度；自然停止后由 Callback 归还池。
            if (_pointerVelocityInfluence > 0f && pointerVelocityWorld.sqrMagnitude > 0.0001f)
            {
                StartCoroutine(ApplyReleaseVelocityNextFrame(
                    pointerVelocityWorld * _pointerVelocityInfluence));
            }
        }

        private IEnumerator ApplyReleaseVelocityNextFrame(Vector3 releaseVelocity)
        {
            yield return null;
            if (_particles == null || !_particles.IsAlive(true))
            {
                yield break;
            }

            int capacity = Mathf.Max(1, _particles.main.maxParticles);
            if (_particleBuffer == null || _particleBuffer.Length < capacity)
            {
                _particleBuffer = new ParticleSystem.Particle[capacity];
            }

            int count = _particles.GetParticles(_particleBuffer);
            for (int i = 0; i < count; i++)
            {
                _particleBuffer[i].velocity += releaseVelocity;
            }

            _particles.SetParticles(_particleBuffer, count);
        }

        private void EnsureBaseParameters()
        {
            if (_baseParametersCaptured || _particles == null)
            {
                return;
            }

            ParticleSystem.MainModule main = _particles.main;
            ParticleSystem.VelocityOverLifetimeModule velocity = _particles.velocityOverLifetime;
            _baseStartSizeMultiplier = main.startSizeMultiplier;
            _baseVelocityXMultiplier = velocity.xMultiplier;
            _baseVelocityYMultiplier = velocity.yMultiplier;
            _baseParametersCaptured = true;
        }

        private void RestoreBaseParameters()
        {
            if (!_baseParametersCaptured || _particles == null)
            {
                return;
            }

            ParticleSystem.MainModule main = _particles.main;
            main.stopAction = ParticleSystemStopAction.Callback;
            main.startSizeMultiplier = _baseStartSizeMultiplier;
            ParticleSystem.VelocityOverLifetimeModule velocity = _particles.velocityOverLifetime;
            velocity.xMultiplier = _baseVelocityXMultiplier;
            velocity.yMultiplier = _baseVelocityYMultiplier;
        }

        private void OnParticleSystemStopped()
        {
            RequestRelease();
        }

        private void RequestRelease()
        {
            if (_releaseRequested)
            {
                return;
            }

            _releaseRequested = true;
            Action<DishDropDustView> release = _release;
            _release = null;
            if (release != null)
            {
                release(this);
            }
            else if (Application.isPlaying)
            {
                Destroy(gameObject);
            }
            else
            {
                DestroyImmediate(gameObject);
            }
        }
    }
}
