using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BreakInfinity;
using DG.Tweening;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using GourmetProject.Runtime;
using GourmetProject.Runtime.Pooling;
using Unity.Profiling;
using UnityEngine;
using TMPro;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 结算期间的程序化餐桌舞台：负责聚光、独立压暗、来源标题、结果盖章与最终亮相。
    /// 不持有计分状态，取消或结束时可无条件清理。
    /// </summary>
    public sealed class SettlementStageView : MonoBehaviour
    {
        internal const float DefaultPendingDishBrightness = 0.28f;
        private const int SpritePoolPrewarm = 12;
        private const int SpritePoolMaxInactive = 32;
        private const int LabelPoolPrewarm = 8;
        private const int LabelPoolMaxInactive = 24;
        private const int FinaleLabelPoolPrewarm = 1;
        private const int FinaleLabelPoolMaxInactive = 2;
        private static readonly ProfilerMarker TransientsMarker =
            new("Gourmet.Settlement.Transients");

        private readonly List<GameObject> _transients = new();
        private readonly Dictionary<GameObject, TransientRegistration> _transientPools = new();
        private readonly HashSet<int> _completedDishInstanceIds = new();
        private IReadOnlyDictionary<int, DishPieceView> _dishViews;
        private DiningTableCoordinateMapper _mapper;
        private Transform _fxRoot;
        private float _visualScale = 1f;

        [Header("固定资源（prefab 绑定）")]
        [SerializeField] private SettlementStageLabelView _labelPrefab;
        [SerializeField] private SettlementStageLabelView _finaleLabelPrefab;
        [SerializeField] private SpriteRenderer _spritePrefab;

        [Header("食物亮度层级")]
        [SerializeField, Range(0f, 1f)] private float _completedDishBrightness = 0.52f;
        [SerializeField, Range(0f, 1f)] private float _pendingDishBrightness = DefaultPendingDishBrightness;
        [SerializeField, Range(0f, 1f)] private float _scopeDishBrightness = 0.92f;
        [SerializeField, Range(0f, 1f)] private float _sweetTransferSourceBrightness = 0.97f;

        [Header("聚焦过渡")]
        [SerializeField, Min(0f)] private float _dishFocusFadeDuration = 0.12f;

        [Header("结果标签布局")]
        [SerializeField, Min(0.01f)] private float _resultLabelWidth = 1.8f;
        [SerializeField, Min(0.01f)] private float _resultLabelHeight = 0.48f;
        [SerializeField, Range(0f, 0.2f)] private float _resultLabelViewportPadding =
            SettlementResultLabelLayout.DefaultViewportPadding;

        private GameObject _groupSpotlight;
        private GameObject _chapterSpotlight;
        private Tween _chapterSpotlightTween;
        private int _chapterDishInstanceId;
        private readonly List<GameObject> _heldLabels = new();
        private SettlementEffectGroup _resultHitSoundGroup;
        private Camera _worldCamera;
        private GameObjectPool _spritePool;
        private GameObjectPool _labelPool;
        private GameObjectPool _finaleLabelPool;
        private int _nextTransientGeneration;

        private readonly struct TransientRegistration
        {
            public TransientRegistration(GameObjectPool pool, int generation)
            {
                Pool = pool;
                Generation = generation;
            }

            public GameObjectPool Pool { get; }
            public int Generation { get; }
        }

        internal float PendingDishBrightness => _pendingDishBrightness;

        private void Awake()
        {
            EnsurePools();
        }

        public void Configure(
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            DiningTableCoordinateMapper mapper,
            Transform fxRoot,
            Camera worldCamera,
            float visualScale = 1f)
        {
            // 餐桌入场已经通过 DishPieceView 的结算亮度通道渐暗到“未结算”。
            // 同一张餐桌交接给舞台时保留这层亮度，避免 Configure 先恢复全亮、
            // 随后第一章节又重新压暗造成一次肉眼可见的闪断。
            bool isSameDishCollection = ReferenceEquals(_dishViews, dishViews);
            ResetPresentationImmediate(clearDishFocus: !isSameDishCollection);
            _dishViews = dishViews;
            _mapper = mapper;
            _fxRoot = fxRoot != null ? fxRoot : transform;
            _worldCamera = worldCamera;
            _visualScale = Mathf.Max(0.0001f, visualScale);
            ApplySettlementProgressFocus(0f);
        }

        internal void PlayTransientEffect(
            Transform parent,
            Vector3 anchor,
            string sourceName,
            string effectText,
            Color theme,
            float duration,
            float delay,
            CancellationToken cancellationToken,
            float visualScale = 1f)
        {
            _ = PlayTransientEffectAsync(
                parent,
                anchor,
                sourceName,
                effectText,
                theme,
                duration,
                delay,
                cancellationToken,
                visualScale);
        }

        private async Awaitable PlayTransientEffectAsync(
            Transform parent,
            Vector3 anchor,
            string sourceName,
            string effectText,
            Color theme,
            float duration,
            float delay,
            CancellationToken cancellationToken,
            float visualScale)
        {
            try
            {
                if (delay > 0f)
                {
                    await Awaitable.WaitForSecondsAsync(delay, cancellationToken);
                }

                await SpawnLabelAsync(
                    anchor,
                    ReadableName(sourceName, "技能触发"),
                    effectText,
                    theme,
                    duration,
                    cancellationToken,
                    parentOverride: parent,
                    visualScaleOverride: visualScale);
            }
            catch (OperationCanceledException)
            {
                // 页面关闭或新一轮演出开始时，尚未完成的即时提示直接结束。
            }
        }

        public async Awaitable PlayBaseAsync(
            DishPieceView view,
            string dishName,
            BigDouble contribution,
            float duration,
            CancellationToken cancellationToken)
        {
            if (view != null)
            {
                _ = PlayFeedbackSafelyAsync(
                    view,
                    SettlementDishFeedbackKind.DishBase,
                    cancellationToken,
                    durationScale: Mathf.Max(0.05f, duration / 0.45f));
            }

            // 基础值已由常驻食物数值徽章同步揭示，不再额外生成一条
            // “食物名 / 基础贡献”结果文本，避免与后续“分数 +N”重复。
            await Awaitable.WaitForSecondsAsync(Mathf.Max(0.0001f, duration), cancellationToken);
        }

        internal void BeginDishChapter(
            int dishInstanceId,
            float revealDuration,
            CancellationToken cancellationToken)
        {
            DestroyGroupSpotlight();
            DestroyChapterSpotlight();
            _completedDishInstanceIds.Remove(dishInstanceId);
            _chapterDishInstanceId = dishInstanceId;
            ApplyChapterFocus(Mathf.Min(_dishFocusFadeDuration, revealDuration));

            DishPieceView view = TryGetDish(dishInstanceId);
            if (view == null)
            {
                return;
            }

            view.SetDishValueBadgeChapterFocused(true, revealDuration);
            SpawnChapterSpotlight(view, revealDuration);
            _ = PlayChapterStartAccentSafelyAsync(view, revealDuration, cancellationToken);
            _ = PlayFeedbackSafelyAsync(
                view,
                SettlementDishFeedbackKind.DishChapterStarted,
                cancellationToken,
                durationScale: Mathf.Max(0.05f, revealDuration / 0.18f));
        }

        internal async Awaitable EndDishChapterAsync(
            int dishInstanceId,
            float duration,
            CancellationToken cancellationToken)
        {
            DestroyGroupSpotlight();
            if (dishInstanceId > 0)
            {
                _chapterDishInstanceId = dishInstanceId;
            }

            ApplyChapterFocus();
            DishPieceView view = TryGetDish(_chapterDishInstanceId);
            Awaitable completionRingTask = default;
            bool hasCompletionRing = false;
            if (view != null)
            {
                view.SetSettlementFocus(1f, _dishFocusFadeDuration);
                _ = PlayFeedbackSafelyAsync(
                    view,
                    SettlementDishFeedbackKind.DishChapterCompleted,
                    cancellationToken,
                    durationScale: Mathf.Max(0.05f, duration / 0.14f));
                completionRingTask = PlayImpactRingAsync(
                    view,
                    SettlementColorPalette.BaseScore,
                    SettlementImpactTier.Chain,
                    cancellationToken,
                    durationOverride: duration);
                hasCompletionRing = true;
            }

            int completingDishId = _chapterDishInstanceId;
            await Awaitable.WaitForSecondsAsync(Mathf.Max(0.0001f, duration), cancellationToken);
            if (hasCompletionRing)
            {
                await completionRingTask;
            }

            if (_chapterDishInstanceId != completingDishId)
            {
                return;
            }

            view?.SetDishValueBadgeChapterFocused(false, Mathf.Min(0.10f, duration));
            if (completingDishId > 0)
            {
                _completedDishInstanceIds.Add(completingDishId);
            }

            _chapterDishInstanceId = 0;
            DestroyChapterSpotlight();
            ApplySettlementProgressFocus();
        }

        internal async Awaitable FocusSourceAsync(
            SettlementEffectGroup group,
            float duration,
            CancellationToken cancellationToken,
            bool actorAlreadyIntroduced = false,
            int cakeLayerCount = -1)
        {
            EndGroupImmediate();
            if (group == null)
            {
                return;
            }

            ApplyFocus(group.ActorDishInstanceId, group.TargetDishIds);
            if (group.Trace?.Kind == SkillExecutionKind.SweetTransfer
                && group.Trace.OwnerDishInstanceId > 0
                && group.Trace.OwnerDishInstanceId != group.ActorDishInstanceId)
            {
                TryGetDish(group.Trace.OwnerDishInstanceId)?.SetSettlementFocus(
                    group.Trace.OwnerDishInstanceId == _chapterDishInstanceId
                        ? 1f
                        : _sweetTransferSourceBrightness);
            }

            Color theme = ThemeFor(group.Trace, group.Source);
            DishPieceView actor = TryGetDish(group.ActorDishInstanceId);
            Vector3 anchor = actor != null
                ? actor.WorldBounds.center
                    + Vector3.up * (actor.WorldBounds.extents.y + 0.42f * _visualScale)
                : _mapper.Center + Vector3.up * (0.72f * _visualScale);
            SpawnSpotlight(actor != null ? actor.WorldBounds : default, anchor, theme);

            if (actor != null && !actorAlreadyIntroduced)
            {
                // 来源食物开始技能触发表现（放大/摆动）的同一瞬间播放单体音效。
                GameApp.Audio.PlaySettlementHit(1);
                _ = PlayFeedbackSafelyAsync(
                    actor,
                    ActorFeedbackFor(group.Trace),
                    cancellationToken,
                    durationScale: Mathf.Max(0.05f, duration / 0.55f));
            }

            await SpawnLabelAsync(
                anchor,
                cakeLayerCount >= 0
                    ? $"欢乐蛋糕 ×{cakeLayerCount}"
                    : "技能触发",
                cakeLayerCount >= 0
                    ? "层数 Buff 爆发"
                    : group.Trace?.Kind == SkillExecutionKind.SweetTransfer
                        ? ReadableName(group.Trace.SkillName, group.SourceName)
                        : group.SourceName,
                theme,
                duration,
                cancellationToken,
                holdUntilCleared: true);
        }

        internal readonly struct SweetTransferHandoffVisual
        {
            public SweetTransferHandoffVisual(
                SettlementSweetTransferPresentationContext context,
                DishPieceView source,
                DishPieceView executor)
            {
                Context = context;
                Source = source;
                Executor = executor;
            }

            public SettlementSweetTransferPresentationContext Context { get; }
            public DishPieceView Source { get; }
            public DishPieceView Executor { get; }
        }

        /// <summary>
        /// 同一波甜蜜传递的所有 source→executor 粒子同时起飞，只等待最长飞行时间。
        /// 不再逐目标弹出「技能来源 / 接收并执行」舞台字。
        /// </summary>
        internal async Awaitable<IReadOnlyList<SettlementSweetTransferPresentationContext>>
            PlaySweetTransferHandoffsAsync(
            IReadOnlyList<SweetTransferHandoffVisual> handoffs,
            SweetTransferParticleView particlePrefab,
            GameObjectPool particlePool,
            float travelDuration,
            CancellationToken cancellationToken)
        {
            EndGroupImmediate();
            if (handoffs == null || handoffs.Count == 0)
            {
                return System.Array.Empty<SettlementSweetTransferPresentationContext>();
            }

            var flights = new List<SweetTransferParticleView>(handoffs.Count);
            var arrivedContexts = new List<SettlementSweetTransferPresentationContext>(handoffs.Count);
            try
            {
                for (int i = 0; i < handoffs.Count; i++)
                {
                    SweetTransferHandoffVisual handoff = handoffs[i];
                    DishPieceView source = handoff.Source;
                    DishPieceView executor = handoff.Executor;
                    source?.SetSettlementFocus(1f);
                    executor?.SetSettlementFocus(1f);
                    if (executor != null)
                    {
                        // 起飞阶段只保留接收者的持续高亮状态。接收者的缩放/旋转反馈
                        // 由粒子抵达后的结果节拍播放，避免目标在粒子刚起飞时先抖一下。
                        executor.BeginSweetTransferExecutorFeedback();
                    }

                    if (source == null
                        || executor == null
                        || handoff.Context.IsSelfTransfer)
                    {
                        source?.SetSettlementFocus(
                            handoff.Context.SourceDishInstanceId == _chapterDishInstanceId
                                ? 1f
                                : _sweetTransferSourceBrightness);
                        continue;
                    }

                    SweetTransferParticleView flight = SweetTransferParticleView.Begin(
                        particlePrefab,
                        _fxRoot,
                        source.WorldBounds.center,
                        executor.WorldBounds.center,
                        travelDuration,
                        visualScale: _visualScale,
                        pool: particlePool);
                    if (flight != null)
                    {
                        flights.Add(flight);
                        arrivedContexts.Add(handoff.Context);
                    }

                    source.SetSettlementFocus(
                        handoff.Context.SourceDishInstanceId == _chapterDishInstanceId
                            ? 1f
                            : _sweetTransferSourceBrightness);
                }

                if (flights.Count > 0)
                {
                    await Awaitable.WaitForSecondsAsync(
                        Mathf.Max(0.0001f, travelDuration),
                        cancellationToken);
                }

                return arrivedContexts;
            }
            finally
            {
                for (int i = 0; i < flights.Count; i++)
                {
                    SweetTransferParticleView flight = flights[i];
                    if (flight != null)
                    {
                        if (particlePool != null)
                        {
                            particlePool.Release(flight);
                        }
                        else
                        {
                            Destroy(flight.gameObject);
                        }
                    }
                }
            }
        }

        internal void ApplyWaveFocus(
            IReadOnlyCollection<int> sourceDishIds,
            IReadOnlyCollection<int> executorDishIds,
            IReadOnlyCollection<int> targetDishIds)
        {
            if (_dishViews == null)
            {
                return;
            }

            foreach (KeyValuePair<int, DishPieceView> entry in _dishViews)
            {
                DishPieceView view = entry.Value;
                if (view == null)
                {
                    continue;
                }

                float brightness = ProgressBrightness(entry.Key);
                if (sourceDishIds != null && sourceDishIds.Contains(entry.Key))
                {
                    brightness = 1f;
                }
                else if (executorDishIds != null && executorDishIds.Contains(entry.Key))
                {
                    brightness = 1f;
                }
                else if (targetDishIds != null && targetDishIds.Contains(entry.Key))
                {
                    brightness = _scopeDishBrightness;
                }

                if (entry.Key == _chapterDishInstanceId)
                {
                    brightness = 1f;
                }

                view.SetSettlementFocus(brightness);
            }
        }

        internal async Awaitable PlaySweetTransferBuffTriggerAsync(
            DishPieceView transferSource,
            DishPieceView buffOwner,
            SweetTransferParticleView particlePrefab,
            GameObjectPool particlePool,
            float duration,
            CancellationToken cancellationToken)
        {
            if (transferSource == null || buffOwner == null)
            {
                return;
            }

            Color theme = SettlementColorPalette.SweetTransfer;
            await SweetTransferParticleView.PlayAsync(
                particlePrefab,
                _fxRoot,
                transferSource.WorldBounds.center,
                buffOwner.WorldBounds.center,
                duration,
                cancellationToken,
                theme,
                _visualScale,
                particlePool);
            await PlayFeedbackSafelyAsync(
                buffOwner,
                SettlementDishFeedbackKind.GenericSkillTriggered,
                cancellationToken,
                // 命中反馈使用原始 0.34s 基准，不随结算速度配置额外拉伸。
                durationScale: Mathf.Max(0.05f, duration / 0.34f));
        }

        internal async Awaitable PlaySweetTransferFailureAsync(
            DishPieceView source,
            SweetTransferParticleView particlePrefab,
            GameObjectPool particlePool,
            float duration,
            CancellationToken cancellationToken)
        {
            if (source == null)
            {
                return;
            }

            await PlayFeedbackSafelyAsync(
                source,
                SettlementDishFeedbackKind.SweetTransferSkillTriggered,
                cancellationToken,
                durationScale: Mathf.Max(0.05f, duration / 0.42f));
            await SweetTransferParticleView.PlayFailureAsync(
                particlePrefab,
                _fxRoot,
                source.WorldBounds.center,
                duration,
                cancellationToken,
                _visualScale,
                particlePool);
        }

        internal async Awaitable ShowScopeAsync(
            SettlementEffectGroup group,
            float duration,
            CancellationToken cancellationToken)
        {
            await ShowScopeAsync(
                group?.TargetDishIds,
                duration,
                cancellationToken);
        }

        internal async Awaitable ShowScopeAsync(
            IReadOnlyCollection<int> targetDishIds,
            float duration,
            CancellationToken cancellationToken)
        {
            var shakenTargets = new List<DishPieceView>();
            if (targetDishIds != null && _dishViews != null)
            {
                foreach (int id in targetDishIds)
                {
                    DishPieceView target = TryGetDish(id);
                    if (target == null)
                    {
                        continue;
                    }

                    shakenTargets.Add(target);
                }
            }

            for (int i = 0; i < shakenTargets.Count; i++)
            {
                DishPieceView target = shakenTargets[i];
                target.SetSettlementFocus(
                    target.Instance != null && target.Instance.Id == _chapterDishInstanceId
                        ? 1f
                        : _scopeDishBrightness);
                target.PlayScopeAffectedShake(Mathf.Max(0.05f, duration / 0.40f));
            }

            await Awaitable.WaitForSecondsAsync(Mathf.Max(0.0001f, duration), cancellationToken);
        }

        internal async Awaitable PlayCakeLayerChargeAsync(
            float duration,
            CancellationToken cancellationToken)
        {
            Vector3 center = _mapper.Center;
            Color chargeColor = SettlementColorPalette.WithAlpha(
                SettlementColorPalette.CakeLayer,
                0.42f);
            GameObject outer = CreateSprite(
                "CakeLayerBuffChargeOuter",
                center,
                chargeColor,
                -5);
            GameObject inner = CreateSprite(
                "CakeLayerBuffChargeInner",
                center,
                SettlementColorPalette.WithAlpha(chargeColor, 0.58f),
                -4);
            int outerGeneration = TransientGeneration(outer);
            int innerGeneration = TransientGeneration(inner);
            if (outer == null || inner == null)
            {
                try
                {
                    await Awaitable.WaitForSecondsAsync(
                        Mathf.Max(0.0001f, duration),
                        cancellationToken);
                }
                finally
                {
                    if (outer != null)
                    {
                        ReleaseTransient(outer, outerGeneration);
                    }
                    if (inner != null)
                    {
                        ReleaseTransient(inner, innerGeneration);
                    }
                }

                return;
            }

            SpriteRenderer outerRenderer = outer.GetComponent<SpriteRenderer>();
            SpriteRenderer innerRenderer = inner.GetComponent<SpriteRenderer>();
            Color outerInitial = outerRenderer.color;
            Color innerInitial = innerRenderer.color;
            outer.transform.localScale = Vector3.one * (3.4f * _visualScale);
            inner.transform.localScale = Vector3.one * (2.2f * _visualScale);
            Tween tween = DOVirtual.Float(0f, 1f, Mathf.Max(0.0001f, duration), progress =>
                {
                    if (outer == null || inner == null)
                    {
                        return;
                    }

                    float eased = progress * progress * progress;
                    outer.transform.localScale = Vector3.one
                        * (Mathf.Lerp(3.4f, 0.62f, eased) * _visualScale);
                    inner.transform.localScale = Vector3.one
                        * (Mathf.Lerp(2.2f, 0.34f, eased) * _visualScale);
                    outerRenderer.color = SettlementColorPalette.WithAlpha(
                        outerInitial,
                        Mathf.Lerp(0.16f, outerInitial.a, eased));
                    innerRenderer.color = SettlementColorPalette.WithAlpha(
                        innerInitial,
                        Mathf.Lerp(0.08f, innerInitial.a, eased));
                })
                .SetEase(Ease.Linear)
                .SetId(outer)
                .SetLink(outer);

            try
            {
                await PresentationTween.AwaitCompletionAsync(tween, cancellationToken);
            }
            finally
            {
                tween?.Kill();
                if (outer != null)
                {
                    ReleaseTransient(outer, outerGeneration);
                }
                if (inner != null)
                {
                    ReleaseTransient(inner, innerGeneration);
                }
            }
        }

        internal void PlayCakeLayerBurstImpact(
            CakeLayerBurstStepProfile profile,
            Color theme,
            float duration,
            CancellationToken cancellationToken)
        {
            _ = PlayCakeLayerBurstImpactSafelyAsync(
                profile,
                theme,
                duration,
                cancellationToken);
        }

        private async Awaitable PlayCakeLayerBurstImpactSafelyAsync(
            CakeLayerBurstStepProfile profile,
            Color theme,
            float duration,
            CancellationToken cancellationToken)
        {
            try
            {
                await PlayCakeLayerBurstImpactAsync(
                    profile,
                    theme,
                    duration,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 结算被中断时，临时冲击波由舞台清理流程统一回收。
            }
        }

        private async Awaitable PlayCakeLayerBurstImpactAsync(
            CakeLayerBurstStepProfile profile,
            Color theme,
            float duration,
            CancellationToken cancellationToken)
        {
            Vector3 center = _mapper.Center;
            float strength = Mathf.Clamp01(profile.Progress);
            duration = Mathf.Max(0.0001f, duration);
            float endScale = Mathf.Lerp(2.6f, 5.4f, strength) * _visualScale;
            Color primaryColor = SettlementColorPalette.WithAlpha(
                theme,
                Mathf.Lerp(0.34f, 0.62f, strength));
            GameObject primary = CreateSprite(
                "CakeLayerBuffTableImpact",
                center,
                primaryColor,
                -3);
            GameObject echo = profile.ImpactTier >= SettlementImpactTier.Chain
                ? CreateSprite(
                    "CakeLayerBuffTableImpactEcho",
                    center,
                    SettlementColorPalette.WithAlpha(primaryColor, primaryColor.a * 0.58f),
                    -4)
                : null;
            int primaryGeneration = TransientGeneration(primary);
            int echoGeneration = TransientGeneration(echo);
            if (primary == null)
            {
                if (echo != null)
                {
                    ReleaseTransient(echo, echoGeneration);
                }
                return;
            }

            SpriteRenderer primaryRenderer = primary.GetComponent<SpriteRenderer>();
            SpriteRenderer echoRenderer = echo != null
                ? echo.GetComponent<SpriteRenderer>()
                : null;
            primary.transform.localScale = Vector3.one * (0.30f * _visualScale);
            if (echo != null)
            {
                echo.transform.localScale = Vector3.one * (0.22f * _visualScale);
            }

            Tween tween = DOVirtual.Float(0f, 1f, duration, progress =>
                {
                    float eased = Mathf.SmoothStep(0f, 1f, progress);
                    if (primary != null)
                    {
                        primary.transform.localScale = Vector3.one
                            * Mathf.Lerp(0.30f * _visualScale, endScale, eased);
                        primaryRenderer.color = SettlementColorPalette.WithAlpha(
                            primaryColor,
                            primaryColor.a * (1f - eased));
                    }

                    if (echo != null)
                    {
                        float echoProgress = Mathf.Clamp01((progress - 0.14f) / 0.86f);
                        float echoEased = Mathf.SmoothStep(0f, 1f, echoProgress);
                        echo.transform.localScale = Vector3.one
                            * Mathf.Lerp(
                                0.22f * _visualScale,
                                endScale * 1.16f,
                                echoEased);
                        echoRenderer.color = SettlementColorPalette.WithAlpha(
                            primaryColor,
                            primaryColor.a * 0.58f * (1f - echoEased));
                    }
                })
                .SetEase(Ease.Linear)
                .SetId(primary)
                .SetLink(primary);

            try
            {
                await PresentationTween.AwaitCompletionAsync(tween, cancellationToken);
            }
            finally
            {
                tween?.Kill();
                if (primary != null)
                {
                    ReleaseTransient(primary, primaryGeneration);
                }
                if (echo != null)
                {
                    ReleaseTransient(echo, echoGeneration);
                }
            }
        }

        internal static Vector3 ResultLabelScatterOffset(float visualScale, float xUnit, float yUnit)
        {
            float scale = Mathf.Max(0.0001f, visualScale);
            return new Vector3(
                Mathf.Clamp(xUnit, -1f, 1f) * 0.12f * scale,
                Mathf.Clamp01(yUnit) * 0.18f * scale,
                0f);
        }

        private Vector3 ResultLabelAnchor(DishPieceView target)
        {
            if (target == null)
            {
                return _mapper.Center + Vector3.up * (0.20f * _visualScale);
            }

            return target.DishValueBadgeWorldPosition
                + Vector3.down * (0.42f * _visualScale);
        }

        internal ResultLabelLayoutPlan BuildResultLabelLayoutPlan(
            IReadOnlyList<ResultLabelLayoutOccurrence> occurrences)
        {
            if (occurrences == null || occurrences.Count == 0)
            {
                return SettlementResultLabelLayout.ResolveBatch(
                    _worldCamera,
                    Array.Empty<ResultLabelLayoutRequest>(),
                    new Vector2(
                        _resultLabelWidth * _visualScale,
                        _resultLabelHeight * _visualScale),
                    _resultLabelViewportPadding);
            }

            var requests = new ResultLabelLayoutRequest[occurrences.Count];
            for (int i = 0; i < occurrences.Count; i++)
            {
                ResultLabelLayoutOccurrence occurrence = occurrences[i];
                requests[i] = new ResultLabelLayoutRequest(
                    occurrence.TargetKey,
                    ResultLabelAnchor(occurrence.Target));
            }

            return SettlementResultLabelLayout.ResolveBatch(
                _worldCamera,
                requests,
                new Vector2(
                    _resultLabelWidth * _visualScale,
                    _resultLabelHeight * _visualScale),
                _resultLabelViewportPadding);
        }

        internal static bool IsLaunchResultLabel(ScoreLine line)
        {
            if (line == null)
            {
                return false;
            }

            return line.Kind == ScoreLineKind.TriggerSweetTransfer;
        }

        internal async Awaitable ShowSweetTransferLaunchAsync(
            IReadOnlyList<DishPieceView> sources,
            float duration,
            CancellationToken cancellationToken,
            bool holdUntilCleared = true)
        {
            if (sources == null || sources.Count == 0)
            {
                return;
            }

            Color theme = SettlementColorPalette.SweetTransfer;
            var tasks = new List<Awaitable>(sources.Count);
            for (int i = 0; i < sources.Count; i++)
            {
                DishPieceView source = sources[i];
                if (source == null)
                {
                    continue;
                }

                source.SetSettlementFocus(1f);
                Vector3 anchor = ResultLabelAnchor(source)
                    + ResultLabelScatterOffset(
                        _visualScale,
                        UnityEngine.Random.Range(-1f, 1f),
                        UnityEngine.Random.Range(0f, 1f));
                tasks.Add(SpawnLabelAsync(
                    anchor,
                    "甜蜜传递",
                    "触发甜蜜传递",
                    theme,
                    duration,
                    cancellationToken,
                    holdUntilCleared: holdUntilCleared,
                    headerSemanticColor: theme,
                    sortingOrder: WorldLabelSorting.NextOrder()));
            }

            for (int i = 0; i < tasks.Count; i++)
            {
                await tasks[i];
            }
        }

        internal async Awaitable ShowResultAsync(
            SettlementEffectGroup group,
            ScoreLine line,
            DishPieceView target,
            BigDouble dishContribution,
            BigDouble runningTotal,
            float duration,
            SettlementImpactTier impactTier,
            float audioPitch,
            bool playTargetFeedback,
            ResultLabelLayoutPlacement layoutPlacement,
            CancellationToken cancellationToken,
            bool holdUntilCleared = false,
            CakeLayerBurstStepProfile? cakeLayerBurstStep = null)
        {
            if (!ShouldShowResultLabel(line))
            {
                return;
            }

            Color theme = ResultThemeFor(line);
            if (target != null)
            {
                if (!cakeLayerBurstStep.HasValue)
                {
                    PlayResultHitSoundIfNeeded(group, audioPitch);
                }
                target.SetSettlementFocus(1f);
                if (playTargetFeedback)
                {
                    _ = PlayFeedbackSafelyAsync(
                        target,
                        cakeLayerBurstStep?.DishFeedbackKind ?? FeedbackFor(line),
                        cancellationToken,
                        durationScale: Mathf.Max(0.05f, duration / 0.80f));
                }
            }

            Vector3 anchor = layoutPlacement.Position;
            Awaitable impactTask = playTargetFeedback
                ? PlayImpactRingAsync(target, theme, impactTier, cancellationToken)
                : default;
            await SpawnLabelAsync(
                anchor,
                cakeLayerBurstStep.HasValue
                    ? CakeLayerBurstResultHeader(line, cakeLayerBurstStep.Value)
                    : ResultHeader(line),
                ResultText(line, dishContribution, runningTotal),
                theme,
                duration,
                cancellationToken,
                holdUntilCleared: holdUntilCleared,
                headerSemanticColor: ResultHeaderSemanticColorFor(line, theme),
                sortingOrder: WorldLabelSorting.NextOrder()
                    + layoutPlacement.StackIndex,
                verticalDriftDirection: layoutPlacement.VerticalDirection,
                impactScale: cakeLayerBurstStep.HasValue
                    ? Mathf.Lerp(1.02f, 1.10f, cakeLayerBurstStep.Value.Progress)
                    : 1f);
            if (playTargetFeedback)
            {
                await impactTask;
            }
        }

        private void PlayResultHitSoundIfNeeded(SettlementEffectGroup group, float pitch)
        {
            if (group == null || ReferenceEquals(_resultHitSoundGroup, group))
            {
                return;
            }

            _resultHitSoundGroup = group;
            var targetIds = new HashSet<int>();
            for (int i = 0; i < group.Lines.Count; i++)
            {
                int targetId = group.Lines[i].DishInstanceId;
                if (targetId > 0 && TryGetDish(targetId) != null)
                {
                    targetIds.Add(targetId);
                }
            }

            // 技能结果真正开始作用到食物时：单体用 multhit1，多个目标用 multhit2。
            GameApp.Audio.PlaySettlementHit(targetIds.Count, pitch);
        }

        public async Awaitable EndGroupAsync(float duration, CancellationToken cancellationToken)
        {
            RestoreChapterFocusOrProgress();
            DestroyGroupSpotlight();
            await Awaitable.WaitForSecondsAsync(Mathf.Max(0.0001f, duration), cancellationToken);
            ClearExpiredTransients();
        }

        public async Awaitable PlayFinaleAsync(
            BigDouble total,
            float duration,
            CancellationToken cancellationToken)
        {
            // 最终亮相直接解除上一组的聚焦状态，避免先全体压暗、再逐个恢复造成闪暗。
            _chapterDishInstanceId = 0;
            DestroyChapterSpotlight();
            ClearFocus();
            DestroyGroupSpotlight();
            ClearExpiredTransients();
            GameApp.Audio.PlaySettlementHit(2, 1.18f);
            if (_dishViews != null)
            {
                foreach (DishPieceView view in _dishViews.Values)
                {
                    if (view == null)
                    {
                        continue;
                    }

                    view.SetDishValueBadgeChapterFocused(false, 0f);
                    view.ClearSettlementFocus();
                    _ = PlayFeedbackSafelyAsync(
                        view,
                        SettlementDishFeedbackKind.DishBase,
                        cancellationToken,
                        durationScale: Mathf.Max(0.05f, duration / 1.20f));
                    await Awaitable.WaitForSecondsAsync(
                        Mathf.Min(0.035f, duration * 0.08f),
                        cancellationToken);
                }
            }

            Vector3 center = _mapper.Center
                + Vector3.up * (0.35f * _visualScale);
            GameObject ring = CreateSprite(
                "FinaleTableRing",
                center,
                SettlementColorPalette.WithAlpha(SettlementColorPalette.FinalScore, 0.42f),
                -4);
            int ringGeneration = TransientGeneration(ring);
            Awaitable labelTask = SpawnLabelAsync(
                center + Vector3.up * (0.62f * _visualScale),
                "本桌结算",
                $"总分  {ScoreNumberFormatter.Format(total)}",
                SettlementColorPalette.FinalScore,
                duration,
                cancellationToken,
                finalStamp: true);

            if (ring == null)
            {
                await labelTask;
                return;
            }

            ring.transform.localScale = Vector3.one
                * (0.28f * _visualScale);
            SpriteRenderer renderer = ring.GetComponent<SpriteRenderer>();
            Color initial = renderer.color;
            Tween ringTween = DOVirtual.Float(0f, 1f, Mathf.Max(0.0001f, duration), t =>
                {
                    if (ring == null || renderer == null)
                    {
                        return;
                    }

                    float eased = Mathf.SmoothStep(0f, 1f, t);
                    ring.transform.localScale = Vector3.one
                        * (Mathf.Lerp(0.28f, 6.2f, eased) * _visualScale);
                    Color color = initial;
                    color.a *= 1f - eased;
                    renderer.color = color;
                })
                .SetEase(Ease.Linear)
                .SetId(ring)
                .SetLink(ring);
            try
            {
                await PresentationTween.AwaitCompletionAsync(ringTween, cancellationToken);
                await labelTask;
            }
            finally
            {
                ReleaseTransient(ring, ringGeneration);
            }
        }

        public void ClearImmediate()
        {
            ResetPresentationImmediate(clearDishFocus: true);
        }

        private void ResetPresentationImmediate(bool clearDishFocus)
        {
            _chapterDishInstanceId = 0;
            _completedDishInstanceIds.Clear();
            if (_dishViews != null)
            {
                foreach (DishPieceView view in _dishViews.Values)
                {
                    view?.SetDishValueBadgeChapterFocused(false, 0f);
                }
            }

            if (clearDishFocus)
            {
                ClearFocus();
            }

            DestroyGroupSpotlight();
            DestroyChapterSpotlight();
            while (_transients.Count > 0)
            {
                int lastIndex = _transients.Count - 1;
                GameObject transient = _transients[lastIndex];
                _transients.RemoveAt(lastIndex);
                ReleaseTransient(transient);
            }
            _transientPools.Clear();
        }

        private void ApplyFocus(int actorDishInstanceId, IReadOnlyCollection<int> targetDishIds)
        {
            if (_dishViews == null)
            {
                return;
            }

            foreach (KeyValuePair<int, DishPieceView> entry in _dishViews)
            {
                DishPieceView view = entry.Value;
                if (view == null)
                {
                    continue;
                }

                bool primary = entry.Key == actorDishInstanceId
                    || entry.Key == _chapterDishInstanceId;
                float brightness = primary
                    ? 1f
                    : targetDishIds != null && targetDishIds.Contains(entry.Key)
                        ? _scopeDishBrightness
                        : ProgressBrightness(entry.Key);
                view.SetSettlementFocus(brightness, _dishFocusFadeDuration);
            }
        }

        private void ApplyChapterFocus(float? fadeDuration = null)
        {
            if (_dishViews == null)
            {
                return;
            }

            foreach (KeyValuePair<int, DishPieceView> entry in _dishViews)
            {
                DishPieceView view = entry.Value;
                if (view == null)
                {
                    continue;
                }

                view.SetSettlementFocus(
                    entry.Key == _chapterDishInstanceId
                        ? 1f
                        : ProgressBrightness(entry.Key),
                    fadeDuration ?? _dishFocusFadeDuration);
            }
        }

        private void RestoreChapterFocusOrProgress()
        {
            if (_chapterDishInstanceId > 0)
            {
                ApplyChapterFocus();
                return;
            }

            ApplySettlementProgressFocus();
        }

        private void ClearFocus()
        {
            if (_dishViews == null)
            {
                return;
            }

            foreach (DishPieceView view in _dishViews.Values)
            {
                view?.ClearSettlementFocus();
            }
        }

        private void ApplySettlementProgressFocus(float? fadeDuration = null)
        {
            if (_dishViews == null)
            {
                return;
            }

            foreach (KeyValuePair<int, DishPieceView> entry in _dishViews)
            {
                entry.Value?.SetSettlementFocus(
                    ProgressBrightness(entry.Key),
                    fadeDuration ?? _dishFocusFadeDuration);
            }
        }

        private float ProgressBrightness(int dishInstanceId)
        {
            return _completedDishInstanceIds.Contains(dishInstanceId)
                ? _completedDishBrightness
                : _pendingDishBrightness;
        }

        private void EndGroupImmediate()
        {
            RestoreChapterFocusOrProgress();
            DestroyGroupSpotlight();
            ClearExpiredTransients();
        }

        private void SpawnChapterSpotlight(DishPieceView view, float revealDuration)
        {
            if (view == null)
            {
                return;
            }

            Bounds bounds = view.WorldBounds;
            _chapterSpotlight = CreateSprite(
                "SettlementDishChapterSpotlight",
                bounds.center,
                WithAlpha(SettlementColorPalette.BaseScore, 0f),
                -8);
            if (_chapterSpotlight == null)
            {
                return;
            }

            Vector2 size = new(
                Mathf.Max(1.5f * _visualScale, bounds.size.x * 1.82f),
                Mathf.Max(1.5f * _visualScale, bounds.size.y * 1.82f));
            Vector3 settledScale = new(size.x, size.y, 1f);
            Vector3 revealScale = Vector3.Scale(settledScale, new Vector3(0.56f, 0.56f, 1f));
            Vector3 overshootScale = Vector3.Scale(settledScale, new Vector3(1.12f, 1.12f, 1f));
            SpriteRenderer renderer = _chapterSpotlight.GetComponent<SpriteRenderer>();
            GameObject spotlight = _chapterSpotlight;
            spotlight.transform.localScale = revealScale;

            _chapterSpotlightTween?.Kill();
            float animationDuration = Mathf.Clamp(revealDuration, 0.08f, 0.16f);
            _chapterSpotlightTween = DOVirtual.Float(0f, 1f, animationDuration, progress =>
                {
                    if (spotlight == null || renderer == null)
                    {
                        return;
                    }

                    float reveal = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / 0.68f));
                    float settle = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - 0.68f) / 0.32f));
                    spotlight.transform.localScale = progress < 0.68f
                        ? Vector3.LerpUnclamped(revealScale, overshootScale, reveal)
                        : Vector3.LerpUnclamped(overshootScale, settledScale, settle);
                    float alpha = progress < 0.68f
                        ? Mathf.Lerp(0f, 0.38f, reveal)
                        : Mathf.Lerp(0.38f, 0.24f, settle);
                    renderer.color = WithAlpha(SettlementColorPalette.BaseScore, alpha);
                })
                .SetEase(Ease.Linear)
                .SetId(spotlight)
                .SetLink(spotlight)
                .OnComplete(() => StartChapterSpotlightBreath(spotlight, renderer, settledScale));
        }

        private void StartChapterSpotlightBreath(
            GameObject spotlight,
            SpriteRenderer renderer,
            Vector3 settledScale)
        {
            if (spotlight == null || renderer == null || spotlight != _chapterSpotlight)
            {
                return;
            }

            _chapterSpotlightTween = DOVirtual.Float(0f, 1f, 0.42f, progress =>
                {
                    if (spotlight == null || renderer == null)
                    {
                        return;
                    }

                    spotlight.transform.localScale = Vector3.LerpUnclamped(
                        settledScale,
                        Vector3.Scale(settledScale, new Vector3(1.055f, 1.055f, 1f)),
                        progress);
                    renderer.color = WithAlpha(
                        SettlementColorPalette.BaseScore,
                        Mathf.Lerp(0.20f, 0.29f, progress));
                })
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetId(spotlight)
                .SetLink(spotlight);
        }

        private async Awaitable PlayChapterStartAccentSafelyAsync(
            DishPieceView view,
            float duration,
            CancellationToken cancellationToken)
        {
            try
            {
                await PlayImpactRingAsync(
                    view,
                    SettlementColorPalette.BaseScore,
                    SettlementImpactTier.Strong,
                    cancellationToken,
                    durationOverride: Mathf.Max(0.08f, duration));
            }
            catch (OperationCanceledException)
            {
                // 中断结算时，章节入场强调与舞台一起清理。
            }
        }

        private void SpawnSpotlight(Bounds bounds, Vector3 fallback, Color theme)
        {
            Vector3 center = bounds.size.sqrMagnitude > 0.0001f ? bounds.center : fallback;
            _groupSpotlight = CreateSprite("SettlementSpotlight", center, WithAlpha(theme, 0.20f), -6);
            if (_groupSpotlight == null)
            {
                return;
            }

            Vector2 size = bounds.size.sqrMagnitude > 0.0001f
                ? new Vector2(
                    Mathf.Max(1.2f * _visualScale, bounds.size.x * 1.45f),
                    Mathf.Max(1.2f * _visualScale, bounds.size.y * 1.45f))
                : Vector2.one * (1.8f * _visualScale);
            _groupSpotlight.transform.localScale = new Vector3(size.x, size.y, 1f);
        }

        private GameObject CreateSprite(string name, Vector3 worldPosition, Color color, int orderOffset)
        {
            if (_spritePrefab == null)
            {
                Debug.LogError($"{nameof(SettlementStageView)} 缺少舞台 Sprite prefab。", this);
                return null;
            }

            EnsurePools();
            SpriteRenderer renderer = _spritePool?.Get<SpriteRenderer>(_fxRoot);
            if (renderer == null)
            {
                return null;
            }

            GameObject root = renderer.gameObject;
            root.name = name;
            root.transform.position = worldPosition;
            renderer.sprite = BattleShadow.SoftShadowSprite;
            renderer.color = color;
            BattleSorting.Apply(renderer, BattleSorting.Fx, BattleSorting.OrderFloatingText + orderOffset);
            RegisterTransient(root, _spritePool);
            return root;
        }

        private void EnsurePools()
        {
            if (_spritePool == null && _spritePrefab != null)
            {
                _spritePool = new GameObjectPool(
                    _spritePrefab.gameObject,
                    transform,
                    SpritePoolPrewarm,
                    SpritePoolMaxInactive,
                    onGet: PrepareSpriteForReuse,
                    onRelease: ResetSpriteForPool);
            }

            if (_labelPool == null && _labelPrefab != null)
            {
                _labelPool = new GameObjectPool(
                    _labelPrefab.gameObject,
                    transform,
                    LabelPoolPrewarm,
                    LabelPoolMaxInactive,
                    onGet: go => go.GetComponent<SettlementStageLabelView>()?.PrepareForReuse(),
                    onRelease: go => go.GetComponent<SettlementStageLabelView>()?.ResetForPool());
            }

            if (_finaleLabelPool == null && _finaleLabelPrefab != null)
            {
                _finaleLabelPool = new GameObjectPool(
                    _finaleLabelPrefab.gameObject,
                    transform,
                    FinaleLabelPoolPrewarm,
                    FinaleLabelPoolMaxInactive,
                    onGet: go => go.GetComponent<SettlementStageLabelView>()?.PrepareForReuse(),
                    onRelease: go => go.GetComponent<SettlementStageLabelView>()?.ResetForPool());
            }
        }

        private void PrepareSpriteForReuse(GameObject root)
        {
            ResetSpriteVisual(root);
        }

        private void ResetSpriteForPool(GameObject root)
        {
            ResetSpriteVisual(root);
        }

        private void ResetSpriteVisual(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            root.transform.DOKill(false);
            SpriteRenderer renderer = root.GetComponent<SpriteRenderer>();
            if (renderer == null || _spritePrefab == null)
            {
                return;
            }

            renderer.DOKill(false);
            renderer.SetPropertyBlock(null);
            renderer.sprite = _spritePrefab.sprite;
            renderer.sharedMaterial = _spritePrefab.sharedMaterial;
            renderer.color = _spritePrefab.color;
            renderer.flipX = _spritePrefab.flipX;
            renderer.flipY = _spritePrefab.flipY;
            renderer.enabled = _spritePrefab.enabled;
        }

        private void RegisterTransient(GameObject root, GameObjectPool pool)
        {
            if (root == null || pool == null)
            {
                return;
            }

            using (TransientsMarker.Auto())
            {
                _transients.Add(root);
                int generation = ++_nextTransientGeneration;
                if (generation == 0)
                {
                    generation = ++_nextTransientGeneration;
                }

                _transientPools[root] = new TransientRegistration(pool, generation);
            }
        }

        private int TransientGeneration(GameObject root)
        {
            return root != null
                && _transientPools.TryGetValue(root, out TransientRegistration registration)
                    ? registration.Generation
                    : 0;
        }

        private void ReleaseTransient(GameObject root, int expectedGeneration = 0)
        {
            if (root == null
                || !_transientPools.TryGetValue(root, out TransientRegistration registration)
                || (expectedGeneration != 0 && registration.Generation != expectedGeneration))
            {
                return;
            }

            _transientPools.Remove(root);

            using (TransientsMarker.Auto())
            {
                DOTween.Kill(root, false);
                _transients.Remove(root);
                _heldLabels.Remove(root);
                registration.Pool.Release(root);
            }
        }

        private async Awaitable PlayImpactRingAsync(
            DishPieceView target,
            Color theme,
            SettlementImpactTier tier,
            CancellationToken cancellationToken,
            float durationOverride = -1f)
        {
            if (target == null || tier < SettlementImpactTier.Normal)
            {
                return;
            }

            Vector3 center = target.WorldBounds.center;
            float strength = Mathf.InverseLerp(
                (float)SettlementImpactTier.Normal,
                (float)SettlementImpactTier.Finale,
                (float)tier);
            Color initialColor = WithAlpha(theme, Mathf.Lerp(0.30f, 0.62f, strength));
            GameObject primary = CreateSprite("SettlementImpactRing", center, initialColor, -2);
            GameObject secondary = tier >= SettlementImpactTier.Chain
                ? CreateSprite("SettlementImpactRingEcho", center, WithAlpha(initialColor, initialColor.a * 0.68f), -3)
                : null;
            int primaryGeneration = TransientGeneration(primary);
            int secondaryGeneration = TransientGeneration(secondary);
            if (primary == null)
            {
                ReleaseTransient(secondary, secondaryGeneration);
                return;
            }

            SpriteRenderer primaryRenderer = primary.GetComponent<SpriteRenderer>();
            SpriteRenderer secondaryRenderer = secondary != null
                ? secondary.GetComponent<SpriteRenderer>()
                : null;
            float duration = durationOverride > 0f
                ? durationOverride
                : Mathf.Lerp(0.18f, 0.32f, strength);
            float endScale = Mathf.Lerp(1.45f, 2.65f, strength) * _visualScale;
            primary.transform.localScale = Vector3.one * (0.28f * _visualScale);
            if (secondary != null)
            {
                secondary.transform.localScale = Vector3.one * (0.20f * _visualScale);
            }

            Tween tween = DOVirtual.Float(0f, 1f, duration, progress =>
                {
                    if (primary != null && primaryRenderer != null)
                    {
                        float eased = Mathf.SmoothStep(0f, 1f, progress);
                        primary.transform.localScale = Vector3.one
                            * Mathf.Lerp(0.28f * _visualScale, endScale, eased);
                        primaryRenderer.color = WithAlpha(initialColor, initialColor.a * (1f - eased));
                    }

                    if (secondary != null && secondaryRenderer != null)
                    {
                        float echo = Mathf.Clamp01((progress - 0.18f) / 0.82f);
                        float easedEcho = Mathf.SmoothStep(0f, 1f, echo);
                        secondary.transform.localScale = Vector3.one
                            * Mathf.Lerp(0.20f * _visualScale, endScale * 1.18f, easedEcho);
                        secondaryRenderer.color = WithAlpha(
                            initialColor,
                            initialColor.a * 0.68f * (1f - easedEcho));
                    }
                })
                .SetEase(Ease.Linear)
                .SetId(primary)
                .SetLink(primary);
            try
            {
                await PresentationTween.AwaitCompletionAsync(tween, cancellationToken);
            }
            finally
            {
                ReleaseTransient(primary, primaryGeneration);
                ReleaseTransient(secondary, secondaryGeneration);
            }
        }

        private async Awaitable SpawnLabelAsync(
            Vector3 anchor,
            string header,
            string body,
            Color theme,
            float duration,
            CancellationToken cancellationToken,
            bool holdUntilCleared = false,
            bool finalStamp = false,
            Transform parentOverride = null,
            float visualScaleOverride = -1f,
            Color? headerSemanticColor = null,
            int sortingOrder = -1,
            float verticalDriftDirection = 1f,
            float impactScale = 1f)
        {
            SettlementStageLabelView prefab = finalStamp ? _finaleLabelPrefab : _labelPrefab;
            if (prefab == null)
            {
                Debug.LogError($"{nameof(SettlementStageView)} 缺少结算标签 prefab。", this);
                return;
            }

            EnsurePools();
            GameObjectPool pool = finalStamp ? _finaleLabelPool : _labelPool;
            SettlementStageLabelView label = pool?.Get<SettlementStageLabelView>(
                parentOverride != null ? parentOverride : _fxRoot);
            if (label == null)
            {
                return;
            }

            GameObject root = label.gameObject;
            root.name = finalStamp ? "SettlementFinaleLabel" : "SettlementStageLabel";
            root.transform.position = anchor;
            RegisterTransient(root, pool);
            int generation = TransientGeneration(root);
            label.Bind(header, body, theme, headerSemanticColor, sortingOrder);

            TextMeshPro headerText = label.HeaderText;
            TextMeshPro bodyText = label.BodyText;
            Color headerColor = headerText.color;
            Color bodyColor = bodyText.color;
            float visualScale = visualScaleOverride > 0f
                ? visualScaleOverride
                : _visualScale;
            visualScale *= Mathf.Max(0.01f, impactScale);
            Vector3 targetScale = new Vector3(
                visualScale,
                visualScale,
                1f);
            root.transform.localScale = Vector3.Scale(
                targetScale,
                new Vector3(0.80f, 0.80f, 1f));
            float animationDuration = Mathf.Max(0.0001f, duration);
            float driftDirection = Mathf.Approximately(verticalDriftDirection, 0f)
                ? 0f
                : verticalDriftDirection < 0f ? -1f : 1f;

            Tween tween = DOVirtual.Float(0f, 1f, animationDuration, t =>
                {
                    if (root == null)
                    {
                        return;
                    }

                    float enter = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.28f));
                    float pulse = Mathf.Sin(Mathf.Clamp01(t / 0.48f) * Mathf.PI) * 0.08f;
                    root.transform.localScale = Vector3.Scale(
                        targetScale,
                        new Vector3(0.80f + enter * 0.20f + pulse, 0.80f + enter * 0.20f + pulse, 1f));
                    root.transform.position = anchor
                        + Vector3.up * (
                            driftDirection * 0.10f * visualScale * enter);

                    float alpha = holdUntilCleared ? 1f : Mathf.Clamp01((1f - t) / 0.24f);
                    headerText.color = WithAlpha(headerColor, alpha);
                    bodyText.color = WithAlpha(bodyColor, alpha);
                })
                .SetEase(Ease.Linear)
                .SetId(root)
                .SetLink(root);

            bool held = false;
            try
            {
                await PresentationTween.AwaitCompletionAsync(tween, cancellationToken);
                if (holdUntilCleared && root != null && _transientPools.ContainsKey(root))
                {
                    _heldLabels.Add(root);
                    held = true;
                }
            }
            finally
            {
                if (!held)
                {
                    ReleaseTransient(root, generation);
                }
            }
        }

        private DishPieceView TryGetDish(int dishInstanceId)
        {
            return dishInstanceId > 0
                && _dishViews != null
                && _dishViews.TryGetValue(dishInstanceId, out DishPieceView view)
                    ? view
                    : null;
        }

        private void DestroyGroupSpotlight()
        {
            if (_groupSpotlight != null)
            {
                ReleaseTransient(_groupSpotlight);
                _groupSpotlight = null;
            }

            if (_heldLabels.Count > 0)
            {
                while (_heldLabels.Count > 0)
                {
                    int lastIndex = _heldLabels.Count - 1;
                    GameObject held = _heldLabels[lastIndex];
                    _heldLabels.RemoveAt(lastIndex);
                    if (held != null)
                    {
                        ReleaseTransient(held);
                    }
                }
            }
        }

        private void DestroyChapterSpotlight()
        {
            _chapterSpotlightTween?.Kill();
            _chapterSpotlightTween = null;
            if (_chapterSpotlight == null)
            {
                return;
            }

            ReleaseTransient(_chapterSpotlight);
            _chapterSpotlight = null;
        }

        private void ClearExpiredTransients()
        {
            _transients.RemoveAll(item => item == null);
        }

        private static Color ThemeFor(SkillExecutionTrace trace, ScoreSource source)
        {
            if (trace != null)
            {
                switch (trace.Kind)
                {
                    case SkillExecutionKind.SweetTransfer:
                        return SettlementColorPalette.SweetTransferSource;
                    case SkillExecutionKind.CopiedSkill:
                        return SettlementColorPalette.CopiedSkillSource;
                }
            }

            if (source != null && source.Type == ScoreSourceType.Relic)
            {
                return SettlementColorPalette.RelicSource;
            }

            return SettlementColorPalette.NativeSource;
        }

        private static SettlementDishFeedbackKind ActorFeedbackFor(SkillExecutionTrace trace)
        {
            return trace?.Kind switch
            {
                SkillExecutionKind.SweetTransfer => SettlementDishFeedbackKind.SweetTransferExecutor,
                SkillExecutionKind.CopiedSkill => SettlementDishFeedbackKind.CopiedSkillTriggered,
                _ => SettlementDishFeedbackKind.GenericSkillTriggered,
            };
        }

        private static string ReadableName(string preferred, string fallback)
        {
            return !string.IsNullOrWhiteSpace(preferred) ? preferred : fallback;
        }

        internal static SettlementDishFeedbackKind FeedbackFor(ScoreLine line)
        {
            if (line == null)
            {
                return SettlementDishFeedbackKind.GenericValueChanged;
            }

            bool active = line.Trace != null
                && line.Trace.RuntimeSelfDishInstanceId == line.DishInstanceId;
            switch (line.Kind)
            {
                case ScoreLineKind.DishFlat:
                    return active ? SettlementDishFeedbackKind.ActiveFlatBonus : SettlementDishFeedbackKind.PassiveFlatBonus;
                case ScoreLineKind.DishPermanentFlat:
                    return SettlementDishFeedbackKind.PermanentFlatBonus;
                case ScoreLineKind.DishMultiplier:
                    return active ? SettlementDishFeedbackKind.ActiveMultiplier : SettlementDishFeedbackKind.PassiveMultiplier;
                case ScoreLineKind.DishMultiplierAdd:
                    return active ? SettlementDishFeedbackKind.ActiveMultiplierAdd : SettlementDishFeedbackKind.PassiveMultiplierAdd;
                case ScoreLineKind.CopySkill:
                    return SettlementDishFeedbackKind.CopySkillTriggered;
                case ScoreLineKind.TriggerSweetTransfer:
                case ScoreLineKind.TriggeredSweetTransferSource:
                    return SettlementDishFeedbackKind.SweetTransferResult;
                case ScoreLineKind.SweetTransferFailed:
                    return SettlementDishFeedbackKind.SweetTransferFailed;
                case ScoreLineKind.SweetTransferBuffApplied:
                case ScoreLineKind.SweetTransferBuffTriggered:
                    return SettlementDishFeedbackKind.GenericSkillTriggered;
                case ScoreLineKind.CountAs:
                    // 主动发动已经在效果组开场由 Actor 播放一次；这里仅表现各目标份数变化，
                    // 避免蛋黄酥自身的结果行看起来像再次发动技能。
                    return SettlementDishFeedbackKind.GenericValueChanged;
                case ScoreLineKind.EmptyCountAs:
                    return SettlementDishFeedbackKind.CountAsChanged;
                case ScoreLineKind.TemporaryCategory:
                    return active
                        ? SettlementDishFeedbackKind.GenericSkillTriggered
                        : SettlementDishFeedbackKind.GenericValueChanged;
                default:
                    return SettlementDishFeedbackKind.GenericValueChanged;
            }
        }

        internal static Color ResultThemeFor(ScoreLine line)
        {
            if (line == null)
            {
                return SettlementColorPalette.Special;
            }

            if (line.Kind == ScoreLineKind.SweetTransferFailed)
            {
                return SettlementColorPalette.Failure;
            }

            if (line.Kind == ScoreLineKind.SweetTransferBuffApplied
                || line.Kind == ScoreLineKind.SweetTransferBuffTriggered)
            {
                return SettlementColorPalette.SweetTransfer;
            }

            return line.Kind == ScoreLineKind.TriggerSweetTransfer
                || line.Kind == ScoreLineKind.TriggeredSweetTransferSource
                ? SettlementColorPalette.SweetTransfer
                : SettlementColorPalette.For(line.Kind);
        }

        internal static Color ResultHeaderSemanticColorFor(ScoreLine line, Color fallback)
        {
            if (line == null)
            {
                return fallback;
            }

            return line.Kind switch
            {
                ScoreLineKind.DishFlat
                    or ScoreLineKind.DishPermanentFlat
                    or ScoreLineKind.FinalFlat => new Color32(40, 102, 156, 255),
                ScoreLineKind.DishMultiplier
                    or ScoreLineKind.DishMultiplierAdd
                    or ScoreLineKind.FinalMultiplier => new Color32(178, 58, 72, 255),
                ScoreLineKind.Gold => new Color32(154, 101, 0, 255),
                ScoreLineKind.ExtraSettlement => new Color32(118, 86, 168, 255),
                ScoreLineKind.SweetTransferBuffTriggered => line.Trace?.ActionType
                    is SkillActionType.AddFlat
                    or SkillActionType.PermanentAddFlat
                        ? new Color32(40, 102, 156, 255)
                        : fallback,
                _ => fallback,
            };
        }

        internal static bool ShouldShowResultLabel(ScoreLine line)
        {
            // 银材质行只是尚未掷骰的判定请求；命中后的具体消耗品由 Game 层发放后另行逐条展示。
            // TriggeredSweetTransferSource 只驱动多来源甜蜜传递的逐个交接演出，不是玩家结果。
            return line != null
                && line.Kind != ScoreLineKind.TriggeredSweetTransferSource;
        }

        internal static string ResultHeader(ScoreLine line)
        {
            if (line == null)
            {
                return "结算结果";
            }

            return line.Kind switch
            {
                ScoreLineKind.DishFlat => "分数",
                ScoreLineKind.DishPermanentFlat => "分数",
                ScoreLineKind.DishMultiplier => "倍率",
                ScoreLineKind.DishMultiplierAdd => "倍率",
                ScoreLineKind.FinalFlat => "总分加成",
                ScoreLineKind.FinalMultiplier => "总分倍率",
                ScoreLineKind.Gold => "金币",
                ScoreLineKind.Layer => "蛋糕层数",
                ScoreLineKind.CopySkill => "技能复制",
                ScoreLineKind.TriggerSweetTransfer => "甜蜜传递",
                ScoreLineKind.TriggeredSweetTransferSource => string.Empty,
                ScoreLineKind.SweetTransferBuffApplied => string.Empty,
                ScoreLineKind.SweetTransferBuffTriggered => SweetTransferBuffTriggeredHeader(line),
                ScoreLineKind.SweetTransferFailed => "甜蜜传递",
                ScoreLineKind.CountAs => "份数",
                ScoreLineKind.EmptyCountAs => "份数",
                ScoreLineKind.TemporaryCategory => "赋予",
                ScoreLineKind.ExtraSettlement => "咸味",
                _ => "结算结果",
            };
        }

        internal static string CakeLayerBurstResultHeader(
            ScoreLine line,
            CakeLayerBurstStepProfile profile)
        {
            string effect = line?.Kind switch
            {
                ScoreLineKind.DishFlat => "加分",
                ScoreLineKind.DishMultiplierAdd => "倍率 +",
                ScoreLineKind.DishMultiplier => "倍率 ×",
                _ => ResultHeader(line),
            };
            return $"{profile.StepNumber}/{profile.StepCount} {effect}";
        }

        internal static string ResultText(ScoreLine line, BigDouble dishContribution, BigDouble runningTotal)
        {
            if (line == null)
            {
                return $"总分  {ScoreNumberFormatter.Format(runningTotal)}";
            }

            if (BigDouble.Abs(line.Value) <= 0.001f
                && line.Kind != ScoreLineKind.FinalMultiplier
                && line.Kind != ScoreLineKind.SweetTransferFailed
                && line.Kind != ScoreLineKind.TriggerSweetTransfer)
            {
                return "无变化";
            }

            string signed = $"{(line.Value >= 0f ? "+" : string.Empty)}{FormatLineValue(line.Value)}";
            string total = ScoreNumberFormatter.Format(runningTotal);
            switch (line.Kind)
            {
                case ScoreLineKind.DishFlat:
                    return Score(signed);
                case ScoreLineKind.DishPermanentFlat:
                    return PermanentScore($"永久{signed}");
                case ScoreLineKind.DishMultiplier:
                    return MultiplierMultiply($"×{FormatLineValue(line.Value)}");
                case ScoreLineKind.DishMultiplierAdd:
                    return MultiplierAdd(signed);
                case ScoreLineKind.FinalFlat:
                    return $"总分 {Score(signed)}  →  {total}";
                case ScoreLineKind.FinalMultiplier:
                    return $"总分 {MultiplierMultiply($"×{FormatLineValue(line.Value)}")}  →  {total}";
                case ScoreLineKind.Gold:
                    return Gold(signed);
                case ScoreLineKind.Layer:
                    return signed;
                case ScoreLineKind.CopySkill:
                    return $"获得技能 ×{Count(line.Value)}";
                case ScoreLineKind.TriggerSweetTransfer:
                    return line.Value > 1f
                        ? $"触发甜蜜传递 ×{Count(line.Value)}"
                        : "触发甜蜜传递";
                case ScoreLineKind.TriggeredSweetTransferSource:
                    return string.Empty;
                case ScoreLineKind.SweetTransferBuffApplied:
                    return SweetTransferBuffName(line);
                case ScoreLineKind.SweetTransferBuffTriggered:
                    return SweetTransferBuffTriggeredText(line, signed);
                case ScoreLineKind.SweetTransferFailed:
                    return "没有可传递目标";
                case ScoreLineKind.CountAs:
                    return signed;
                case ScoreLineKind.EmptyCountAs:
                    return signed;
                case ScoreLineKind.TemporaryCategory:
                    return TemporaryCategoryText(line.Message);
                case ScoreLineKind.ExtraSettlement:
                    return Benefit("额外结算");
                default:
                    return string.IsNullOrEmpty(line.Message) ? signed : line.Message;
            }
        }

        internal static string SweetTransferBuffName(ScoreLine line)
        {
            string ownerName = line?.Trace?.OwnerDishName;
            return string.IsNullOrWhiteSpace(ownerName)
                ? "甜蜜Buff"
                : $"{ownerName}Buff";
        }

        internal static string SweetTransferBuffTriggeredHeader(ScoreLine line)
        {
            if (line?.Trace?.ActionType == SkillActionType.TriggerSweetTransfer)
            {
                return SweetTransferBuffName(line);
            }

            if (IsLaunchResultLabel(line))
            {
                return "甜蜜传递";
            }

            return line?.Trace?.ActionType switch
            {
                SkillActionType.AddFlat or SkillActionType.PermanentAddFlat => "分数",
                SkillActionType.AddMult
                    or SkillActionType.AddMultFlat => "倍率",
                _ => SweetTransferBuffName(line),
            };
        }

        internal static string SweetTransferBuffTriggeredText(ScoreLine line, string signed)
        {
            if (line?.Trace?.ActionType == SkillActionType.TriggerSweetTransfer)
            {
                return $"额外目标 +{Count(line.Value)}";
            }

            return line?.Trace?.ActionType switch
            {
                SkillActionType.AddFlat or SkillActionType.PermanentAddFlat => Score(signed),
                SkillActionType.AddMultFlat => MultiplierAdd(signed),
                SkillActionType.AddMult =>
                    MultiplierMultiply($"×{FormatLineValue(line.Value)}"),
                _ => MultiplierMultiply($"×{FormatLineValue(line.Value)}"),
            };
        }

        private static string TemporaryCategoryText(string message)
        {
            string text = string.IsNullOrWhiteSpace(message) ? "视为蛋糕" : message.Trim();
            return text.StartsWith("本场", StringComparison.Ordinal)
                ? text.Substring(2).TrimStart()
                : text;
        }

        /// <summary>
        /// 上菜即时提示保留 Gameplay 层的纯文本，由表现层依据结构化效果类型添加语义标记。
        /// </summary>
        internal static string SemanticServeTriggerText(ServeTriggerCue cue)
        {
            if (cue == null)
            {
                return string.Empty;
            }

            string text = cue.Text ?? string.Empty;
            switch (cue.EffectKind)
            {
                case ServeCueEffectKind.MultiplierFlat:
                    return StrongKeyword(
                        WrapFirst(text, FormatSignedCueValue(cue.Value), "multadd"),
                        "倍率");
                case ServeCueEffectKind.MultiplierFactor:
                    return StrongKeyword(
                        WrapFirst(text, $"×{FormatCueValue(cue.Value)}", "multmul"),
                        "倍率");
                case ServeCueEffectKind.BaseScoreFactor:
                    return WrapFirst(text, $"×{FormatCueValue(cue.Value)}", "multmul");
                case ServeCueEffectKind.GoldDelta:
                    return WrapFirst(text, $"金币 {FormatSignedCueValue(cue.Value)}", "gold");
                default:
                    return text;
            }
        }

        private static string WrapFirst(string source, string token, string tag)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(token))
            {
                return source ?? string.Empty;
            }

            int index = source.IndexOf(token, StringComparison.Ordinal);
            if (index < 0)
            {
                return source;
            }

            return source.Substring(0, index)
                + $"[{tag}]"
                + token
                + $"[/{tag}]"
                + source.Substring(index + token.Length);
        }

        private static string StrongKeyword(string source, string keyword)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(keyword))
            {
                return source ?? string.Empty;
            }

            return source.Replace(keyword, Strong(keyword));
        }

        private static string Strong(string value) => $"[strong]{value}[/strong]";

        private static string Score(string value) => $"[score]{value}[/score]";

        private static string PermanentScore(string value) => $"[scoreperm]{value}[/scoreperm]";

        private static string MultiplierAdd(string value) => $"[multadd]{value}[/multadd]";

        private static string MultiplierMultiply(string value) => $"[multmul]{value}[/multmul]";

        private static string Gold(string value) => $"[gold]{value}[/gold]";

        private static string Term(string value) => $"[term]{value}[/term]";

        private static string Benefit(string value) => $"[benefit]{value}[/benefit]";

        private static string FormatSignedCueValue(float value)
            => value >= 0f ? $"+{FormatCueValue(value)}" : FormatCueValue(value);

        private static string FormatCueValue(float value)
            => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        private static string FormatLineValue(BigDouble value)
        {
            return BigDouble.Abs(value) < ScoreNumberFormatter.ScientificThreshold
                ? value.ToString("G3")
                : ScoreNumberFormatter.Format(value);
        }

        private static int Count(BigDouble value)
        {
            return (int)Math.Round(value.ToDouble(), MidpointRounding.AwayFromZero);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a *= Mathf.Clamp01(alpha);
            return color;
        }

        private static async Awaitable PlayFeedbackSafelyAsync(
            DishPieceView view,
            SettlementDishFeedbackKind kind,
            CancellationToken cancellationToken,
            float durationScale)
        {
            if (view == null)
            {
                return;
            }

            try
            {
                await view.PlaySettlementFeedbackAsync(kind, cancellationToken, durationScale);
            }
            catch (OperationCanceledException)
            {
                // 结算被打断时，已经启动的并行动画正常退出。
            }
        }

        private void OnDisable()
        {
            ClearImmediate();
        }

        private void OnDestroy()
        {
            ClearImmediate();
            _spritePool?.Clear();
            _labelPool?.Clear();
            _finaleLabelPool?.Clear();
        }
    }
}
