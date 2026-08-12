using System.Collections.Generic;
using GourmetProject.Game.Analytics;
using GourmetProject.Runtime;
using GourmetProject.Runtime.Settings;
using UnityEngine;

namespace GourmetProject.Game.Settings
{
    /// <summary>
    /// 设置项目录：集中声明界面要展示的全部设置项。这是设置系统的“可扩展入口”——
    /// 想加新设置（例如经营挑战倍速）只需在 <see cref="BuildDefault"/> 里再 Add 一个描述符，
    /// UI 会自动按其 <see cref="SettingControlType"/> 渲染对应控件，无需改动 SettingsForm。
    /// </summary>
    public static class SettingsCatalog
    {
        // 全屏模式候选（顺序即下拉项顺序）。
        private static readonly FullScreenMode[] FullScreenModes =
        {
            FullScreenMode.FullScreenWindow,
            FullScreenMode.ExclusiveFullScreen,
            FullScreenMode.Windowed,
        };

        private static readonly string[] FullScreenLabels =
        {
            "全屏（窗口）",
            "全屏（独占）",
            "窗口",
        };

        // 目标帧率候选；-1 表示不限制。
        private static readonly int[] FrameRateOptions = { -1, 30, 60, 120, 144 };
        private static readonly string[] FrameRateLabels = { "不限制", "30", "60", "120", "144" };

        private static List<Vector2Int> _resolutionCache;

