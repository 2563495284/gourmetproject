using GourmetProject.Core.Diagnostics;
using GourmetProject.Core.Rng;
using GourmetProject.Core.Save;
using GourmetProject.Core.Utility;
using GourmetProject.Runtime.Audio;
using GourmetProject.Runtime.Diagnostics;
using GourmetProject.Runtime.Localization;
using GourmetProject.Runtime.Resource;
using GourmetProject.Runtime.Scene;
using GourmetProject.Runtime.Settings;
using UnityEngine;
using GFEntry = UnityGameFramework.Runtime.GameEntry;
using UnityGameFramework.Runtime;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Runtime
{
    /// <summary>
    /// 项目级总入口。聚合 GameFramework 内置组件与本项目自建的、与玩法无关的基础服务
    /// （随机、存档、设置、配置）。玩法层只通过这里取用基础设施。
    /// </summary>
    public static class GameApp
    {
        public const string Tag = "GameApp";

        private static bool _servicesInitialized;

        /// <summary>确定性随机系统。</summary>
        public static RandomService Random { get; private set; }

        /// <summary>通用存档服务。</summary>
        public static ISaveService Save { get; private set; }

        /// <summary>全局设置服务（封装 GameFramework SettingComponent）。</summary>
        public static SettingsService Settings { get; private set; }

        /// <summary>Luban 配置服务。</summary>
        public static global::GourmetProject.Config.ConfigService Config { get; private set; }

        // —— 内置模块的开箱即用封装（惰性创建，绑定到对应 GameFramework 组件）——
        private static ResourceLoader _assets;
        private static AudioService _audio;
        private static SceneLoader _scenes;
        private static LocalizationService _l10n;

        /// <summary>资源加载封装（回调式异步加载）。</summary>
        public static ResourceLoader Assets => _assets ??= new ResourceLoader(Resource);

        /// <summary>音频封装（背景音乐 / 音效）。</summary>
        public static AudioService Audio => _audio ??= new AudioService(Sound);

        /// <summary>场景转场封装。</summary>
        public static SceneLoader Scenes => _scenes ??= new SceneLoader(Scene);

        /// <summary>本地化取词封装。</summary>
        public static LocalizationService L10n => _l10n ??= new LocalizationService(Localization);

        // —— GameFramework 内置组件快捷访问 ——
        public static BaseComponent Base => GFEntry.GetComponent<BaseComponent>();
        public static EventComponent Event => GFEntry.GetComponent<EventComponent>();
        public static FsmComponent Fsm => GFEntry.GetComponent<FsmComponent>();
        public static ProcedureComponent Procedure => GFEntry.GetComponent<ProcedureComponent>();
        public static ResourceComponent Resource => GFEntry.GetComponent<ResourceComponent>();
        public static ObjectPoolComponent ObjectPool => GFEntry.GetComponent<ObjectPoolComponent>();
        public static UIComponent UI => GFEntry.GetComponent<UIComponent>();
        public static SoundComponent Sound => GFEntry.GetComponent<SoundComponent>();
        public static SceneComponent Scene => GFEntry.GetComponent<SceneComponent>();
        public static LocalizationComponent Localization => GFEntry.GetComponent<LocalizationComponent>();
        public static SettingComponent Setting => GFEntry.GetComponent<SettingComponent>();

        /// <summary>
        /// 初始化与玩法无关的基础服务。幂等：重复调用只生效一次。
        /// 由 ProcedureLaunch 在 GameFramework 启动后调用。
        /// </summary>
        public static void InitializeServices()
        {
            if (_servicesInitialized)
            {
                return;
            }

            Log.SetSink(new UnityLogSink());
            Log.MinLevel = Debug.isDebugBuild ? LogLevel.Debug : LogLevel.Info;

            Random = new RandomService();

            var saveOptions = new SaveServiceOptions
            {
                CurrentVersion = 1,
                EnableChecksum = true,
                Indented = Debug.isDebugBuild,
            };
            Save = new JsonSaveService(System.IO.Path.Combine(Application.persistentDataPath, "saves"), saveOptions);

            Settings = new SettingsService(Setting, Sound);
            _audio = new AudioService(Sound, Settings.ApplyAudioSettings);
            Config = new global::GourmetProject.Config.ConfigService();
            Settings.ApplyAll();

            ServiceLocator.Instance.Register(Random);
            ServiceLocator.Instance.Register(Save);
            ServiceLocator.Instance.Register(Settings);
            ServiceLocator.Instance.Register(Config);

            _servicesInitialized = true;
            Log.Info("Base services initialized (Random / Save / Settings).", Tag);
        }
    }
}
