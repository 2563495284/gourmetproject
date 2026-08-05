using System;
using GourmetProject.Core.Diagnostics;
using GourmetProject.Runtime.Audio;
using UnityEngine;
using UnityGameFramework.Runtime;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Runtime.Settings
{
    /// <summary>
    /// 全局设置服务。封装 GameFramework SettingComponent，提供强类型的常用设置入口
    /// （音量、语言、画质等），与单局存档完全分离。所有写入在 <see cref="Save"/> 时落盘。
    /// 这里只放与玩法无关的通用偏好，玩法相关设置由玩法层另行扩展。
    /// </summary>
    public sealed class SettingsService
    {
        private const string Tag = "Settings";
        private const int DefaultResolutionWidth = 1920;
        private const int DefaultResolutionHeight = 1080;

        private readonly SettingComponent _setting;
        private readonly SoundComponent _sound;

        // 通用设置键。
        public const string KeyMasterVolume = "Audio.MasterVolume";
        public const string KeyMusicVolume = "Audio.MusicVolume";
        public const string KeySoundVolume = "Audio.SoundVolume";
        public const string KeyLanguage = "App.Language";
        public const string KeyMuted = "Audio.Muted";

        // 画面显示偏好键（与玩法无关的通用偏好）。
        public const string KeyResolutionWidth = "Display.ResolutionWidth";
        public const string KeyResolutionHeight = "Display.ResolutionHeight";
        public const string KeyRefreshRate = "Display.RefreshRate";
        public const string KeyFullScreenMode = "Display.FullScreenMode";
        public const string KeyVSync = "Display.VSync";
        public const string KeyTargetFrameRate = "Display.TargetFrameRate";

        // 游戏演出偏好键。
        public const string KeySettlementAcceleration = "Gameplay.SettlementAcceleration";

        public SettingsService(SettingComponent setting, SoundComponent sound)
        {
            _setting = setting ?? throw new ArgumentNullException(nameof(setting));
            _sound = sound ?? throw new ArgumentNullException(nameof(sound));
        }

        public float MasterVolume
        {
            get => _setting.GetFloat(KeyMasterVolume, 1f);
            set => _setting.SetFloat(KeyMasterVolume, Clamp01(value));
        }

        public float MusicVolume
        {
            get => _setting.GetFloat(KeyMusicVolume, 1f);
            set => _setting.SetFloat(KeyMusicVolume, Clamp01(value));
        }

        public float SoundVolume
        {
            get => _setting.GetFloat(KeySoundVolume, 1f);
            set => _setting.SetFloat(KeySoundVolume, Clamp01(value));
        }

        public bool Muted
        {
            get => _setting.GetBool(KeyMuted, false);
            set => _setting.SetBool(KeyMuted, value);
        }

        public string Language
        {
            get => _setting.GetString(KeyLanguage, string.Empty);
            set => _setting.SetString(KeyLanguage, value ?? string.Empty);
        }

        // —— 画面显示偏好。首次启动固定使用项目的 1920×1080 设计分辨率。——

        public int ResolutionWidth
        {
            get => _setting.GetInt(KeyResolutionWidth, DefaultResolutionWidth);
            set => _setting.SetInt(KeyResolutionWidth, value);
        }

        public int ResolutionHeight
        {
            get => _setting.GetInt(KeyResolutionHeight, DefaultResolutionHeight);
            set => _setting.SetInt(KeyResolutionHeight, value);
        }

        /// <summary>目标刷新率（Hz）。0 表示沿用当前显示器刷新率。</summary>
        public int RefreshRate
        {
            get => _setting.GetInt(KeyRefreshRate, Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value));
            set => _setting.SetInt(KeyRefreshRate, value);
        }

        public FullScreenMode FullScreenMode
        {
            get => (FullScreenMode)_setting.GetInt(KeyFullScreenMode, (int)Screen.fullScreenMode);
            set => _setting.SetInt(KeyFullScreenMode, (int)value);
        }

        public bool VSync
        {
            get => _setting.GetBool(KeyVSync, QualitySettings.vSyncCount > 0);
            set => _setting.SetBool(KeyVSync, value);
        }

        /// <summary>目标帧率。-1 表示不限制（由平台/垂直同步决定）。</summary>
        public int TargetFrameRate
        {
            get => _setting.GetInt(KeyTargetFrameRate, -1);
            set => _setting.SetInt(KeyTargetFrameRate, value);
        }

        /// <summary>结算演出是否按 cue 进度逐步加速。</summary>
        public bool SettlementAcceleration
        {
            get => _setting.GetBool(KeySettlementAcceleration, false);
            set => _setting.SetBool(KeySettlementAcceleration, value);
        }

        /// <summary>
        /// 把当前画面偏好应用到引擎（分辨率/全屏模式/垂直同步/目标帧率）。
        /// 不负责落盘，落盘请另行调用 <see cref="Save"/>。
        /// </summary>
        public void ApplyDisplaySettings()
        {
            int width = ResolutionWidth;
            int height = ResolutionHeight;
            int refreshRate = RefreshRate;
            FullScreenMode mode = FullScreenMode;

            if (width > 0 && height > 0)
            {
                if (refreshRate > 0)
                {
                    Screen.SetResolution(width, height, mode, new RefreshRate { numerator = (uint)refreshRate, denominator = 1u });
                }
                else
                {
                    Screen.SetResolution(width, height, mode);
                }
            }
            else
            {
                Screen.fullScreenMode = mode;
            }

            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = TargetFrameRate;

            Log.Debug(
                $"Display applied: {width}x{height}@{refreshRate} mode={mode} vsync={VSync} fps={TargetFrameRate}.",
                Tag);
        }

        /// <summary>把当前音量和静音偏好应用到 GameFramework 声音组。</summary>
        public void ApplyAudioSettings()
        {
            float master = Muted ? 0f : MasterVolume;
            var music = _sound.GetSoundGroup(AudioService.GroupMusic);
            if (music != null)
            {
                music.Volume = master * MusicVolume;
            }

            var sound = _sound.GetSoundGroup(AudioService.GroupSound);
            if (sound != null)
            {
                sound.Volume = master * SoundVolume;
            }
        }

        /// <summary>把所有可在运行时即时生效的设置统一应用一次。</summary>
        public void ApplyAll()
        {
            ApplyDisplaySettings();
            ApplyAudioSettings();
        }

        // 透传访问，便于玩法层存放自定义偏好而不必再包一层。
        public int GetInt(string key, int defaultValue = 0) => _setting.GetInt(key, defaultValue);
        public void SetInt(string key, int value) => _setting.SetInt(key, value);
        public float GetFloat(string key, float defaultValue = 0f) => _setting.GetFloat(key, defaultValue);
        public void SetFloat(string key, float value) => _setting.SetFloat(key, value);
        public bool GetBool(string key, bool defaultValue = false) => _setting.GetBool(key, defaultValue);
        public void SetBool(string key, bool value) => _setting.SetBool(key, value);
        public string GetString(string key, string defaultValue = "") => _setting.GetString(key, defaultValue);
        public void SetString(string key, string value) => _setting.SetString(key, value ?? string.Empty);
        public bool Has(string key) => _setting.HasSetting(key);
        public void Remove(string key) => _setting.RemoveSetting(key);

        /// <summary>把当前所有设置落盘。</summary>
        public void Save()
        {
            _setting.Save();
            Log.Debug("Settings saved.", Tag);
        }

        private static float Clamp01(float v)
        {
            if (v < 0f)
            {
                return 0f;
            }

            return v > 1f ? 1f : v;
        }
    }
}
