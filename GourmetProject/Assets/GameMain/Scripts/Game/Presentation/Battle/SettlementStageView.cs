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

            Vector3 anchor = view != null
                ? view.DishValueBadgeWorldPosition
                    + Vector3.down * (0.42f * _visualScale)
                : _mapper.Center;
            await SpawnLabelAsync(
                anchor,
                string.IsNullOrEmpty(dishName) ? "基础美味" : dishName,
                $"基础贡献  {ScoreNumberFormatter.Format(contribution)}",
                SettlementColorPalette.WithAlpha(SettlementColorPalette.BaseScore, 0.96f),
                duration,
                cancellationToken);
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

        internal async Awaitable PlaySweetTransferHandoffAsync(
            SettlementSweetTransferPresentationContext context,
            DishPieceView source,
            DishPieceView executor,
            SweetTransferParticleView particlePrefab,
            float sourceDuration,
            float travelDuration,
            float executorDuration,
            CancellationToken cancellationToken)
        {
            EndGroupImmediate();
            DimAllDishes();
            Color theme = SettlementColorPalette.SweetTransferSource;
            string sourceName = ReadableName(context.SourceName, "技能来源");
            string executorName = ReadableName(context.ExecutorName, "接收者");
            string skillName = ReadableName(context.SkillName, "甜蜜传递技能");

            source?.SetSettlementFocus(1f);
            if (source == null || executor == null)
            {
                executor?.SetSettlementFocus(1f);
                if (executor != null)
                {
                    executor.BeginSweetTransferExecutorFeedback();
                    _ = PlayFeedbackSafelyAsync(
                        executor,
                        SettlementDishFeedbackKind.SweetTransferExecutor,
                        cancellationToken,
                        durationScale: Mathf.Max(0.05f, executorDuration / 0.34f));
                }

                await SpawnLabelAsync(
                    _mapper.Center + Vector3.up * (0.45f * _visualScale),
                    "甜蜜传递",
                    $"{sourceName} 的技能由 {executorName} 执行",
                    theme,
                    Mathf.Max(0.0001f, sourceDuration + travelDuration + executorDuration),
                    cancellationToken);
                return;
            }

            Vector3 sourceAnchor = source.WorldBounds.center
                + Vector3.up * (source.WorldBounds.extents.y + 0.42f * _visualScale);
            await SpawnLabelAsync(
                sourceAnchor,
                "技能来源",
                sourceName,
                theme,
                sourceDuration,
                cancellationToken);

            if (!context.IsSelfTransfer)
            {
                await SweetTransferParticleView.PlayAsync(
                    particlePrefab,
                    _fxRoot,
                    source.WorldBounds.center,
                    executor.WorldBounds.center,
                    travelDuration,
                    cancellationToken,
                    visualScale: _visualScale);
                source.SetSettlementFocus(SweetTransferSourceBrightness);
            }

            executor.SetSettlementFocus(1f);
            executor.BeginSweetTransferExecutorFeedback();
            _ = PlayFeedbackSafelyAsync(
                executor,
                SettlementDishFeedbackKind.SweetTransferExecutor,
                cancellationToken,
                durationScale: Mathf.Max(0.05f, executorDuration / 0.34f));

            Vector3 executorAnchor = executor.WorldBounds.center
                + Vector3.up * (executor.WorldBounds.extents.y + 0.42f * _visualScale);
            await SpawnLabelAsync(
                executorAnchor,
                context.IsSelfTransfer ? "自身执行" : "接收并执行",
                $"{executorName} · {skillName}",
                theme,
                executorDuration,
                cancellationToken);
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
            var shakenTargets = new List<DishPieceView>();
            if (group != null && _dishViews != null)
            {
                foreach (int id in group.TargetDishIds)
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

        internal async Awaitable ShowResultAsync(
            SettlementEffectGroup group,
            ScoreLine line,
            DishPieceView target,
            BigDouble dishContribution,
            BigDouble runningTotal,
            float duration,
            int stackIndex,
            int stackCount,
            SettlementImpactTier impactTier,
            float audioPitch,
            bool playTargetFeedback,
            CancellationToken cancellationToken)
        {
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
            if (stackCount > 1)
            {
                float centeredIndex = stackIndex - (stackCount - 1) * 0.5f;
                anchor += Vector3.up
                    * (centeredIndex * 0.54f * _visualScale);
            }
            Awaitable impactTask = playTargetFeedback
                ? PlayImpactRingAsync(target, theme, impactTier, cancellationToken)
                : default;
            await SpawnLabelAsync(
                anchor,
                ResultHeader(line),
                ResultText(line, dishContribution, runningTotal),
                theme,
                duration,
                cancellationToken);
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
            float visualScaleOverride = -1f)
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
            label.Bind(header, body, theme);

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

        private static string ResultHeader(ScoreLine line)
        {
            if (line == null)
            {
                return "结算结果";
            }

            return line.Kind switch
            {
                ScoreLineKind.DishFlat => "基础分",
                ScoreLineKind.DishPermanentFlat => "永久分数",
                ScoreLineKind.DishMultiplier => "乘倍率",
                ScoreLineKind.DishMultiplierAdd => "加倍率",
                ScoreLineKind.FinalFlat => "总分加成",
                ScoreLineKind.FinalMultiplier => "总分倍率",
                ScoreLineKind.Gold => "金币",
                ScoreLineKind.Layer => "快乐蛋糕",
                ScoreLineKind.SilverItemRoll => "银材质奖励",
                ScoreLineKind.CopySkill => "技能复制",
                ScoreLineKind.TriggerSweetTransfer => "甜蜜传递",
                ScoreLineKind.TriggeredSweetTransferSource => "传递来源",
                ScoreLineKind.SweetTransferBuffApplied => "甜蜜 Buff",
                ScoreLineKind.SweetTransferBuffTriggered => "Buff 响应",
                ScoreLineKind.SweetTransferFailed => "甜蜜传递",
                ScoreLineKind.CountAs => "份数",
                ScoreLineKind.TemporaryCategory => "临时分类",
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
                    return $"永久 {Score(signed)}";
                case ScoreLineKind.DishMultiplier:
                    return MultiplierMultiply($"×{FormatLineValue(line.Value)}");
                case ScoreLineKind.DishMultiplierAdd:
                    return $"{Strong("倍率")} {MultiplierAdd(signed)}";
                case ScoreLineKind.FinalFlat:
                    return $"总分 {Score(signed)}  →  {total}";
                case ScoreLineKind.FinalMultiplier:
                    return $"总分 {MultiplierMultiply($"×{FormatLineValue(line.Value)}")}  →  {total}";
                case ScoreLineKind.Gold:
                    return Gold($"金币 {signed}");
                case ScoreLineKind.Layer:
                    return $"层数 {signed}";
                case ScoreLineKind.SilverItemRoll:
                    return $"获得{Term("装饰品")}和{Term("消耗品")} ×{Count(line.Value)}";
                case ScoreLineKind.CopySkill:
                    return $"获得技能 ×{Count(line.Value)}";
                case ScoreLineKind.TriggerSweetTransfer:
                    return line.Value > 1f
                        ? $"触发{Term("甜蜜传递")} ×{Count(line.Value)}"
                        : $"触发{Term("甜蜜传递")}";
                case ScoreLineKind.TriggeredSweetTransferSource:
                    return "来源已接力";
                case ScoreLineKind.SweetTransferBuffApplied:
                    return $"挂载目标 ×{Count(line.Value)}";
                case ScoreLineKind.SweetTransferBuffTriggered:
                    return line.Trace?.ActionType == SkillActionType.TriggerSweetTransfer
                        ? $"额外目标 +{Count(line.Value)}"
                        : $"本行{Strong("倍率")} {MultiplierMultiply($"×{FormatLineValue(line.Value)}")}";
                case ScoreLineKind.SweetTransferFailed:
                    return "没有可传递目标";
                case ScoreLineKind.ExtraSettlement:
                    return $"{Benefit("额外结算")} {Score(signed)}";
                case ScoreLineKind.CountAs:
                    return $"{Strong("份数")} {signed}  →  {FormatLineValue(line.After)}";
                case ScoreLineKind.TemporaryCategory:
                    return string.IsNullOrEmpty(line.Message) ? "临时分类生效" : line.Message;
                default:
                    return string.IsNullOrEmpty(line.Message) ? signed : line.Message;
            }
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
