using System;
using GourmetProject.Game.Meta;
using UnityEngine;

namespace GourmetProject.Game.UI.Hud
{
    public static class TimelineAxisIconKeys
    {
        private const string BossPrefix = "boss:";
        public const string RestoreHeart = "restore_heart";

        public static string ForAction(string actionId, ActionDisplayKind fallbackKind)
        {
            return string.Equals(actionId, "act_restore_heart", StringComparison.Ordinal)
                ? RestoreHeart
                : ForKind(fallbackKind);
        }

        public static string ForKind(ActionDisplayKind kind)
        {
            return kind switch
            {
                ActionDisplayKind.Shop => "shop",
                ActionDisplayKind.Interest => "interest",
                ActionDisplayKind.Boss => "boss",
                ActionDisplayKind.Event => "event",
                ActionDisplayKind.Reward => "reward",
                ActionDisplayKind.Slot => "slot",
                ActionDisplayKind.Negative => "negative",
                _ => string.Empty,
            };
        }

        public static string Boss(string debuffId)
        {
            if (string.IsNullOrEmpty(debuffId))
            {
                return "boss";
            }

            const string prefix = "debuff_";
            string key = debuffId.StartsWith(prefix, StringComparison.Ordinal)
                ? debuffId.Substring(prefix.Length)
                : debuffId;
            return BossPrefix + key;
        }

        public static bool TryGetBossId(string key, out string debuffId)
        {
            debuffId = string.Empty;
            if (string.IsNullOrEmpty(key)
                || !key.StartsWith(BossPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            debuffId = key.Substring(BossPrefix.Length);
            return !string.IsNullOrEmpty(debuffId);
        }
    }

    [Serializable]
    public sealed class TimelineAxisBossIcon
    {
        public string DebuffId = string.Empty;
        public Sprite Sprite;
    }

    [Serializable]
    public sealed class TimelineAxisPalette
    {
        public Color Cream = new Color32(255, 244, 214, 255);
        public Color Sage = new Color32(139, 191, 122, 255);
        public Color DeepSage = new Color32(78, 118, 90, 255);
        public Color Apricot = new Color32(243, 163, 92, 255);
        public Color Ink = new Color32(91, 57, 38, 255);
        public Color Danger = new Color32(232, 111, 97, 255);
        public Color Preview = new Color32(142, 216, 182, 255);
        public Color Completed = new Color32(166, 170, 153, 210);
        public Color Future = new Color32(91, 57, 38, 90);
        public Color ExecutingGlow = new Color32(255, 194, 92, 220);
    }

    [Serializable]
    public sealed class TimelineAxisMotion
    {
        [Min(0.01f)] public float LayoutDuration = 0.20f;
        [Min(0.01f)] public float EnterDuration = 0.16f;
        [Min(0.01f)] public float ExitDuration = 0.18f;
        [Min(0.01f)] public float ChangeHalfDuration = 0.10f;
        [Min(0.01f)] public float AdvanceBaseDuration = 0.76f;
        [Min(0f)] public float AdvancePerDayDuration = 0.36f;
        [Min(0.01f)] public float AdvanceMaximumDuration = 2.20f;
        [Min(0.01f)] public float PulseDuration = 0.30f;
        [Min(0.01f)] public float ResizeDuration = 0.38f;
    }

    [CreateAssetMenu(
        fileName = "TimelineAxisTheme",
        menuName = "Gourmet Project/UI/Timeline Axis Theme")]
    public sealed class TimelineAxisTheme : ScriptableObject
    {
        [Header("Chrome")]
        [SerializeField] private Sprite _panel;
        [SerializeField] private Sprite _track;
        [SerializeField] private Sprite _progress;
        [SerializeField] private Sprite _tick;
        [SerializeField] private Sprite _cursor;
        [SerializeField] private Sprite _dayBadge;
        [SerializeField] private Sprite _nodeBubble;
        [SerializeField] private Sprite _bossNodeBubble;

        [Header("Node icons")]
        [SerializeField] private Sprite _shop;
        [SerializeField] private Sprite _interest;
        [SerializeField] private Sprite _boss;
        [SerializeField] private Sprite _event;
        [SerializeField] private Sprite _restoreHeart;
        [SerializeField] private Sprite _reward;
        [SerializeField] private Sprite _slot;
        [SerializeField] private Sprite _negative;
        [SerializeField] private TimelineAxisBossIcon[] _bossIcons = Array.Empty<TimelineAxisBossIcon>();

        [Header("Style")]
        [SerializeField] private TimelineAxisPalette _palette = new TimelineAxisPalette();
        [SerializeField] private TimelineAxisMotion _motion = new TimelineAxisMotion();

        public Sprite Panel => _panel;
        public Sprite Track => _track;
        public Sprite Progress => _progress;
        public Sprite Tick => _tick;
        public Sprite Cursor => _cursor;
        public Sprite DayBadge => _dayBadge;
        public Sprite NodeBubble => _nodeBubble;
        public Sprite BossNodeBubble => _bossNodeBubble != null ? _bossNodeBubble : _nodeBubble;
        public TimelineAxisPalette Palette => _palette ??= new TimelineAxisPalette();
        public TimelineAxisMotion Motion => _motion ??= new TimelineAxisMotion();

        public Sprite ResolveIcon(string iconKey, ActionDisplayKind fallbackKind)
        {
            if (string.Equals(iconKey, TimelineAxisIconKeys.RestoreHeart, StringComparison.Ordinal))
            {
                return _restoreHeart != null ? _restoreHeart : _event;
            }

            if (TimelineAxisIconKeys.TryGetBossId(iconKey, out string debuffId))
            {
                for (int i = 0; i < (_bossIcons?.Length ?? 0); i++)
                {
                    TimelineAxisBossIcon entry = _bossIcons[i];
                    if (entry != null
                        && entry.Sprite != null
                        && string.Equals(entry.DebuffId, debuffId, StringComparison.Ordinal))
                    {
                        return entry.Sprite;
                    }
                }

                return _boss;
            }

            return (string.IsNullOrEmpty(iconKey)
                    ? fallbackKind
                    : KindForKey(iconKey, fallbackKind)) switch
            {
                ActionDisplayKind.Shop => _shop,
                ActionDisplayKind.Interest => _interest,
                ActionDisplayKind.Boss => _boss,
                ActionDisplayKind.Event => _event,
                ActionDisplayKind.Reward => _reward,
                ActionDisplayKind.Slot => _slot,
                ActionDisplayKind.Negative => _negative,
                _ => null,
            };
        }

        private static ActionDisplayKind KindForKey(string iconKey, ActionDisplayKind fallback)
        {
            return iconKey switch
            {
                "shop" => ActionDisplayKind.Shop,
                "interest" => ActionDisplayKind.Interest,
                "boss" => ActionDisplayKind.Boss,
                "event" => ActionDisplayKind.Event,
                "reward" => ActionDisplayKind.Reward,
                "slot" => ActionDisplayKind.Slot,
                "negative" => ActionDisplayKind.Negative,
                _ => fallback,
            };
        }
    }
}
