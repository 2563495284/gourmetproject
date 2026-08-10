using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Hortor
{
    public enum EventType
    {
        Track,
        UserSet,
        UserSetOnce,
        UserAdd,
        UserUnset,
        UserDel,
    }

    public sealed class PostGameLogOption
    {
        public string eventName = string.Empty;
        public EventType eventType = EventType.Track;
        public Dictionary<string, object> customData = new Dictionary<string, object>();
    }

    internal sealed class CombTrackLogItem
    {
        public string eventName;
        public string eventType;
        public Dictionary<string, object> extra;
        public string logId;
        public int logType;
        public string queryRaw;
        public int timestamp;
    }

    internal sealed class TGAHandler : MonoBehaviour
    {
        private static readonly List<CombTrackLogItem> Logs = new List<CombTrackLogItem>();
        private static readonly string[] EventTypeNames =
        {
            "track", "user_set", "user_setOnce", "user_add", "user_unset", "user_del",
        };

        private static TGAHandler _instance;
        private string _accountId = string.Empty;
        private bool _hasStartedTimer;

        public static TGAHandler Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                GameObject gameObject = GameObject.Find("TGAHandler") ?? new GameObject("TGAHandler");
                if (!gameObject.TryGetComponent(out _instance))
                {
                    _instance = gameObject.AddComponent<TGAHandler>();
                }
                DontDestroyOnLoad(gameObject);
                return _instance;
            }
        }

        public static void ClearPendingLogs()
        {
            Logs.Clear();
            if (_instance == null)
            {
                return;
            }

            _instance.CancelInvoke(nameof(CheckLogListAndPost));
            _instance._hasStartedTimer = false;
        }

        public void SetAccountId(string id)
        {
            _accountId = id ?? string.Empty;
        }

        public void PostGameLog(PostGameLogOption option)
        {
            if (option == null)
            {
                throw new ArgumentNullException(nameof(option));
            }

            if (string.IsNullOrWhiteSpace(option.eventName))
            {
                throw new ArgumentException("eventName must not be empty.", nameof(option));
            }

            Dictionary<string, object> extra = option.customData == null
                ? new Dictionary<string, object>()
                : new Dictionary<string, object>(option.customData);
            AddSdkProperties(extra, option.eventType);
            Logs.Add(new CombTrackLogItem
            {
                eventName = option.eventName,
                eventType = EventTypeNames[(int)option.eventType],
                extra = extra,
                logId = Guid.NewGuid().ToString(),
                logType = 2,
                queryRaw = "{}",
                timestamp = 0,
            });

            if (_hasStartedTimer)
            {
                return;
            }

            _hasStartedTimer = true;
            InvokeRepeating(nameof(CheckLogListAndPost), 0f, 3f);
        }

        private static void AddSdkProperties(Dictionary<string, object> extra, EventType eventType)
        {
            extra["sdk_trackplatform"] = "game_c";
            extra["app_sdk_version"] = string.Empty;
            extra["client_versioin"] = string.Empty;
            extra["game_version"] = GameInfo.GameVersion;
            extra["hsdk_version"] = InitHandler.HsdkVersion;
            extra["idfa"] = string.Empty;
            extra["sdk_client"] = "minigame";
            extra["sdk_client_version"] = string.Empty;
            extra["sdk_current_channel"] = string.Empty;
            extra["sdk_first_channel"] = string.Empty;
            extra["game_channel"] = string.Empty;
            extra["game_origin_channel"] = string.Empty;
            extra["sdk_is_ipad"] = false;
            extra["sdk_time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            extra["trackplatform"] = "sdk_frontend";
            extra["graphicsMemorySize"] = ClientSystemInfo.GraphicsMemorySize;
            extra["graphicsDeviceName"] = ClientSystemInfo.GraphicsDeviceName;
            extra["processorType"] = ClientSystemInfo.ProcessorType;
            extra["processorCount"] = ClientSystemInfo.ProcessorCount;
            extra["systemMemorySize"] = ClientSystemInfo.SystemMemorySize;
            extra["processorFrequency"] = ClientSystemInfo.ProcessorFrequency;

            if (eventType != EventType.Track)
            {
                return;
            }

            extra["#device_model"] = string.Empty;
            extra["#lib"] = "weixin";
            extra["#lib_version"] = string.Empty;
            extra["#manufacturer"] = string.Empty;
            extra["#network_type"] = string.Empty;
            extra["#os"] = ClientSystemInfo.Platform;
            extra["#os_version"] = ClientSystemInfo.Version;
            extra["#scene"] = 1001;
            extra["#screen_height"] = 100;
            extra["#system_language"] = "zh_CN";
        }

        private void CheckLogListAndPost()
        {
            if (Logs.Count == 0)
            {
                return;
            }

            var pendingLogs = new List<CombTrackLogItem>(Logs);
            Logs.Clear();
            var data = new Dictionary<string, object>
            {
                ["gameId"] = GameInfo.GameId,
                ["gameTp"] = "minigame",
                ["accountId"] = string.Empty,
                ["channel"] = "hortor",
                ["clientInfo"] = CreateClientInfo(),
                ["distinctId"] = GetLocalStorageGuid(),
                ["gameVersion"] = GameInfo.GameVersion,
                ["logSource"] = "mini-sdk",
                ["openId"] = string.Empty,
                ["platformId"] = _accountId,
                ["logs"] = pendingLogs,
            };
            Network.Instance.Post("/htlog/api/v1/log/comb", JsonConvert.SerializeObject(data));
        }

        private static Dictionary<string, object> CreateClientInfo()
        {
            return new Dictionary<string, object>
            {
                ["clientType"] = "weixin",
                ["version"] = ClientSystemInfo.Version,
                ["language"] = "zh_CN",
                ["model"] = "devtools",
                ["system"] = ClientSystemInfo.Platform,
                ["brand"] = "devtools",
                ["pixelRatio"] = 3,
                ["screenWidth"] = 360,
                ["screenHeight"] = 640,
                ["platform"] = "devtools",
                ["sdkVersion"] = "1.0.0",
                ["hsdkVersion"] = InitHandler.HsdkVersion,
                ["netType"] = "wifi",
            };
        }

        private static string GetLocalStorageGuid()
        {
            const string key = "HSDK.TGA.LocalGuid";
            string localGuid = PlayerPrefs.GetString(key, string.Empty);
            if (!string.IsNullOrEmpty(localGuid))
            {
                return localGuid;
            }

            localGuid = Guid.NewGuid().ToString();
            PlayerPrefs.SetString(key, localGuid);
            PlayerPrefs.Save();
            return localGuid;
        }
    }
}
