using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DG.Tweening;
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
        private GameObject _groupSpotlight;
        private GameObject _groupLabel;
        private SettlementEffectGroup _resultHitSoundGroup;

        public void Configure(
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            DiningTableCoordinateMapper mapper,
            Transform fxRoot)
        {
            ClearImmediate();
            _dishViews = dishViews;
            _mapper = mapper;
            _fxRoot = fxRoot != null ? fxRoot : transform;
        }

        internal void PlayTransientEffect(
            Transform parent,
            Vector3 anchor,
            string sourceName,
            string effectText,
            Color theme,
            float duration,
            float delay,
            CancellationToken cancellationToken)
        {
            _ = PlayTransientEffectAsync(
                parent,
                anchor,
                sourceName,
                effectText,
                theme,
                duration,
                delay,
                cancellationToken);
        }

        private async Awaitable PlayTransientEffectAsync(
            Transform parent,
            Vector3 anchor,
            string sourceName,
            string effectText,
            Color theme,
            float duration,
            float delay,
            CancellationToken cancellationToken)
        {
            try
            {
                if (delay > 0f)
                {
                    await Awaitable.WaitForSecondsAsync(delay, cancellationToken);
                }

                await SpawnLabelAsync(
                    anchor,
                    ReadableName(sourceName, "效果触发"),
                    effectText,
                    theme,
                    duration,
                    cancellationToken,
                    parentOverride: parent);
            }
            catch (OperationCanceledException)
            {
                // 页面关闭或新一轮演出开始时，尚未完成的即时提示直接结束。
            }
        }

        public async Awaitable PlayBaseAsync(
            DishPieceView view,
            string dishName,
            float contribution,
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
                ? view.DishValueBadgeWorldPosition + Vector3.down * 0.42f
                : _mapper.Center;
            await SpawnLabelAsync(
                anchor,
                string.IsNullOrEmpty(dishName) ? "基础美味" : dishName,
                $"基础贡献  {contribution:0}",
                SettlementAttributePalette.WithAlpha(SettlementAttributePalette.BaseScore, 0.96f),
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
                ? actor.WorldBounds.center + Vector3.up * (actor.WorldBounds.extents.y + 0.42f)
                : _mapper.Center + Vector3.up * 0.72f;
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
                group.Trace?.Kind == SkillExecutionKind.SweetTransfer ? "执行技能" : "效果触发",
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
            Color theme = new Color(1f, 0.30f, 0.68f, 1f);
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
                    _mapper.Center + Vector3.up * 0.45f,
                    "甜蜜传递",
                    $"{sourceName} 的技能由 {executorName} 执行",
                    theme,
                    Mathf.Max(0.0001f, sourceDuration + travelDuration + executorDuration),
                    cancellationToken);
                return;
            }

            Vector3 sourceAnchor = source.WorldBounds.center
                + Vector3.up * (source.WorldBounds.extents.y + 0.42f);
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
                    cancellationToken);
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
                + Vector3.up * (executor.WorldBounds.extents.y + 0.42f);
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
            SkillActionType actionType,
            float duration,
            CancellationToken cancellationToken)
        {
            if (transferSource == null || buffOwner == null)
            {
                return;
            }

            Color theme = actionType == SkillActionType.TriggerSweetTransfer
                ? new Color32(54, 224, 242, 255)
                : new Color32(255, 84, 178, 255);
            await SweetTransferParticleView.PlayAsync(
                particlePrefab,
                _fxRoot,
                transferSource.WorldBounds.center,
                buffOwner.WorldBounds.center,
                duration,
                cancellationToken,
                theme);
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
                cancellationToken);
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
            float dishContribution,
            int runningTotal,
            float duration,
            int stackIndex,
            int stackCount,
            CancellationToken cancellationToken)
        {
            Color theme = ResultThemeFor(line);
            if (target != null)
            {
                PlayResultHitSoundIfNeeded(group);
                target.SetSettlementFocus(1f);
                _ = PlayFeedbackSafelyAsync(
                    target,
                    FeedbackFor(line),
                    cancellationToken,
                    durationScale: Mathf.Max(0.05f, duration / 0.80f));
            }

            Vector3 anchor = target != null
                ? target.DishValueBadgeWorldPosition + Vector3.down * 0.42f
                : _mapper.Center + Vector3.up * 0.20f;
            if (stackCount > 1)
            {
                float centeredIndex = stackIndex - (stackCount - 1) * 0.5f;
                anchor += Vector3.up * (centeredIndex * 0.54f);
            }
            await SpawnLabelAsync(
                anchor,
                ResultHeader(line),
                ResultText(line, dishContribution, runningTotal),
                theme,
                duration,
                cancellationToken);
        }

        private void PlayResultHitSoundIfNeeded(SettlementEffectGroup group)
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
            GameApp.Audio.PlaySettlementHit(targetIds.Count);
        }

        public async Awaitable EndGroupAsync(float duration, CancellationToken cancellationToken)
        {
            DimAllDishes();
            DestroyGroupSpotlight();
            await Awaitable.WaitForSecondsAsync(Mathf.Max(0.0001f, duration), cancellationToken);
            ClearExpiredTransients();
        }

        public async Awaitable PlayFinaleAsync(
            int total,
            float duration,
            CancellationToken cancellationToken)
        {
            EndGroupImmediate();
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

            Vector3 center = _mapper.Center + Vector3.up * 0.35f;
            GameObject ring = CreateSprite("FinaleTableRing", center, new Color(1f, 0.72f, 0.16f, 0.42f), -4);
            ring.transform.localScale = Vector3.one * 0.28f;
            SpriteRenderer renderer = ring.GetComponent<SpriteRenderer>();
            Color initial = renderer.color;
            Tween ringTween = DOVirtual.Float(0f, 1f, Mathf.Max(0.0001f, duration), t =>
                {
                    if (ring == null || renderer == null)
                    {
                        return;
                    }

                    float eased = Mathf.SmoothStep(0f, 1f, t);
                    ring.transform.localScale = Vector3.one * Mathf.Lerp(0.28f, 6.2f, eased);
                    Color color = initial;
                    color.a *= 1f - eased;
                    renderer.color = color;
                })
                .SetEase(Ease.Linear)
                .SetLink(ring);

            Awaitable labelTask = SpawnLabelAsync(
                center + Vector3.up * 0.62f,
                "本桌结算",
                $"总分  {total}",
                new Color(1f, 0.62f, 0.10f, 1f),
                duration,
                cancellationToken,
                finalStamp: true);
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
            Vector2 size = bounds.size.sqrMagnitude > 0.0001f
                ? new Vector2(Mathf.Max(1.2f, bounds.size.x * 1.45f), Mathf.Max(1.2f, bounds.size.y * 1.45f))
                : Vector2.one * 1.8f;
            _groupSpotlight.transform.localScale = new Vector3(size.x, size.y, 1f);
        }

        private GameObject CreateSprite(string name, Vector3 worldPosition, Color color, int orderOffset)
        {
            GameObject root = new(name);
            root.transform.SetParent(_fxRoot, worldPositionStays: true);
            root.transform.position = worldPosition;
            SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
            renderer.sprite = BattleShadow.SoftShadowSprite;
            renderer.color = color;
            BattleSorting.Apply(renderer, BattleSorting.Fx, BattleSorting.OrderFloatingText + orderOffset);
            _transients.Add(root);
            return root;
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
            Transform parentOverride = null)
        {
            GameObject root = new("SettlementStageLabel");
            root.transform.SetParent(parentOverride != null ? parentOverride : _fxRoot, worldPositionStays: true);
            root.transform.position = anchor;
            _transients.Add(root);

            GameObject backgroundObject = new("Background");
            backgroundObject.transform.SetParent(root.transform, false);
            backgroundObject.transform.localPosition = Vector3.zero;
            backgroundObject.transform.localScale = finalStamp
                ? new Vector3(3.4f, 0.95f, 1f)
                : new Vector3(2.7f, 0.72f, 1f);
            SpriteRenderer background = backgroundObject.AddComponent<SpriteRenderer>();
            background.sprite = BattleShadow.SoftShadowSprite;
            background.color = WithAlpha(theme, 0.88f);
            BattleSorting.Apply(background, BattleSorting.Fx, BattleSorting.OrderFloatingText);

            TextMeshPro headerText = CreateText(
                root.transform,
                "Header",
                header,
                finalStamp ? 0.18f : 0.14f,
                finalStamp ? 28 : 22,
                finalStamp ? 0.085f : 0.070f,
                2);
            TextMeshPro bodyText = CreateText(
                root.transform,
                "Body",
                body,
                finalStamp ? -0.16f : -0.13f,
                finalStamp ? 44 : 32,
                finalStamp ? 0.115f : 0.090f,
                3);
            headerText.color = new Color(1f, 0.96f, 0.82f, 0.92f);
            bodyText.color = Color.white;

            Color backgroundColor = background.color;
            Color headerColor = headerText.color;
            Color bodyColor = bodyText.color;
            Vector3 targetScale = Vector3.one;
            root.transform.localScale = new Vector3(0.80f, 0.80f, 1f);
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
                    root.transform.position = anchor + Vector3.up * (0.10f * enter);

                    float alpha = holdUntilCleared ? 1f : Mathf.Clamp01((1f - t) / 0.24f);
                    background.color = WithAlpha(backgroundColor, alpha);
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

        internal static TextMeshPro CreateText(
            Transform parent,
            string name,
            string text,
            float localY,
            int fontSize,
            float characterSize,
            int orderOffset)
        {
            GameObject child = new(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = new Vector3(0f, localY, -0.02f);
            child.transform.localScale = Vector3.one * (characterSize * 1.1289f);
            TextMeshPro mesh = child.AddComponent<TextMeshPro>();
            mesh.text = text ?? string.Empty;
            mesh.alignment = TextAlignmentOptions.Center;
            mesh.fontSize = fontSize;
            mesh.fontStyle = FontStyles.Bold;
            mesh.textWrappingMode = TextWrappingModes.NoWrap;
            mesh.overflowMode = TextOverflowModes.Overflow;
            mesh.font = Resources.Load<TMP_FontAsset>("Fonts/AlimamaShuHeiTi-Bold SDF")
                ?? TMP_Settings.defaultFontAsset;
            mesh.ForceMeshUpdate(true, true);

            BattleSorting.Apply(mesh, BattleSorting.Fx, BattleSorting.OrderFloatingText + orderOffset);
            return mesh;
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
                        return new Color(1f, 0.28f, 0.68f, 1f);
                    case SkillExecutionKind.CopiedSkill:
                        return new Color(0.24f, 0.88f, 1f, 1f);
                }
            }

            if (source != null && source.Type == ScoreSourceType.Relic)
            {
                return new Color(0.72f, 0.48f, 1f, 1f);
            }

            return new Color(1f, 0.72f, 0.18f, 1f);
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
                default:
                    return SettlementDishFeedbackKind.GenericValueChanged;
            }
        }

        internal static Color ResultThemeFor(ScoreLine line)
        {
            if (line == null)
            {
                return SettlementAttributePalette.Special;
            }

            if (line.Kind == ScoreLineKind.SweetTransferFailed)
            {
                return new Color32(224, 106, 132, 255);
            }

            if (line.Kind == ScoreLineKind.SweetTransferBuffApplied
                || line.Kind == ScoreLineKind.SweetTransferBuffTriggered)
            {
                return line.Trace?.ActionType == SkillActionType.TriggerSweetTransfer
                    ? new Color32(54, 224, 242, 255)
                    : new Color32(255, 84, 178, 255);
            }

            return line.Kind == ScoreLineKind.TriggerSweetTransfer
                || line.Kind == ScoreLineKind.TriggeredSweetTransferSource
                ? new Color32(255, 77, 173, 255)
                : SettlementAttributePalette.For(line.Kind);
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
                _ => "结算结果",
            };
        }

        internal static string ResultText(ScoreLine line, float dishContribution, int runningTotal)
        {
            if (line == null)
            {
                return $"总分  {runningTotal}";
            }

            if (Mathf.Abs(line.Value) <= 0.001f
                && line.Kind != ScoreLineKind.FinalMultiplier
                && line.Kind != ScoreLineKind.SweetTransferFailed)
            {
                return "无变化";
            }

            string signed = $"{(line.Value >= 0f ? "+" : string.Empty)}{line.Value:0.#}";
            switch (line.Kind)
            {
                case ScoreLineKind.DishFlat:
                    return $"分数 {signed}";
                case ScoreLineKind.DishMultiplier:
                    return $"倍率 ×{line.Value:0.##}";
                case ScoreLineKind.DishMultiplierAdd:
                    return $"倍率 {signed}";
                case ScoreLineKind.FinalFlat:
                    return $"总分 {signed}  →  {runningTotal}";
                case ScoreLineKind.FinalMultiplier:
                    return $"总分 ×{line.Value:0.##}  →  {runningTotal}";
                case ScoreLineKind.Gold:
                    return $"金币 {signed}";
                case ScoreLineKind.Layer:
                    return $"层数 {signed}";
                case ScoreLineKind.SilverItemRoll:
                    return $"获得装饰品和消耗品 ×{Mathf.RoundToInt(line.Value)}";
                case ScoreLineKind.CopySkill:
                    return $"获得技能 ×{Mathf.RoundToInt(line.Value)}";
                case ScoreLineKind.TriggerSweetTransfer:
                    return line.Value > 1f ? $"触发 ×{Mathf.RoundToInt(line.Value)}" : "触发甜蜜传递";
                case ScoreLineKind.TriggeredSweetTransferSource:
                    return "来源已接力";
                case ScoreLineKind.SweetTransferBuffApplied:
                    return $"挂载目标 ×{Mathf.RoundToInt(line.Value)}";
                case ScoreLineKind.SweetTransferBuffTriggered:
                    return line.Trace?.ActionType == SkillActionType.TriggerSweetTransfer
                        ? $"额外目标 +{Mathf.RoundToInt(line.Value)}"
                        : $"本行倍率 ×{line.Value:0.##}";
                case ScoreLineKind.SweetTransferFailed:
                    return "没有可传递目标";
                default:
                    return string.IsNullOrEmpty(line.Message) ? signed : line.Message;
            }
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
