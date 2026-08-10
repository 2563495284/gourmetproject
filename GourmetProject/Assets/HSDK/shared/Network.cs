using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Hortor
{
    internal sealed class Network : MonoBehaviour
    {
        private static Network _instance;
        private static string _tgaHost = string.Empty;

        public static Network Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                GameObject gameObject = GameObject.Find("HSDK.Network") ?? new GameObject("HSDK.Network");
                if (!gameObject.TryGetComponent(out _instance))
                {
                    _instance = gameObject.AddComponent<Network>();
                }
                DontDestroyOnLoad(gameObject);
                return _instance;
            }
        }

        public static void Init(ENV environment)
        {
            string suffix = environment == ENV.Test ? "-test" : string.Empty;
            _tgaHost = $"https://platform-stat{suffix}.hortorgames.com";
        }

        public void Post(string path, string json)
        {
            StartCoroutine(PostRequest($"{_tgaHost}{path}", json));
        }

        private static IEnumerator PostRequest(string url, string json)
        {
            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[HSDK] TGA request failed: {request.error}");
                }
                else
                {
                    HSDK.Print($"TGA response: {request.downloadHandler.text}");
                }
            }
        }
    }
}
