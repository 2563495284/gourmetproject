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
        private const float DimmedDishBrightness = 0.72f;
        private const float ScopeDishBrightness = 0.86f;
        private const float SweetTransferSourceBrightness = 0.82f;

        private readonly List<GameObject> _transients = new();
        private IReadOnlyDictionary<int, DishPieceView> _dishViews;
        private DiningTableCoordinateMapper _mapper;
        private Transform _fxRoot;
        private float _visualScale = 1f;

        [Header("固定资源（prefab 绑定）")]
        [SerializeField] private SettlementStageLabelView _labelPrefab;
        [SerializeField] private SettlementStageLabelView _finaleLabelPrefab;
        [SerializeField] private SpriteRenderer _spritePrefab;

        private GameObject _groupSpotlight;
        private GameObject _groupLabel;
        private SettlementEffectGroup _resultHitSoundGroup;

        public void Configure(
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            DiningTableCoordinateMapper mapper,
            Transform fxRoot,
            float visualScale = 1f)
        {
            ClearImmediate();
            _dishViews = dishViews;
            _mapper = mapper;
            _fxRoot = fxRoot != null ? fxRoot : transform;
            _visualScale = Mathf.Max(0.0001f, visualScale);
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

        internal async Awaitable FocusSourceAsync(
            SettlementEffectGroup group,
            float duration,
            CancellationToken cancellationToken,
            bool actorAlreadyIntroduced = false)
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
                TryGetDish(group.Trace.OwnerDishInstanceId)?.SetSettlementFocus(SweetTransferSourceBrightness);
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
                "技能触发",
                group.Trace?.Kind == SkillExecutionKind.SweetTransfer
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
        internal async Awaitable PlaySweetTransferHandoffsAsync(
            IReadOnlyList<SweetTransferHandoffVisual> handoffs,
            SweetTransferParticleView particlePrefab,
            float travelDuration,
            float executorDuration,
            CancellationToken cancellationToken)
        {
            EndGroupImmediate();
            DimAllDishes();
            if (handoffs == null || handoffs.Count == 0)
            {
                return;
            }

            var travelTasks = new List<Awaitable>(handoffs.Count);
            float executorScale = Mathf.Max(0.05f, executorDuration / 0.34f);
            for (int i = 0; i < handoffs.Count; i++)
            {
                SweetTransferHandoffVisual handoff = handoffs[i];
                DishPieceView source = handoff.Source;
                DishPieceView executor = handoff.Executor;
                source?.SetSettlementFocus(1f);
                executor?.SetSettlementFocus(1f);
                if (executor != null)
                {
                    executor.BeginSweetTransferExecutorFeedback();
                    _ = PlayFeedbackSafelyAsync(
                        executor,
                        SettlementDishFeedbackKind.SweetTransferExecutor,
                        cancellationToken,
                        durationScale: executorScale);
                }

                if (source == null
                    || executor == null
                    || handoff.Context.IsSelfTransfer)
                {
                    source?.SetSettlementFocus(SweetTransferSourceBrightness);
                    continue;
                }

                travelTasks.Add(SweetTransferParticleView.PlayAsync(
                    particlePrefab,
                    _fxRoot,
                    source.WorldBounds.center,
                    executor.WorldBounds.center,
                    travelDuration,
                    cancellationToken,
                    visualScale: _visualScale));
                source.SetSettlementFocus(SweetTransferSourceBrightness);
            }

            for (int i = 0; i < travelTasks.Count; i++)
            {
                await travelTasks[i];
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

                float brightness = DimmedDishBrightness;
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
                    brightness = ScopeDishBrightness;
                }

                view.SetSettlementFocus(brightness);
            }
        }

        internal async Awaitable PlaySweetTransferBuffTriggerAsync(
            DishPieceView transferSource,
            DishPieceView buffOwner,
            SweetTransferParticleView particlePrefab,
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
                _visualScale);
            await PlayFeedbackSafelyAsync(
                buffOwner,
                SettlementDishFeedbackKind.GenericSkillTriggered,
                cancellationToken,
                durationScale: Mathf.Max(0.05f, duration / 0.34f));
        }

        internal async Awaitable PlaySweetTransferFailureAsync(
            DishPieceView source,
            SweetTransferParticleView particlePrefab,
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
                _visualScale);
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
                target.SetSettlementFocus(ScopeDishBrightness);
                target.PlayScopeAffectedShake(Mathf.Max(0.05f, duration / 0.40f));
            }

            await Awaitable.WaitForSecondsAsync(Mathf.Max(0.0001f, duration), cancellationToken);
        }

        internal static Vector3 ResultLabelScatterOffset(float visualScale, float xUnit, float yUnit)
        {
            float scale = Mathf.Max(0.0001f, visualScale);
            return new Vector3(
                Mathf.Clamp(xUnit, -1f, 1f) * 0.12f * scale,
                Mathf.Clamp01(yUnit) * 0.18f * scale,
                0f);
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
            CancellationToken cancellationToken)
        {
            if (!ShouldShowResultLabel(line))
            {
                return;
            }

            Color theme = ResultThemeFor(line);
            if (target != null)
            {
                PlayResultHitSoundIfNeeded(group, audioPitch);
                target.SetSettlementFocus(1f);
                if (playTargetFeedback)
                {
                    _ = PlayFeedbackSafelyAsync(
                        target,
                        FeedbackFor(line),
                        cancellationToken,
                        durationScale: Mathf.Max(0.05f, duration / 0.80f));
                }
            }

            Vector3 anchor = target != null
                ? target.DishValueBadgeWorldPosition
                    + Vector3.down * (0.42f * _visualScale)
                : _mapper.Center + Vector3.up * (0.20f * _visualScale);
            anchor += ResultLabelScatterOffset(
                _visualScale,
                UnityEngine.Random.Range(-1f, 1f),
                UnityEngine.Random.Range(0f, 1f));
            Awaitable impactTask = playTargetFeedback
                ? PlayImpactRingAsync(target, theme, impactTier, cancellationToken)
                : default;
            await SpawnLabelAsync(
                anchor,
                ResultHeader(line),
                ResultText(line, dishContribution, runningTotal),
                theme,
                duration,
                cancellationToken,
                headerSemanticColor: ResultHeaderSemanticColorFor(line, theme),
                sortingOrder: WorldLabelSorting.NextOrder());
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
            DimAllDishes();
            DestroyGroupSpotlight();
            await Awaitable.WaitForSecondsAsync(Mathf.Max(0.0001f, duration), cancellationToken);
            ClearExpiredTransients();
        }

        public async Awaitable PlayFinaleAsync(
            BigDouble total,
            float duration,
            CancellationToken cancellationToken)
        {
            EndGroupImmediate();
            GameApp.Audio.PlaySettlementHit(2, 1.18f);
            if (_dishViews != null)
            {
                foreach (DishPieceView view in _dishViews.Values)
                {
                    if (view == null)
                    {
                        continue;
                    }

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
                .SetLink(ring);

            await PresentationTween.AwaitCompletionAsync(ringTween, cancellationToken);
            await labelTask;
        }

        public void ClearImmediate()
        {
            ClearFocus();
            DestroyGroupSpotlight();
            for (int i = 0; i < _transients.Count; i++)
            {
                GameObject transient = _transients[i];
                if (transient != null)
                {
                    Destroy(transient);
                }
            }

            _transients.Clear();
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

                float brightness = entry.Key == actorDishInstanceId
                    ? 1f
                    : targetDishIds != null && targetDishIds.Contains(entry.Key)
                        ? ScopeDishBrightness
                        : DimmedDishBrightness;
                view.SetSettlementFocus(brightness);
            }
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

        private void DimAllDishes()
        {
            if (_dishViews == null)
            {
                return;
            }

            foreach (DishPieceView view in _dishViews.Values)
            {
                view?.SetSettlementFocus(DimmedDishBrightness);
            }
        }

        private void EndGroupImmediate()
        {
            DimAllDishes();
            DestroyGroupSpotlight();
            ClearExpiredTransients();
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

            SpriteRenderer renderer = Instantiate(_spritePrefab, _fxRoot);
            GameObject root = renderer.gameObject;
            root.name = name;
            root.transform.position = worldPosition;
            renderer.sprite = BattleShadow.SoftShadowSprite;
            renderer.color = color;
            BattleSorting.Apply(renderer, BattleSorting.Fx, BattleSorting.OrderFloatingText + orderOffset);
            _transients.Add(root);
            return root;
        }

        private async Awaitable PlayImpactRingAsync(
            DishPieceView target,
            Color theme,
            SettlementImpactTier tier,
            CancellationToken cancellationToken)
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
            if (primary == null)
            {
                return;
            }

            SpriteRenderer primaryRenderer = primary.GetComponent<SpriteRenderer>();
            SpriteRenderer secondaryRenderer = secondary != null
                ? secondary.GetComponent<SpriteRenderer>()
                : null;
            float duration = Mathf.Lerp(0.18f, 0.32f, strength);
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
                .SetLink(primary);

            await PresentationTween.AwaitCompletionAsync(tween, cancellationToken);
            if (primary != null)
            {
                Destroy(primary);
            }

            if (secondary != null)
            {
                Destroy(secondary);
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
            int sortingOrder = -1)
        {
            SettlementStageLabelView prefab = finalStamp ? _finaleLabelPrefab : _labelPrefab;
            if (prefab == null)
            {
                Debug.LogError($"{nameof(SettlementStageView)} 缺少结算标签 prefab。", this);
                return;
            }

            SettlementStageLabelView label = Instantiate(
                prefab,
                parentOverride != null ? parentOverride : _fxRoot);
            GameObject root = label.gameObject;
            root.name = finalStamp ? "SettlementFinaleLabel" : "SettlementStageLabel";
            root.transform.position = anchor;
            _transients.Add(root);
            label.Bind(header, body, theme, headerSemanticColor, sortingOrder);

            TextMeshPro headerText = label.HeaderText;
            TextMeshPro bodyText = label.BodyText;
            Color headerColor = headerText.color;
            Color bodyColor = bodyText.color;
            float visualScale = visualScaleOverride > 0f
                ? visualScaleOverride
                : _visualScale;
            Vector3 targetScale = new Vector3(
                visualScale,
                visualScale,
                1f);
            root.transform.localScale = Vector3.Scale(
                targetScale,
                new Vector3(0.80f, 0.80f, 1f));
            float animationDuration = Mathf.Max(0.0001f, duration);

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
                        + Vector3.up * (0.10f * visualScale * enter);

                    float alpha = holdUntilCleared ? 1f : Mathf.Clamp01((1f - t) / 0.24f);
                    headerText.color = WithAlpha(headerColor, alpha);
                    bodyText.color = WithAlpha(bodyColor, alpha);
                })
                .SetEase(Ease.Linear)
                .SetLink(root);

            await PresentationTween.AwaitCompletionAsync(tween, cancellationToken);
            if (holdUntilCleared && root != null)
            {
                _groupLabel = root;
            }

            if (!holdUntilCleared && root != null)
            {
                Destroy(root);
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
                Destroy(_groupSpotlight);
                _groupSpotlight = null;
            }

            if (_groupLabel != null)
            {
                Destroy(_groupLabel);
                _groupLabel = null;
            }
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
                ScoreLineKind.SweetTransferBuffTriggered => SweetTransferBuffName(line),
                ScoreLineKind.SweetTransferFailed => "甜蜜传递",
                ScoreLineKind.CountAs => "份数",
                ScoreLineKind.EmptyCountAs => "份数",
                ScoreLineKind.TemporaryCategory => "赋予",
                ScoreLineKind.ExtraSettlement => "咸味",
                _ => "结算结果",
            };
        }

        internal static string ResultText(ScoreLine line, BigDouble dishContribution, BigDouble runningTotal)
        {
            if (line == null)
            {
                return $"总分  {ScoreNumberFormatter.Format(runningTotal)}";
            }

            if (BigDouble.Abs(line.Value) <= 0.001f
                && line.Kind != ScoreLineKind.FinalMultiplier
                && line.Kind != ScoreLineKind.SweetTransferFailed)
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
                        ? $"发动{Term("甜蜜传递")} ×{Count(line.Value)}"
                        : $"发动{Term("甜蜜传递")}";
                case ScoreLineKind.TriggeredSweetTransferSource:
                    return string.Empty;
                case ScoreLineKind.SweetTransferBuffApplied:
                    return SweetTransferBuffName(line);
                case ScoreLineKind.SweetTransferBuffTriggered:
                    return line.Trace?.ActionType == SkillActionType.TriggerSweetTransfer
                        ? $"额外目标 +{Count(line.Value)}"
                        : $"本行{Strong("倍率")} {MultiplierMultiply($"×{FormatLineValue(line.Value)}")}";
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
        }
    }
}
