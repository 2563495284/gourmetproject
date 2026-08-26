using System;
using System.Collections.Generic;
using BreakInfinity;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Scoring;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 结算语义色板。结果色表达“发生了什么”，来源色表达“谁触发”，两套语义互不混用。
    /// </summary>
    internal static class SettlementColorPalette
    {
        // 结果属性
        public static readonly Color BaseScore = new Color32(246, 196, 83, 255);
        public static readonly Color PermanentScore = new Color32(51, 125, 181, 255);
        public static readonly Color AddMultiplier = new Color32(57, 208, 176, 255);
        public static readonly Color MultiplyMultiplier = new Color32(255, 90, 95, 255);
        public static readonly Color Gold = new Color32(244, 183, 64, 255);
        public static readonly Color CakeLayer = new Color32(255, 138, 61, 255);
        public static readonly Color CopySkill = new Color32(54, 224, 242, 255);
        public static readonly Color SweetTransfer = new Color32(255, 84, 178, 255);
        public static readonly Color Failure = new Color32(224, 106, 132, 255);
        public static readonly Color Special = new Color32(169, 120, 255, 255);
        public static readonly Color CountAs = new Color32(255, 204, 82, 255);
        public static readonly Color TemporaryCategory = new Color32(226, 92, 126, 255);
        public static readonly Color FinalScore = new Color32(255, 158, 26, 255);

        // 来源身份
        public static readonly Color NativeSource = new Color32(255, 184, 46, 255);
        public static readonly Color RelicSource = new Color32(184, 122, 255, 255);
        public static readonly Color SweetTransferSource = SweetTransfer;
        public static readonly Color CopiedSkillSource = CopySkill;

        // 标签使用压暗的语义底板和提亮的语义文字，避免所有文字看起来都是黑色。
        public static readonly Color TextInk = new Color32(35, 24, 15, 255);
        public static readonly Color TextLight = new Color32(255, 244, 220, 255);
        public static Color For(ScoreLineKind kind)
        {
            return kind switch
            {
                ScoreLineKind.DishBase or ScoreLineKind.DishFlat or ScoreLineKind.FinalFlat
                    or ScoreLineKind.ExtraSettlement => BaseScore,
                ScoreLineKind.DishPermanentFlat => PermanentScore,
                ScoreLineKind.DishMultiplierAdd => AddMultiplier,
                ScoreLineKind.DishMultiplier or ScoreLineKind.FinalMultiplier => MultiplyMultiplier,
                ScoreLineKind.Gold => Gold,
                ScoreLineKind.Layer => CakeLayer,
                ScoreLineKind.CopySkill => CopySkill,
                ScoreLineKind.TriggerSweetTransfer
                    or ScoreLineKind.TriggeredSweetTransferSource
                    or ScoreLineKind.SweetTransferBuffApplied
                    or ScoreLineKind.SweetTransferBuffTriggered => SweetTransfer,
                ScoreLineKind.SweetTransferFailed => Failure,
                ScoreLineKind.CountAs or ScoreLineKind.EmptyCountAs => CountAs,
                ScoreLineKind.TemporaryCategory => TemporaryCategory,
                _ => Special,
            };
        }

        public static Color PlateFor(Color theme)
        {
            return Color.Lerp(TextInk, theme, 0.25f);
        }

        public static Color TextFor(Color theme)
        {
            return Color.Lerp(theme, TextLight, 0.30f);
        }

        public static Color ResultHeaderTextFor(Color semanticColor)
        {
            return Color.Lerp(semanticColor, TextLight, 0.18f);
        }

        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }
    }

    internal enum SettlementScoreFeedbackStep
    {
        None = 0,
        Subtle = 1,
        Clear = 2,
        Strong = 3,
        Burst = 4,
        Peak = 5,
    }

    internal enum SettlementScoreFireFeedback
    {
        None = 0,
        TransientSpark = 1,
        IgnitedBurst = 2,
    }

    /// <summary>由本帧净分数解析出的 HUD 飘字、面板和普通火焰反馈参数。</summary>
    internal readonly struct SettlementScoreFeedbackProfile
    {
        public SettlementScoreFeedbackProfile(
            SettlementScoreFeedbackStep step,
            float stepProgress,
            float intensity,
            bool isPositive,
            Color textColor,
            Color outlineColor,
            float outlineWidth,
            float startScale,
            float peakScale,
            float travelDistance,
            float scorePunch,
            float panelPunch,
            float shakeStrength,
            float rollDuration,
            float fadeDuration,
            float fireStrength,
            bool allowsTransientSpark,
            bool allowsIgnitedBurst)
        {
            Step = step;
            StepProgress = stepProgress;
            Intensity = intensity;
            IsPositive = isPositive;
            TextColor = textColor;
            OutlineColor = outlineColor;
            OutlineWidth = outlineWidth;
            StartScale = startScale;
            PeakScale = peakScale;
            TravelDistance = travelDistance;
            ScorePunch = scorePunch;
            PanelPunch = panelPunch;
            ShakeStrength = shakeStrength;
            RollDuration = rollDuration;
            FadeDuration = fadeDuration;
            FireStrength = fireStrength;
            AllowsTransientSpark = allowsTransientSpark;
            AllowsIgnitedBurst = allowsIgnitedBurst;
        }

        public SettlementScoreFeedbackStep Step { get; }
        public float StepProgress { get; }
        public float Intensity { get; }
        public bool IsPositive { get; }
        public bool IsNegative => Visible && !IsPositive;
        public bool Visible => Step != SettlementScoreFeedbackStep.None;
        public Color TextColor { get; }
        public Color OutlineColor { get; }
        public float OutlineWidth { get; }
        public float StartScale { get; }
        public float PeakScale { get; }
        public float TravelDistance { get; }
        public float ScorePunch { get; }
        public float PanelPunch { get; }
        public float ShakeStrength { get; }
        public float RollDuration { get; }
        public float FadeDuration { get; }
        public float FireStrength { get; }
        public bool AllowsTransientSpark { get; }
        public bool AllowsIgnitedBurst { get; }
    }

    /// <summary>
    /// 固定分数阶梯：1 / 50 / 200 / 1000 / 5000。仅由净分数决定，
    /// 不读取目标分、周目、结算行语义或 ImpactTier。
    /// </summary>
    internal static class SettlementScoreFeedbackResolver
    {
        private static readonly Color[] PositiveColors =
        {
            new Color32(0x73, 0xCF, 0xFF, 0xFF),
            new Color32(0x58, 0xE1, 0xDE, 0xFF),
            new Color32(0xFF, 0xD4, 0x5B, 0xFF),
            new Color32(0xFF, 0x87, 0x2F, 0xFF),
            new Color32(0xFF, 0xF3, 0xD0, 0xFF),
        };

        private static readonly Color[] PositiveOutlines =
        {
            new Color32(0x27, 0x5C, 0x91, 0xFF),
            new Color32(0x14, 0x7A, 0x78, 0xFF),
            new Color32(0xA7, 0x63, 0x00, 0xFF),
            new Color32(0xB9, 0x33, 0x12, 0xFF),
            new Color32(0xD9, 0x4B, 0x19, 0xFF),
        };

        private static readonly Color[] NegativeColors =
        {
            new Color32(0xF2, 0xA1, 0xAE, 0xFF),
            new Color32(0xFF, 0x74, 0x85, 0xFF),
            new Color32(0xFF, 0x48, 0x5E, 0xFF),
            new Color32(0xF1, 0x26, 0x46, 0xFF),
            new Color32(0xFF, 0x12, 0x3D, 0xFF),
        };

        private static readonly Color[] NegativeOutlines =
        {
            new Color32(0x7B, 0x2A, 0x3D, 0xFF),
            new Color32(0x8E, 0x1F, 0x38, 0xFF),
            new Color32(0x8D, 0x10, 0x2B, 0xFF),
            new Color32(0x76, 0x00, 0x18, 0xFF),
            new Color32(0x5E, 0x00, 0x14, 0xFF),
        };

        public static SettlementScoreFeedbackProfile Resolve(BigDouble delta)
        {
            if (delta == BigDouble.Zero)
            {
                return default;
            }

            bool isPositive = delta > BigDouble.Zero;
            BigDouble magnitude = BigDouble.Abs(delta);
            int segment;
            float stepProgress;
            SettlementScoreFeedbackStep step;
            if (magnitude >= 5000d)
            {
                segment = 4;
                stepProgress = 1f;
                step = SettlementScoreFeedbackStep.Peak;
            }
            else if (magnitude >= 1000d)
            {
                segment = 3;
                stepProgress = SmoothStep(InverseLerp(1000f, 5000f, magnitude));
                step = SettlementScoreFeedbackStep.Burst;
            }
            else if (magnitude >= 200d)
            {
                segment = 2;
                stepProgress = SmoothStep(InverseLerp(200f, 1000f, magnitude));
                step = SettlementScoreFeedbackStep.Strong;
            }
            else if (magnitude >= 50d)
            {
                segment = 1;
                stepProgress = SmoothStep(InverseLerp(50f, 200f, magnitude));
                step = SettlementScoreFeedbackStep.Clear;
            }
            else
            {
                segment = 0;
                stepProgress = SmoothStep(InverseLerp(1f, 50f, magnitude));
                step = SettlementScoreFeedbackStep.Subtle;
            }

            float intensity = segment >= 4
                ? 1f
                : (segment + stepProgress) / 4f;
            Color[] colors = isPositive ? PositiveColors : NegativeColors;
            Color[] outlines = isPositive ? PositiveOutlines : NegativeOutlines;
            Color textColor = segment >= 4
                ? colors[4]
                : Color.Lerp(colors[segment], colors[segment + 1], stepProgress);
            Color outlineColor = segment >= 4
                ? outlines[4]
                : Color.Lerp(outlines[segment], outlines[segment + 1], stepProgress);
            float travelDistance = isPositive
                ? Mathf.Lerp(14f, 42f, intensity)
                : -Mathf.Lerp(10f, 30f, intensity);
            float panelPunch = isPositive
                ? magnitude >= 50d ? Mathf.Lerp(0.04f, 0.16f, intensity) : 0f
                : Mathf.Lerp(0.025f, 0.10f, intensity);

            return new SettlementScoreFeedbackProfile(
                step,
                stepProgress,
                intensity,
                isPositive,
                textColor,
                outlineColor,
                Mathf.Lerp(0.08f, 0.18f, intensity),
                Mathf.Lerp(0.88f, 0.72f, intensity),
                Mathf.Lerp(1.04f, 1.30f, intensity),
                travelDistance,
                isPositive
                    ? Mathf.Lerp(0.05f, 0.20f, intensity)
                    : Mathf.Lerp(0.03f, 0.12f, intensity),
                panelPunch,
                isPositive ? 0f : Mathf.Lerp(2f, 10f, intensity),
                Mathf.Lerp(0.22f, 0.36f, intensity),
                Mathf.Lerp(0.13f, 0.20f, intensity),
                isPositive ? Mathf.Lerp(0.20f, 1f, intensity) : 0f,
                isPositive && magnitude >= 200d,
                isPositive && magnitude >= 50d);
        }

        public static SettlementScoreFireFeedback ResolveFireFeedback(
            SettlementScoreFeedbackProfile profile,
            SettlementPacePhase phase)
        {
            if (!profile.Visible || !profile.IsPositive)
            {
                return SettlementScoreFireFeedback.None;
            }

            if (phase == SettlementPacePhase.BelowTarget)
            {
                return profile.AllowsTransientSpark
                    ? SettlementScoreFireFeedback.TransientSpark
                    : SettlementScoreFireFeedback.None;
            }

            return profile.AllowsIgnitedBurst
                ? SettlementScoreFireFeedback.IgnitedBurst
                : SettlementScoreFireFeedback.None;
        }

        private static float InverseLerp(float lower, float upper, BigDouble value)
        {
            double clamped = Math.Max(lower, Math.Min(upper, value.ToDouble()));
            return Mathf.InverseLerp(lower, upper, (float)clamped);
        }

        private static float SmoothStep(float value)
        {
            float clamped = Mathf.Clamp01(value);
            return clamped * clamped * (3f - 2f * clamped);
        }
    }

    internal enum SettlementSweetTransferTransition
    {
        None = 0,
        Begin = 1,
        Keep = 2,
        Switch = 3,
        Clear = 4,
    }

    /// <summary>纯逻辑判定甜蜜传递持续来源应如何切换，避免表现状态跨效果组泄漏。</summary>
    internal static class SettlementSweetTransferTransitionResolver
    {
        public static SettlementSweetTransferTransition Resolve(int currentSourceDishId, int nextSourceDishId)
        {
            if (currentSourceDishId <= 0)
            {
                return nextSourceDishId > 0
                    ? SettlementSweetTransferTransition.Begin
                    : SettlementSweetTransferTransition.None;
            }

            if (nextSourceDishId <= 0)
            {
                return SettlementSweetTransferTransition.Clear;
            }

            return currentSourceDishId == nextSourceDishId
                ? SettlementSweetTransferTransition.Keep
                : SettlementSweetTransferTransition.Switch;
        }
    }

    internal readonly struct SettlementSweetTransferPresentationKey : IEquatable<SettlementSweetTransferPresentationKey>
    {
        public SettlementSweetTransferPresentationKey(
            int sourceDishInstanceId,
            int executorDishInstanceId,
            string skillId,
            int handoffExecutionGroupId = 0)
        {
            SourceDishInstanceId = sourceDishInstanceId;
            ExecutorDishInstanceId = executorDishInstanceId;
            SkillId = skillId ?? string.Empty;
            HandoffExecutionGroupId = handoffExecutionGroupId;
        }

        public int SourceDishInstanceId { get; }
        public int ExecutorDishInstanceId { get; }
        public string SkillId { get; }
        public int HandoffExecutionGroupId { get; }
        public bool IsEmpty => SourceDishInstanceId <= 0 || ExecutorDishInstanceId <= 0;

        public bool Equals(SettlementSweetTransferPresentationKey other)
        {
            if (SourceDishInstanceId != other.SourceDishInstanceId
                || ExecutorDishInstanceId != other.ExecutorDishInstanceId)
            {
                return false;
            }

            if (HandoffExecutionGroupId > 0 || other.HandoffExecutionGroupId > 0)
            {
                return HandoffExecutionGroupId > 0
                    && HandoffExecutionGroupId == other.HandoffExecutionGroupId;
            }

            return string.Equals(SkillId, other.SkillId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is SettlementSweetTransferPresentationKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = SourceDishInstanceId;
                hash = (hash * 397) ^ ExecutorDishInstanceId;
                hash = HandoffExecutionGroupId > 0
                    ? (hash * 397) ^ HandoffExecutionGroupId
                    : (hash * 397) ^ StringComparer.Ordinal.GetHashCode(SkillId ?? string.Empty);
                return hash;
            }
        }
    }

    internal readonly struct SettlementSweetTransferPresentationContext
    {
        private SettlementSweetTransferPresentationContext(
            int sourceDishInstanceId,
            int effectOwnerDishInstanceId,
            int executorDishInstanceId,
            string sourceName,
            string executorName,
            string skillId,
            string skillName,
            int executionGroupId,
            int handoffExecutionGroupId,
            string handoffSkillId,
            int handoffPayloadCount)
        {
            SourceDishInstanceId = sourceDishInstanceId;
            EffectOwnerDishInstanceId = effectOwnerDishInstanceId;
            ExecutorDishInstanceId = executorDishInstanceId;
            SourceName = sourceName ?? string.Empty;
            ExecutorName = executorName ?? string.Empty;
            SkillId = skillId ?? string.Empty;
            SkillName = skillName ?? SkillId;
            ExecutionGroupId = executionGroupId;
            HandoffExecutionGroupId = handoffExecutionGroupId;
            HandoffSkillId = string.IsNullOrEmpty(handoffSkillId) ? SkillId : handoffSkillId;
            HandoffPayloadCount = handoffPayloadCount;
        }

        /// <summary>本次真正发送粒子并播放触发反馈的食物实例。</summary>
        public int SourceDishInstanceId { get; }
        /// <summary>当前被执行子技能的历史拥有者，仅用于效果来源归因。</summary>
        public int EffectOwnerDishInstanceId { get; }
        public int ExecutorDishInstanceId { get; }
        public string SourceName { get; }
        public string ExecutorName { get; }
        public string SkillId { get; }
        public string SkillName { get; }
        public int ExecutionGroupId { get; }
        public int HandoffExecutionGroupId { get; }
        public string HandoffSkillId { get; }
        public int HandoffPayloadCount { get; }
        public bool IsValid => SourceDishInstanceId > 0 && ExecutorDishInstanceId > 0;
        public bool IsSelfTransfer => IsValid && SourceDishInstanceId == ExecutorDishInstanceId;
        public SettlementSweetTransferPresentationKey Key => new(
            SourceDishInstanceId,
            ExecutorDishInstanceId,
            SkillId,
            HandoffExecutionGroupId);

        public bool RequiresHandoffAfter(SettlementSweetTransferPresentationKey previous)
        {
            return IsValid && !Key.Equals(previous);
        }

        public static SettlementSweetTransferPresentationContext FromGroup(SettlementEffectGroup group)
        {
            return group != null ? FromTrace(group.Trace, group.GroupId) : default;
        }

        public static SettlementSweetTransferPresentationContext FromLine(ScoreLine line)
        {
            return line != null ? FromTrace(line.Trace, line.ExecutionGroupId) : default;
        }

        private static SettlementSweetTransferPresentationContext FromTrace(
            SkillExecutionTrace trace,
            int executionGroupId)
        {
            if (trace == null || trace.Kind != SkillExecutionKind.SweetTransfer)
            {
                return default;
            }

            int handoffSourceDishInstanceId = trace.SweetTransferHandoffSourceDishInstanceId > 0
                ? trace.SweetTransferHandoffSourceDishInstanceId
                : trace.OwnerDishInstanceId;
            return new SettlementSweetTransferPresentationContext(
                handoffSourceDishInstanceId,
                trace.OwnerDishInstanceId,
                trace.RuntimeSelfDishInstanceId,
                trace.OwnerDishName,
                trace.RuntimeSelfDishName,
                trace.SkillId,
                trace.SkillName,
                executionGroupId,
                trace.SweetTransferHandoffExecutionGroupId,
                trace.SweetTransferHandoffSkillId,
                trace.SweetTransferHandoffPayloadCount);
        }
    }

    /// <summary>同一波甜蜜传递抵达后，向 HUD 一次性揭示的一件被动装饰品表现。</summary>
    public readonly struct PassiveSettlementPresentationBatch
    {
        public PassiveSettlementPresentationBatch(
            string itemId,
            string infoTextBefore,
            string infoTextAfter,
            int goldDelta,
            bool shouldPulse)
        {
            ItemId = itemId ?? string.Empty;
            InfoTextBefore = infoTextBefore ?? string.Empty;
            InfoTextAfter = infoTextAfter ?? string.Empty;
            GoldDelta = goldDelta;
            ShouldPulse = shouldPulse;
        }

        public string ItemId { get; }

        public string InfoTextBefore { get; }

        public string InfoTextAfter { get; }

        public int GoldDelta { get; }

        public bool ShouldPulse { get; }
    }

    /// <summary>
    /// 把正式结算时已经落地的被动表现记录，按甜蜜传递粒子的真实交接批次消费。
    /// 每条记录最多消费一次；同一波同一装饰品合并成一个 HUD 更新。
    /// </summary>
    internal sealed class PassiveSettlementPlaybackLedger
    {
        private sealed class BatchBuilder
        {
            public string ItemId;
            public string InfoTextBefore;
            public string InfoTextAfter;
            public int GoldDelta;
            public bool ShouldPulse;

            public PassiveSettlementPresentationBatch Build()
                => new PassiveSettlementPresentationBatch(
                    ItemId,
                    InfoTextBefore,
                    InfoTextAfter,
                    GoldDelta,
                    ShouldPulse);
        }

        private readonly IReadOnlyList<PassiveSettlementPresentationOccurrence> _occurrences;
        private readonly bool[] _consumed;

        public PassiveSettlementPlaybackLedger(
            IReadOnlyList<PassiveSettlementPresentationOccurrence> occurrences)
        {
            _occurrences = occurrences ?? Array.Empty<PassiveSettlementPresentationOccurrence>();
            _consumed = new bool[_occurrences.Count];
        }

        public int UnconsumedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _consumed.Length; i++)
                {
                    if (!_consumed[i])
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public IReadOnlyList<PassiveSettlementPresentationBatch> ConsumeWave(
            IReadOnlyList<SettlementSweetTransferPresentationContext> handoffs)
        {
            if (handoffs == null || handoffs.Count == 0 || _occurrences.Count == 0)
            {
                return Array.Empty<PassiveSettlementPresentationBatch>();
            }

            var builders = new List<BatchBuilder>();
            var builderIndexByItemId = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int occurrenceIndex = 0; occurrenceIndex < _occurrences.Count; occurrenceIndex++)
            {
                if (_consumed[occurrenceIndex])
                {
                    continue;
                }

                PassiveSettlementPresentationOccurrence occurrence = _occurrences[occurrenceIndex];
                if (!MatchesAny(occurrence.Transfer, handoffs))
                {
                    continue;
                }

                _consumed[occurrenceIndex] = true;
                if (!builderIndexByItemId.TryGetValue(occurrence.ItemId, out int builderIndex))
                {
                    builderIndex = builders.Count;
                    builderIndexByItemId.Add(occurrence.ItemId, builderIndex);
                    builders.Add(new BatchBuilder
                    {
                        ItemId = occurrence.ItemId,
                        InfoTextBefore = occurrence.InfoTextBefore,
                        InfoTextAfter = occurrence.InfoTextAfter,
                    });
                }

                BatchBuilder builder = builders[builderIndex];
                builder.InfoTextAfter = occurrence.InfoTextAfter;
                builder.GoldDelta += occurrence.GoldDelta;
                builder.ShouldPulse |= occurrence.ShouldPulse;
            }

            if (builders.Count == 0)
            {
                return Array.Empty<PassiveSettlementPresentationBatch>();
            }

            var result = new PassiveSettlementPresentationBatch[builders.Count];
            for (int i = 0; i < builders.Count; i++)
            {
                result[i] = builders[i].Build();
            }

            return result;
        }

        private static bool MatchesAny(
            SweetTransferOccurrence occurrence,
            IReadOnlyList<SettlementSweetTransferPresentationContext> handoffs)
        {
            for (int i = 0; i < handoffs.Count; i++)
            {
                SettlementSweetTransferPresentationContext handoff = handoffs[i];
                if (occurrence.SourceInstanceId != handoff.SourceDishInstanceId
                    || occurrence.TargetInstanceId != handoff.ExecutorDishInstanceId)
                {
                    continue;
                }

                if (occurrence.HandoffExecutionGroupId <= 0
                    || handoff.HandoffExecutionGroupId <= 0
                    || occurrence.HandoffExecutionGroupId == handoff.HandoffExecutionGroupId)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public enum SettlementBeatKind
    {
        SourceStarted = 0,
        ScopeShown = 1,
        ResultApplied = 2,
        GroupCompleted = 3,
        FinaleConfirmed = 4,
        DishStarted = 5,
        DishCompleted = 6,
    }

    /// <summary>结算反馈的视觉强度。只描述表现层级，不参与任何计分。</summary>
    public enum SettlementImpactTier
    {
        Base = 0,
        Normal = 1,
        Strong = 2,
        Chain = 3,
        Finale = 4,
    }

    /// <summary>当前结算累计分数驱动的常驻演出档位。档位在单次结算中只升不降。</summary>
    internal enum SettlementPacePhase
    {
        BelowTarget = 0,
        TargetReached = 1,
        DoubleTarget = 2,
    }

    /// <summary>稳定的结算节拍信号；当前只预留给音效，后续可用 Speed 映射 pitch。</summary>
    public readonly struct SettlementBeatSignal
    {
        public SettlementBeatSignal(
            SettlementBeatKind kind,
            string sourceName,
            int dishInstanceId,
            float speed,
            float normalizedProgress)
        {
            Kind = kind;
            SourceName = sourceName ?? string.Empty;
            DishInstanceId = dishInstanceId;
            Speed = speed;
            NormalizedProgress = normalizedProgress;
            LineKind = ScoreLineKind.DishBase;
            BeforeScore = BigDouble.Zero;
            AfterScore = BigDouble.Zero;
            ImpactTier = SettlementImpactTier.Base;
            TargetCount = 0;
            ReachedTarget = false;
            HasScoreChange = false;
        }

        public SettlementBeatSignal(
            SettlementBeatKind kind,
            string sourceName,
            int dishInstanceId,
            float speed,
            float normalizedProgress,
            ScoreLineKind lineKind,
            BigDouble beforeScore,
            BigDouble afterScore,
            SettlementImpactTier impactTier,
            int targetCount,
            bool reachedTarget)
            : this(kind, sourceName, dishInstanceId, speed, normalizedProgress)
        {
            LineKind = lineKind;
            BeforeScore = beforeScore;
            AfterScore = afterScore;
            ImpactTier = impactTier;
            TargetCount = Mathf.Max(1, targetCount);
            ReachedTarget = reachedTarget;
            HasScoreChange = true;
        }

        public SettlementBeatKind Kind { get; }
        public string SourceName { get; }
        public int DishInstanceId { get; }
        public float Speed { get; }
        public float NormalizedProgress { get; }
        public ScoreLineKind LineKind { get; }
        public BigDouble BeforeScore { get; }
        public BigDouble AfterScore { get; }
        public BigDouble ScoreDelta => AfterScore - BeforeScore;
        public SettlementImpactTier ImpactTier { get; }
        public int TargetCount { get; }
        public bool ReachedTarget { get; }
        public bool HasScoreChange { get; }
    }

    internal sealed class SettlementPresentationPlan
    {
        public List<SettlementBaseBeat> BaseBeats { get; } = new();
        public List<SettlementEffectGroup> PreludeGroups { get; } = new();
        public List<SettlementDishChapter> DishChapters { get; } = new();
        public List<SettlementEffectGroup> EpilogueGroups { get; } = new();

        public int ResultBeatCount
        {
            get
            {
                // 基础分在结算开始时直接写入 BattleInfo，不再占用表现节拍。
                int count = 1;
                count += CountResultLines(PreludeGroups);
                count += CountResultLines(EpilogueGroups);
                for (int i = 0; i < DishChapters.Count; i++)
                {
                    count += CountResultLines(DishChapters[i].Groups);
                }

                return Mathf.Max(1, count);
            }
        }

        private static int CountResultLines(IReadOnlyList<SettlementEffectGroup> groups)
        {
            int count = 0;
            if (groups == null)
            {
                return count;
            }

            for (int i = 0; i < groups.Count; i++)
            {
                count += groups[i]?.Lines.Count ?? 0;
            }

            return count;
        }

        public static SettlementPresentationPlan Build(ScoreResult result)
        {
            var plan = new SettlementPresentationPlan();
            if (result == null)
            {
                return plan;
            }

            var baseLines = new Dictionary<int, ScoreLine>();
            var remainingBaseLines = new List<ScoreLine>();
            for (int i = 0; i < result.ScoreLines.Count; i++)
            {
                ScoreLine line = result.ScoreLines[i];
                if (line == null || line.Kind != ScoreLineKind.DishBase)
                {
                    continue;
                }

                if (!baseLines.ContainsKey(line.DishInstanceId))
                {
                    baseLines[line.DishInstanceId] = line;
                }
                else
                {
                    remainingBaseLines.Add(line);
                }
            }

            var addedBaseIds = new HashSet<int>();
            for (int i = 0; i < result.DishScores.Count; i++)
            {
                DishScore score = result.DishScores[i];
                if (score == null || !addedBaseIds.Add(score.DishInstanceId))
                {
                    continue;
                }

                baseLines.TryGetValue(score.DishInstanceId, out ScoreLine line);
                plan.BaseBeats.Add(new SettlementBaseBeat(
                    score.DishInstanceId,
                    score.DishId,
                    line,
                    line != null ? line.After : score.BaseValue));
            }

            foreach (KeyValuePair<int, ScoreLine> entry in baseLines)
            {
                if (addedBaseIds.Add(entry.Key))
                {
                    plan.BaseBeats.Add(new SettlementBaseBeat(
                        entry.Key,
                        entry.Value.DishId,
                        entry.Value,
                        entry.Value.After));
                }
            }

            for (int i = 0; i < remainingBaseLines.Count; i++)
            {
                ScoreLine line = remainingBaseLines[i];
                plan.BaseBeats.Add(new SettlementBaseBeat(
                    line.DishInstanceId,
                    line.DishId,
                    line,
                    line.After));
            }

            var chaptersByDishId = new Dictionary<int, SettlementDishChapter>();
            for (int i = 0; i < plan.BaseBeats.Count; i++)
            {
                SettlementBaseBeat beat = plan.BaseBeats[i];
                if (beat == null || chaptersByDishId.ContainsKey(beat.DishInstanceId))
                {
                    continue;
                }

                var chapter = new SettlementDishChapter(beat);
                chaptersByDishId.Add(beat.DishInstanceId, chapter);
                plan.DishChapters.Add(chapter);
            }

            int lineCount = result.ScoreLines.Count;
            var previousBaseDishIds = new int[lineCount];
            var nextBaseDishIds = new int[lineCount];
            int currentBaseDishId = 0;
            for (int i = 0; i < lineCount; i++)
            {
                ScoreLine line = result.ScoreLines[i];
                if (line != null && line.Kind == ScoreLineKind.DishBase)
                {
                    currentBaseDishId = line.DishInstanceId;
                }

                previousBaseDishIds[i] = currentBaseDishId;
            }

            int nextBaseDishId = 0;
            for (int i = lineCount - 1; i >= 0; i--)
            {
                ScoreLine line = result.ScoreLines[i];
                if (line != null && line.Kind == ScoreLineKind.DishBase)
                {
                    nextBaseDishId = line.DishInstanceId;
                }

                nextBaseDishIds[i] = nextBaseDishId;
            }

            for (int i = 0; i < lineCount; i++)
            {
                ScoreLine line = result.ScoreLines[i];
                if (line == null || line.Kind == ScoreLineKind.DishBase)
                {
                    continue;
                }

                if (line.Phase == ScorePhase.BeforeAll)
                {
                    AppendLine(plan.PreludeGroups, line);
                    continue;
                }

                if (line.Phase >= ScorePhase.AfterAllDishes)
                {
                    AppendLine(plan.EpilogueGroups, line);
                    continue;
                }

                int chapterDishId = line.Phase == ScorePhase.BeforeDish
                    ? ResolveSemanticChapterDishId(line, chaptersByDishId)
                    : previousBaseDishIds[i];
                if (line.Phase == ScorePhase.BeforeDish && chapterDishId <= 0)
                {
                    chapterDishId = nextBaseDishIds[i];
                }

                if (chapterDishId <= 0 || !chaptersByDishId.ContainsKey(chapterDishId))
                {
                    chapterDishId = ResolveSemanticChapterDishId(line, chaptersByDishId);
                }

                if (chapterDishId > 0
                    && chaptersByDishId.TryGetValue(chapterDishId, out SettlementDishChapter chapter))
                {
                    AppendLine(chapter.Groups, line);
                }
                else if (line.Phase < ScorePhase.DishBase)
                {
                    AppendLine(plan.PreludeGroups, line);
                }
                else
                {
                    AppendLine(plan.EpilogueGroups, line);
                }
            }

            AddAggregateFallbacks(plan, result);
            return plan;
        }

        private static int ResolveSemanticChapterDishId(
            ScoreLine line,
            IReadOnlyDictionary<int, SettlementDishChapter> chaptersByDishId)
        {
            if (line == null || chaptersByDishId == null)
            {
                return 0;
            }

            int runtimeSelfId = line.Trace?.RuntimeSelfDishInstanceId ?? 0;
            if (runtimeSelfId > 0 && chaptersByDishId.ContainsKey(runtimeSelfId))
            {
                return runtimeSelfId;
            }

            int sourceDishId = line.Source?.DishInstanceId ?? 0;
            if (sourceDishId > 0 && chaptersByDishId.ContainsKey(sourceDishId))
            {
                return sourceDishId;
            }

            return line.DishInstanceId > 0 && chaptersByDishId.ContainsKey(line.DishInstanceId)
                ? line.DishInstanceId
                : 0;
        }

        private static void AppendLine(List<SettlementEffectGroup> groups, ScoreLine line)
        {
            if (groups == null || line == null)
            {
                return;
            }

            SettlementEffectGroup current = groups.Count > 0
                ? groups[groups.Count - 1]
                : null;
            if (current == null || !current.CanAppend(line))
            {
                groups.Add(new SettlementEffectGroup(line));
                return;
            }

            current.Append(line);
        }

        private static void AddAggregateFallbacks(SettlementPresentationPlan plan, ScoreResult result)
        {
            bool hasFinalFlat = false;
            bool hasFinalMultiplier = false;
            bool hasGold = false;
            bool hasLayer = false;
            for (int i = 0; i < result.ScoreLines.Count; i++)
            {
                ScoreLine line = result.ScoreLines[i];
                if (line == null)
                {
                    continue;
                }

                hasFinalFlat |= line.Kind == ScoreLineKind.FinalFlat;
                hasFinalMultiplier |= line.Kind == ScoreLineKind.FinalMultiplier;
                hasGold |= line.Kind == ScoreLineKind.Gold;
                hasLayer |= line.Kind == ScoreLineKind.Layer;
            }

            var source = ScoreSource.FinalModifier("settlement_fallback", "结算");
            if (!hasFinalFlat && BigDouble.Abs(result.FinalFlat) > 0.001f)
            {
                AppendSynthetic(plan, new ScoreLine(
                    ScorePhase.Final,
                    ScoreLineKind.FinalFlat,
                    source,
                    0,
                    string.Empty,
                    null,
                    result.FinalFlat,
                    0f,
                    result.FinalFlat,
                    $"局级加法 {FormatSigned(result.FinalFlat)}"));
            }

            if (!hasFinalMultiplier && BigDouble.Abs(result.FinalMultiplier - 1f) > 0.001f)
            {
                AppendSynthetic(plan, new ScoreLine(
                    ScorePhase.Final,
                    ScoreLineKind.FinalMultiplier,
                    source,
                    0,
                    string.Empty,
                    null,
                    result.FinalMultiplier,
                    1f,
                    result.FinalMultiplier,
                    $"局级[strong]倍率[/strong] [multmul]×{FormatDecimal(result.FinalMultiplier)}[/multmul]"));
            }

            if (!hasGold && Mathf.Abs(result.GoldDelta) > 0.001f)
            {
                AppendSynthetic(plan, SyntheticSideEffect(
                    ScoreLineKind.Gold,
                    result.GoldDelta,
                    $"金币 {FormatSigned(result.GoldDelta)}"));
            }

            if (!hasLayer && result.HappyCakeLayerDelta != 0)
            {
                AppendSynthetic(plan, SyntheticSideEffect(
                    ScoreLineKind.Layer,
                    result.HappyCakeLayerDelta,
                    $"层数 {FormatSigned(result.HappyCakeLayerDelta)}"));
            }

        }

        private static ScoreLine SyntheticSideEffect(ScoreLineKind kind, float value, string message)
        {
            return new ScoreLine(
                ScorePhase.Final,
                kind,
                ScoreSource.FinalModifier("settlement_fallback", "结算"),
                0,
                string.Empty,
                null,
                value,
                0f,
                value,
                message);
        }

        private static void AppendSynthetic(SettlementPresentationPlan plan, ScoreLine line)
        {
            AppendLine(plan.EpilogueGroups, line);
        }

        private static string FormatSigned(BigDouble value)
        {
            return $"{(value >= 0f ? "+" : string.Empty)}{ScoreNumberFormatter.Format(value)}";
        }

        private static string FormatDecimal(BigDouble value)
        {
            return BigDouble.Abs(value) < ScoreNumberFormatter.ScientificThreshold
                ? value.ToString("G3")
                : ScoreNumberFormatter.Format(value);
        }
    }

    internal sealed class SettlementBaseBeat
    {
        public SettlementBaseBeat(int dishInstanceId, string dishId, ScoreLine line, BigDouble baseValue)
        {
            DishInstanceId = dishInstanceId;
            DishId = dishId ?? string.Empty;
            Line = line;
            BaseValue = baseValue;
        }

        public int DishInstanceId { get; }
        public string DishId { get; }
        public ScoreLine Line { get; }
        public BigDouble BaseValue { get; }
    }

    internal sealed class SettlementDishChapter
    {
        public SettlementDishChapter(SettlementBaseBeat baseBeat)
        {
            BaseBeat = baseBeat;
        }

        public SettlementBaseBeat BaseBeat { get; }
        public int DishInstanceId => BaseBeat?.DishInstanceId ?? 0;
        public string DishId => BaseBeat?.DishId ?? string.Empty;
        public List<SettlementEffectGroup> Groups { get; } = new();
    }

    internal sealed class SettlementEffectGroup
    {
        private readonly HashSet<int> _targetDishIds = new();

        public SettlementEffectGroup(ScoreLine first)
        {
            GroupId = first?.ExecutionGroupId ?? 0;
            Phase = first?.Phase ?? ScorePhase.Final;
            Source = first?.Source;
            Trace = first?.Trace;
            Append(first);
        }

        public int GroupId { get; }
        public ScorePhase Phase { get; }
        public ScoreSource Source { get; }
        public SkillExecutionTrace Trace { get; }
        public List<ScoreLine> Lines { get; } = new();
        public IReadOnlyCollection<int> TargetDishIds => _targetDishIds;

        public int ActorDishInstanceId
        {
            get
            {
                if (Trace?.Kind == SkillExecutionKind.SweetTransfer
                    && Trace.RuntimeSelfDishInstanceId > 0)
                {
                    return Trace.RuntimeSelfDishInstanceId;
                }

                if (Trace != null && Trace.OwnerDishInstanceId > 0)
                {
                    return Trace.OwnerDishInstanceId;
                }

                return Source?.DishInstanceId ?? 0;
            }
        }

        public string SourceName
        {
            get
            {
                if (Trace != null && !string.IsNullOrEmpty(Trace.SourceLabel))
                {
                    return Trace.SourceLabel;
                }

                if (!string.IsNullOrEmpty(Source?.Name))
                {
                    return Source.Name;
                }

                return "结算";
            }
        }

        public bool CanAppend(ScoreLine line)
        {
            if (line == null)
            {
                return false;
            }

            if (GroupId > 0 || line.ExecutionGroupId > 0)
            {
                return GroupId > 0 && GroupId == line.ExecutionGroupId;
            }

            return Phase == line.Phase
                && SameSource(Source, line.Source);
        }

        public void Append(ScoreLine line)
        {
            if (line == null)
            {
                return;
            }

            Lines.Add(line);
            if (line.DishInstanceId > 0)
            {
                _targetDishIds.Add(line.DishInstanceId);
            }

            if (line.Trace?.VisualTargetDishInstanceIds != null)
            {
                for (int i = 0; i < line.Trace.VisualTargetDishInstanceIds.Count; i++)
                {
                    int id = line.Trace.VisualTargetDishInstanceIds[i];
                    if (id > 0)
                    {
                        _targetDishIds.Add(id);
                    }
                }
            }
        }

        private static bool SameSource(ScoreSource a, ScoreSource b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            return a != null
                && b != null
                && a.Type == b.Type
                && a.DishInstanceId == b.DishInstanceId
                && string.Equals(a.Id, b.Id, StringComparison.Ordinal);
        }
    }

    internal sealed class SettlementRunningLedger
    {
        private sealed class DishState
        {
            public BigDouble Base;
            public BigDouble Flat;
            public BigDouble Multiplier = BigDouble.One;
            public bool HasBase;
        }

        private readonly Dictionary<int, DishState> _dishes = new();
        private BigDouble _finalFlat;
        private BigDouble _finalMultiplier = BigDouble.One;

        public SettlementRunningLedger(
            IReadOnlyList<DishScore> scores,
            SettlementBaselineSnapshot baselineSnapshot)
        {
            if (scores == null)
            {
                return;
            }

            for (int i = 0; i < scores.Count; i++)
            {
                DishScore score = scores[i];
                if (score == null)
                {
                    continue;
                }

                BigDouble multiplier = BigDouble.One;
                if (baselineSnapshot != null
                    && baselineSnapshot.TryGet(score.DishInstanceId, out SettlementDishBaseline baseline))
                {
                    multiplier = baseline.Multiplier;
                }

                _dishes[score.DishInstanceId] = new DishState { Multiplier = multiplier };
            }
        }

        public BigDouble CurrentTotal
        {
            get
            {
                BigDouble raw = BigDouble.Zero;
                foreach (DishState state in _dishes.Values)
                {
                    if (state.HasBase)
                    {
                        raw += DishScore.CeilContribution(state.Base + state.Flat, state.Multiplier);
                    }
                }

                return BigDouble.Round(
                    (raw + _finalFlat) * _finalMultiplier,
                    MidpointRounding.AwayFromZero);
            }
        }

        public BigDouble ApplyBase(int dishInstanceId, BigDouble baseValue)
        {
            DishState state = EnsureDish(dishInstanceId);
            state.Base = baseValue;
            state.HasBase = true;
            return ContributionFor(dishInstanceId);
        }

        public BigDouble Apply(ScoreLine line)
        {
            if (line == null)
            {
                return BigDouble.Zero;
            }

            DishState state;
            switch (line.Kind)
            {
                case ScoreLineKind.DishBase:
                    return ApplyBase(line.DishInstanceId, line.After);
                case ScoreLineKind.DishFlat:
                case ScoreLineKind.DishPermanentFlat:
                    state = EnsureDish(line.DishInstanceId);
                    // 结算演出会为了语义节奏重排部分明细（例如先播甜蜜传递结果、
                    // 再播跳跳糖响应）。Before/After 属于正式计算时的绝对快照，
                    // 重排后直接写 After 会用旧快照覆盖较新的状态，制造虚假扣分。
                    // 演出账本只消费这条明细在正式计算中实际产生的变化量。
                    state.Flat += line.After - line.Before;
                    break;
                case ScoreLineKind.DishMultiplier:
                case ScoreLineKind.DishMultiplierAdd:
                    state = EnsureDish(line.DishInstanceId);
                    state.Multiplier += line.After - line.Before;
                    break;
                case ScoreLineKind.FinalFlat:
                    _finalFlat += line.After - line.Before;
                    break;
                case ScoreLineKind.FinalMultiplier:
                    _finalMultiplier += line.After - line.Before;
                    break;
            }

            return ContributionFor(line.DishInstanceId);
        }

        public BigDouble ContributionFor(int dishInstanceId)
        {
            if (!_dishes.TryGetValue(dishInstanceId, out DishState state) || !state.HasBase)
            {
                return BigDouble.Zero;
            }

            return DishScore.CeilContribution(state.Base + state.Flat, state.Multiplier);
        }

        private DishState EnsureDish(int dishInstanceId)
        {
            if (!_dishes.TryGetValue(dishInstanceId, out DishState state))
            {
                state = new DishState();
                _dishes[dishInstanceId] = state;
            }

            return state;
        }
    }
}
