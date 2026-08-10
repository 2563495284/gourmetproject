using System;
using System.Collections.Generic;
using System.Text;
using BreakInfinity;
using GourmetProject.Game.Run;
using GourmetProject.Runtime;
using Hortor;
using UnityEngine;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.Analytics
{
    public enum AnalyticsConsentState
    {
        Unknown = 0,
        Granted = 1,
        Denied = 2,
    }

    public enum AnalyticsSelectionMode
    {
        Optional,
        Forced,
        Auto,
    }

    public static class GameAnalyticsService
    {
        public const string ConsentSettingKey = "Privacy.AnonymousAnalytics";
        private const string Tag = "Analytics";
        private const int SchemaVersion = 1;
        private const string TgaGameId = "yjcs_app_test";
        private const int MaxBusinessPropertyCount = 64;
        private const int MaxTextBytes = 2 * 1024;
        private const double MaxNumber = 9e15;

        private static bool _sdkInitialized;
        private static bool _suppressSdkCallsForTests;
        private static readonly HashSet<string> FirstEventKeys = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, ArchetypeVector> BattleArchetypeSnapshots =
            new Dictionary<string, ArchetypeVector>(StringComparer.Ordinal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            // Enter Play Mode Options may disable domain reload, so reset both service and HSDK queues.
            _sdkInitialized = false;
            _suppressSdkCallsForTests = false;
            FirstEventKeys.Clear();
            BattleArchetypeSnapshots.Clear();
            HSDK.ClearPendingGameLogs();
        }

        /// <summary>PlayMode 测试使用，避免授权流程测试向真实项目发送数据。</summary>
        public static void SetSdkSuppressedForTests(bool suppressed)
        {
            _suppressSdkCallsForTests = suppressed;
            _sdkInitialized = false;
        }

        public static AnalyticsConsentState ConsentState
        {
            get
            {
                int raw = GameApp.Settings?.GetInt(ConsentSettingKey, 0) ?? 0;
                return Enum.IsDefined(typeof(AnalyticsConsentState), raw)
                    ? (AnalyticsConsentState)raw
                    : AnalyticsConsentState.Unknown;
            }
        }

        public static bool IsEnabled => ConsentState == AnalyticsConsentState.Granted && _sdkInitialized;

        public static void ApplyStoredConsent()
        {
            if (ConsentState == AnalyticsConsentState.Granted)
            {
                EnsureSdkInitialized();
            }
        }

        public static void SetConsent(AnalyticsConsentState state)
        {
            if (state == AnalyticsConsentState.Unknown || GameApp.Settings == null)
            {
                return;
            }

            GameApp.Settings.SetInt(ConsentSettingKey, (int)state);
            GameApp.Settings.Save();
            if (state == AnalyticsConsentState.Granted)
            {
                EnsureSdkInitialized();
                return;
            }

            if (_sdkInitialized)
            {
                SafeSdkCall(HSDK.ClearPendingGameLogs, "clear pending logs");
                _sdkInitialized = false;
            }
        }

        public static void TrackRunStarted(GameRun run)
        {
            TrackFirst("run_started", Common(run), $"run_started:{run?.RunId}");
        }

        public static void TrackRunCheckpoint(GameRun run, int weekIndex, int dayBoundary)
        {
            Dictionary<string, object> properties = Common(run);
            properties["week_index"] = weekIndex;
            properties["day_boundary"] = dayBoundary;
            properties["day_value"] = dayBoundary;
            properties["day_bucket"] = dayBoundary;
            TrackFirst(
                "run_checkpoint_reached",
                properties,
                $"checkpoint:{run?.RunId}:{weekIndex}:{dayBoundary}");
        }

        public static void TrackCrossedDayCheckpoints(GameRun run, float previousDay)
        {
            if (run == null || run.CurrentDay <= previousDay)
            {
                return;
            }

            int firstBoundary = Math.Max(0, (int)Math.Floor(previousDay) + 1);
            int lastBoundary = Math.Max(0, (int)Math.Floor(run.CurrentDay + 0.0001f));
            for (int day = firstBoundary; day <= lastBoundary; day++)
            {
                TrackRunCheckpoint(run, run.WeekIndex, day);
            }
        }

        public static void TrackRunEnded(GameRun run, string reason, bool isDeath)
        {
            Dictionary<string, object> properties = Common(run);
            properties["end_reason"] = reason ?? string.Empty;
            properties["is_death"] = isDeath;
            TrackFirst("run_ended", properties, $"run_ended:{run?.RunId}:{reason}");
        }

        public static void TrackRunMilestone(GameRun run, BigDouble score, int targetScore)
        {
            Dictionary<string, object> properties = Common(run);
            AddScore(properties, score);
            properties["target_score"] = targetScore;
            TrackFirst("run_milestone_reached", properties, $"milestone:{run?.RunId}:{run?.WeekIndex}");
        }

        public static void TrackChoiceCandidate(
            GameRun run,
            bool selected,
            string offerId,
            string context,
            string contentType,
            string contentId,
            string baseId,
            int position,
            int candidateCount,
            int requiredPickCount,
            AnalyticsSelectionMode selectionMode,
            int revision = 0,
            ArchetypeVector? archetype = null)
        {
            Dictionary<string, object> properties = Common(run);
            if (archetype.HasValue)
            {
                AddArchetype(properties, archetype.Value);
            }
            properties["offer_id"] = offerId ?? string.Empty;
            properties["context"] = context ?? string.Empty;
            properties["content_type"] = contentType ?? string.Empty;
            properties["content_id"] = contentId ?? string.Empty;
            properties["base_id"] = baseId ?? string.Empty;
            properties["position"] = position;
            properties["candidate_count"] = candidateCount;
            properties["required_pick_count"] = requiredPickCount;
            properties["selection_mode"] = ToSnakeCase(selectionMode.ToString());
            properties["revision"] = revision;
            string eventName = selected ? "choice_candidate_selected" : "choice_candidate_shown";
            TrackFirst(
                eventName,
                properties,
                $"{eventName}:{run?.RunId}:{offerId}:{position}:{contentId}");
        }

        public static void TrackChoiceOfferResolved(
            GameRun run,
            string offerId,
            string context,
            int selectedCount,
            bool skipped,
            int rerollCount,
            long durationMs,
            ArchetypeVector? archetype = null)
        {
            Dictionary<string, object> properties = Common(run);
            if (archetype.HasValue)
            {
                AddArchetype(properties, archetype.Value);
            }
            properties["offer_id"] = offerId ?? string.Empty;
            properties["context"] = context ?? string.Empty;
            properties["selected_count"] = selectedCount;
            properties["skipped"] = skipped;
            properties["reroll_count"] = rerollCount;
            properties["duration_ms"] = Math.Max(0L, durationMs);
            TrackFirst("choice_offer_resolved", properties, $"choice_resolved:{run?.RunId}:{offerId}");
        }

        public static void TrackShopItemShown(
            GameRun run,
            string shopId,
            string category,
            string contentId,
            int slot,
            int price,
            bool affordable,
            int restockIndex)
        {
            Dictionary<string, object> properties = Common(run);
            properties["shop_id"] = shopId ?? string.Empty;
            properties["spend_category"] = category ?? string.Empty;
            properties["content_id"] = contentId ?? string.Empty;
            properties["slot"] = slot;
            properties["price"] = price;
            properties["affordable"] = affordable;
            properties["restock_index"] = restockIndex;
            TrackFirst("shop_item_shown", properties, $"shop_shown:{run?.RunId}:{shopId}:{category}:{slot}:{restockIndex}:{contentId}");
        }

        public static void TrackShopPurchase(
            GameRun run,
            string shopId,
            string category,
            string contentId,
            int goldSpent,
            int goldBefore,
            int goldAfter,
            ArchetypeVector? archetype = null)
        {
            Dictionary<string, object> properties = Common(run);
            if (archetype.HasValue)
            {
                AddArchetype(properties, archetype.Value);
            }
            properties["shop_id"] = shopId ?? string.Empty;
            properties["spend_category"] = category ?? string.Empty;
            properties["content_id"] = contentId ?? string.Empty;
            properties["gold_spent"] = goldSpent;
            properties["gold_before"] = goldBefore;
            properties["gold_after"] = goldAfter;
            Track("shop_purchase", properties);
        }

        public static void TrackBattleStarted(
            GameRun run,
            string battleId,
            bool isBoss,
            string bossId,
            int targetScore,
            int heartsBefore)
        {
            Dictionary<string, object> properties = Common(run);
            ArchetypeVector archetype = ArchetypeService.Capture(run);
            AddArchetype(properties, archetype);
            BattleArchetypeSnapshots[BattleSnapshotKey(run, battleId)] = archetype;
            properties["battle_id"] = battleId ?? string.Empty;
            properties["is_boss"] = isBoss;
            properties["boss_id"] = bossId ?? string.Empty;
            properties["target_score"] = targetScore;
            properties["hearts_before"] = heartsBefore;
            TrackFirst("battle_started", properties, $"battle_started:{run?.RunId}:{battleId}");
        }

        public static void TrackBattleSettled(
            GameRun run,
            string battleId,
            bool isBoss,
            string bossId,
            BigDouble score,
            bool targetHit,
            bool survived,
            bool terminalDeath,
            int heartsAfter)
        {
            Dictionary<string, object> properties = Common(run);
            string snapshotKey = BattleSnapshotKey(run, battleId);
            if (BattleArchetypeSnapshots.TryGetValue(snapshotKey, out ArchetypeVector archetype))
            {
                AddArchetype(properties, archetype);
                BattleArchetypeSnapshots.Remove(snapshotKey);
            }
            properties["battle_id"] = battleId ?? string.Empty;
            properties["is_boss"] = isBoss;
            properties["boss_id"] = bossId ?? string.Empty;
            properties["target_hit"] = targetHit;
            properties["survived"] = survived;
            properties["terminal_death"] = terminalDeath;
            properties["hearts_after"] = heartsAfter;
            AddScore(properties, score);
            TrackFirst("battle_settled", properties, $"battle_settled:{run?.RunId}:{battleId}");
        }

        private static string BattleSnapshotKey(GameRun run, string battleId)
        {
            return $"{run?.RunId}:{battleId}";
        }

        public static void TrackActiveItemUsed(GameRun run, string itemId, string useContext)
        {
            Dictionary<string, object> properties = Common(run);
            properties["item_id"] = itemId ?? string.Empty;
            properties["use_context"] = useContext ?? string.Empty;
            Track("active_item_used", properties);
        }

        private static void EnsureSdkInitialized()
        {
            if (_sdkInitialized)
            {
                return;
            }

            if (_suppressSdkCallsForTests)
            {
                _sdkInitialized = true;
                return;
            }

            try
            {
                HSDK.Init(new InitOption
                {
                    gameId = TgaGameId,
                    gameVersion = Application.version ?? string.Empty,
                    env = ENV.Test,
                });
                _sdkInitialized = true;
                Log.Info("HSDK TGA anonymous analytics enabled after consent.", Tag);
            }
            catch (Exception exception)
            {
                _sdkInitialized = false;
                Log.Warning($"HSDK TGA initialization failed: {exception.Message}", Tag);
            }
        }

        private static Dictionary<string, object> Common(GameRun run)
        {
            var properties = new Dictionary<string, object>
            {
                ["schema_version"] = SchemaVersion,
                ["app_version"] = Application.version ?? string.Empty,
                ["build_guid"] = Application.buildGUID ?? string.Empty,
                ["platform"] = Application.platform.ToString(),
                ["channel"] = "default",
                ["build_type"] = Debug.isDebugBuild ? "debug" : "release",
            };
            if (run == null)
            {
                return properties;
            }

            properties["run_id"] = run.RunId ?? string.Empty;
            properties["character_id"] = run.CharacterId ?? string.Empty;
            properties["is_tutorial"] = run.IsTutorialRun;
            properties["week_index"] = run.WeekIndex;
            properties["day_value"] = (double)run.CurrentDay;
            properties["day_bucket"] = Math.Max(0, (int)Math.Floor(run.CurrentDay));
            AddArchetype(properties, ArchetypeService.Capture(run));
            return properties;
        }

        private static void AddArchetype(Dictionary<string, object> properties, ArchetypeVector vector)
        {
            properties["archetype_id"] = vector.Id;
            properties["archetype_0_share"] = vector.Share0;
            properties["archetype_1_share"] = vector.Share1;
            properties["archetype_2_share"] = vector.Share2;
            properties["archetype_lead"] = vector.Lead;
        }

        private static void AddScore(Dictionary<string, object> properties, BigDouble score)
        {
            properties["score_mantissa"] = score.Mantissa;
            properties["score_exponent"] = score.Exponent;
            properties["score_log10"] = score <= BigDouble.Zero ? 0d : BigDouble.Log10(score);
            properties["score_text"] = score.ToString();
        }

        private static void Track(string eventName, Dictionary<string, object> properties)
        {
            if (!IsEnabled)
            {
                return;
            }

            SafeSdkCall(() =>
            {
                HSDK.PostGameLog(new PostGameLogOption
                {
                    eventName = eventName,
                    eventType = Hortor.EventType.Track,
                    customData = NormalizeProperties(properties),
                });
            }, eventName);
        }

        private static void TrackFirst(string eventName, Dictionary<string, object> properties, string uniqueKey)
        {
            if (!IsEnabled)
            {
                return;
            }

            string checkId = Hash128.Compute(uniqueKey ?? string.Empty).ToString();
            if (!FirstEventKeys.Add(checkId))
            {
                return;
            }

            properties["event_unique_id"] = checkId;
            Track(eventName, properties);
        }

        private static Dictionary<string, object> NormalizeProperties(Dictionary<string, object> properties)
        {
            if (properties == null)
            {
                return new Dictionary<string, object>();
            }

            if (properties.Count > MaxBusinessPropertyCount)
            {
                throw new InvalidOperationException(
                    $"TGA event has {properties.Count} business properties; maximum is {MaxBusinessPropertyCount}.");
            }

            var normalized = new Dictionary<string, object>(properties.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> pair in properties)
            {
                normalized[pair.Key] = NormalizeValue(pair.Value);
            }
            return normalized;
        }

        private static object NormalizeValue(object value)
        {
            switch (value)
            {
                case null:
                    return string.Empty;
                case string text:
                    return TruncateUtf8(text, MaxTextBytes);
                case double number:
                    return NormalizeNumber(number);
                case float number:
                    return (float)NormalizeNumber(number);
                case decimal number:
                    return Math.Max((decimal)-MaxNumber, Math.Min((decimal)MaxNumber, number));
                case long number:
                    return Math.Max((long)-MaxNumber, Math.Min((long)MaxNumber, number));
                case ulong number:
                    return Math.Min((ulong)MaxNumber, number);
                default:
                    return value;
            }
        }

        private static double NormalizeNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0d;
            }
            return Math.Max(-MaxNumber, Math.Min(MaxNumber, value));
        }

        private static string TruncateUtf8(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value) || Encoding.UTF8.GetByteCount(value) <= maxBytes)
            {
                return value ?? string.Empty;
            }

            int charCount = value.Length;
            while (charCount > 0 && Encoding.UTF8.GetByteCount(value, 0, charCount) > maxBytes)
            {
                charCount--;
            }
            if (charCount > 0 && char.IsHighSurrogate(value[charCount - 1]))
            {
                charCount--;
            }
            return value.Substring(0, charCount);
        }

        private static void SafeSdkCall(Action action, string operation)
        {
            if (_suppressSdkCallsForTests)
            {
                return;
            }

            try
            {
                action?.Invoke();
            }
            catch (Exception exception)
            {
                Log.Warning($"HSDK TGA '{operation}' failed: {exception.Message}", Tag);
            }
        }

        private static string ToSnakeCase(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.ToLowerInvariant();
        }
    }
}