        /// <summary>构建默认设置项列表。</summary>
        public static List<SettingDescriptor> BuildDefault()
        {
            var settings = GameApp.Settings;
            var list = new List<SettingDescriptor>();

            // —— 画面：分辨率 ——
            var resolutions = GetResolutions();
            list.Add(new SettingDescriptor
            {
                Id = "Display.Resolution",
                Label = "分辨率",
                ControlType = SettingControlType.Dropdown,
                GetOptions = () =>
                {
                    var opts = new List<string>(resolutions.Count);
                    foreach (var r in resolutions)
                    {
                        opts.Add($"{r.x} x {r.y}");
                    }

                    return opts;
                },
                GetSelectedIndex = () =>
                {
                    int w = settings.ResolutionWidth;
                    int h = settings.ResolutionHeight;
                    for (int i = 0; i < resolutions.Count; i++)
                    {
                        if (resolutions[i].x == w && resolutions[i].y == h)
                        {
                            return i;
                        }
                    }

                    return resolutions.Count - 1;
                },
                SetSelectedIndex = i =>
                {
                    if (i < 0 || i >= resolutions.Count)
                    {
                        return;
                    }

                    settings.ResolutionWidth = resolutions[i].x;
                    settings.ResolutionHeight = resolutions[i].y;
                },
            });

            // —— 画面：全屏模式 ——
            list.Add(new SettingDescriptor
            {
                Id = "Display.FullScreenMode",
                Label = "显示模式",
                ControlType = SettingControlType.Dropdown,
                GetOptions = () => new List<string>(FullScreenLabels),
                GetSelectedIndex = () =>
                {
                    var mode = settings.FullScreenMode;
                    for (int i = 0; i < FullScreenModes.Length; i++)
                    {
                        if (FullScreenModes[i] == mode)
                        {
                            return i;
                        }
                    }

                    return 0;
                },
                SetSelectedIndex = i =>
                {
                    if (i >= 0 && i < FullScreenModes.Length)
                    {
                        settings.FullScreenMode = FullScreenModes[i];
                    }
                },
            });

            // // —— 画面：垂直同步 ——
            // list.Add(new SettingDescriptor
            // {
            //     Id = "Display.VSync",
            //     Label = "垂直同步",
            //     ControlType = SettingControlType.Toggle,
            //     GetToggleValue = () => settings.VSync,
            //     SetToggleValue = v => settings.VSync = v,
            // });

            // —— 画面：目标帧率 ——
            list.Add(new SettingDescriptor
            {
                Id = "Display.TargetFrameRate",
                Label = "目标帧率",
                ControlType = SettingControlType.Dropdown,
                GetOptions = () => new List<string>(FrameRateLabels),
                GetSelectedIndex = () =>
                {
                    int fps = settings.TargetFrameRate;
                    for (int i = 0; i < FrameRateOptions.Length; i++)
                    {
                        if (FrameRateOptions[i] == fps)
                        {
                            return i;
                        }
                    }

                    return 0;
                },
                SetSelectedIndex = i =>
                {
                    if (i >= 0 && i < FrameRateOptions.Length)
                    {
                        settings.TargetFrameRate = FrameRateOptions[i];
                    }
                },
            });

            // —— 音频：音量 ——
            list.Add(BuildVolume("Audio.MasterVolume", "主音量", () => settings.MasterVolume, v => settings.MasterVolume = v));
            list.Add(BuildVolume("Audio.MusicVolume", "音乐音量", () => settings.MusicVolume, v => settings.MusicVolume = v));
            list.Add(BuildVolume("Audio.SoundVolume", "音效音量", () => settings.SoundVolume, v => settings.SoundVolume = v));

            // —— 音频：静音 ——
            list.Add(new SettingDescriptor
            {
                Id = "Audio.Muted",
                Label = "静音",
                ControlType = SettingControlType.Toggle,
                GetToggleValue = () => settings.Muted,
                SetToggleValue = v => settings.Muted = v,
            });

            // —— 语言 ——
            var languageCodes = new[] { string.Empty, "ChineseSimplified", "English" };
            var languageLabels = new[] { "跟随系统", "简体中文", "English" };
            list.Add(new SettingDescriptor
            {
                Id = "App.Language",
                Label = "语言",
                ControlType = SettingControlType.Dropdown,
                GetOptions = () => new List<string>(languageLabels),
                GetSelectedIndex = () =>
                {
                    string cur = settings.Language;
                    for (int i = 0; i < languageCodes.Length; i++)
                    {
                        if (languageCodes[i] == cur)
                        {
                            return i;
                        }
                    }

                    return 0;
                },
                SetSelectedIndex = i =>
                {
                    if (i >= 0 && i < languageCodes.Length)
                    {
                        settings.Language = languageCodes[i];
                    }
                },
            });

            // —— 游戏：结算演出 ——
            list.Add(new SettingDescriptor
            {
                Id = SettingsService.KeySettlementAcceleration,
                Label = "结算双倍速",
                ControlType = SettingControlType.Toggle,
                GetToggleValue = () => settings.SettlementDoubleSpeed,
                SetToggleValue = v => settings.SettlementDoubleSpeed = v,
            });

            // —— 游戏：拖放后需确认上菜 ——
            list.Add(new SettingDescriptor
            {
                Id = SettingsService.KeyRequireServeConfirmation,
                Label = "需确认上菜",
                ControlType = SettingControlType.Toggle,
                GetToggleValue = () => settings.RequireServeConfirmation,
                SetToggleValue = v => settings.RequireServeConfirmation = v,
            });

            // —— 隐私：匿名数据统计 ——
            list.Add(new SettingDescriptor
            {
                Id = GameAnalyticsService.ConsentSettingKey,
                Label = "匿名数据统计",
                ControlType = SettingControlType.Toggle,
                GetToggleValue = () =>
                    GameAnalyticsService.ConsentState == AnalyticsConsentState.Granted,
                SetToggleValue = value => GameAnalyticsService.SetConsent(
                    value ? AnalyticsConsentState.Granted : AnalyticsConsentState.Denied),
            });

            // —— 扩展示例（默认注释关闭）——
            // 想给游戏加“经营挑战倍速”，把下面这段取消注释即可，无需改 SettingsForm：
            //
            // list.Add(new SettingDescriptor
            // {
            //     Id = "Gameplay.Speed",
            //     Label = "经营挑战倍速",
            //     ControlType = SettingControlType.Slider,
            //     SliderMin = 1f,
            //     SliderMax = 4f,
            //     SliderWholeNumbers = true,
            //     GetSliderValue = () => settings.GetFloat("Gameplay.Speed", 1f),
            //     SetSliderValue = v => settings.SetFloat("Gameplay.Speed", v),
            //     FormatSliderValue = v => $"{v:0}x",
            // });

            return list;
        }

        private static SettingDescriptor BuildVolume(string id, string label, System.Func<float> get, System.Action<float> set)
        {
            return new SettingDescriptor
            {
                Id = id,
                Label = label,
                ControlType = SettingControlType.Slider,
                SliderMin = 0f,
                SliderMax = 1f,
                SliderWholeNumbers = false,
                GetSliderValue = get,
                SetSliderValue = set,
                FormatSliderValue = v => $"{Mathf.RoundToInt(v * 100f)}%",
            };
        }

        /// <summary>可选分辨率列表（按面积升序去重）。</summary>
        private static List<Vector2Int> GetResolutions()
        {
            if (_resolutionCache != null)
            {
                return _resolutionCache;
            }

            var seen = new HashSet<long>();
            var list = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                long key = ((long)r.width << 32) | (uint)r.height;
                if (seen.Add(key))
                {
                    list.Add(new Vector2Int(r.width, r.height));
                }
            }

            // 极端情况下（部分平台）拿不到列表，至少塞入当前分辨率兜底。
            if (list.Count == 0)
            {
                list.Add(new Vector2Int(Screen.width, Screen.height));
            }

            list.Sort((a, b) => (a.x * a.y).CompareTo(b.x * b.y));
            _resolutionCache = list;
            return list;
        }
    }
}
