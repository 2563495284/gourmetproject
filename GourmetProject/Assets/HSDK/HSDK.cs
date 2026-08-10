using UnityEngine;

namespace Hortor
{
    public enum ENV
    {
        Test,
        Production,
    }

    public static class HSDK
    {
        public static void Init(InitOption option)
        {
            Print($"Init {JsonUtility.ToJson(option)}");
            InitHandler.Init(option);
        }

        public static void SetTGAAccountId(string id)
        {
            Print($"SetTGAAccountId {id}");
            TGAHandler.Instance.SetAccountId(id);
        }

        public static void PostGameLog(PostGameLogOption option)
        {
            Print($"PostGameLog {JsonUtility.ToJson(option)}");
            TGAHandler.Instance.PostGameLog(option);
        }

        public static void ClearPendingGameLogs()
        {
            TGAHandler.ClearPendingLogs();
        }

        internal static void Print(string message)
        {
            if (Debug.isDebugBuild)
            {
                Debug.Log($"[HSDK] {message}");
            }
        }
    }
}
