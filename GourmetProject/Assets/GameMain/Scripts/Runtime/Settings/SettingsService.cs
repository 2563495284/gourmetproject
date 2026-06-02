using System;
using GourmetProject.Core.Diagnostics;
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

        private readonly SettingComponent _setting;

        // 通用设置键。
        public const string KeyMasterVolume = "Audio.MasterVolume";
        public const string KeyMusicVolume = "Audio.MusicVolume";
        public const string KeySoundVolume = "Audio.SoundVolume";
        public const string KeyLanguage = "App.Language";
        public const string KeyMuted = "Audio.Muted";

        public SettingsService(SettingComponent setting)
        {
            _setting = setting ?? throw new ArgumentNullException(nameof(setting));
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
