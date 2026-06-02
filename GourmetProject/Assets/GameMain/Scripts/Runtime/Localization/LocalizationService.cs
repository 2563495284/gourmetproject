using System;
using UnityGameFramework.Runtime;

namespace GourmetProject.Runtime.Localization
{
    /// <summary>
    /// 本地化封装（开箱即用）。透传 GameFramework LocalizationComponent 的取词，
    /// 文本字典由本地化字典资源提供。与玩法无关。
    /// </summary>
    public sealed class LocalizationService
    {
        private readonly LocalizationComponent _localization;

        public LocalizationService(LocalizationComponent localization)
        {
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        }

        public string Get(string key)
        {
            return _localization.GetString(key);
        }

        public string Get(string key, params object[] args)
        {
            return _localization.GetString(key, args);
        }

        public bool HasKey(string key)
        {
            return _localization.HasRawString(key);
        }
    }
}
