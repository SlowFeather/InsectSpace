using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace InsectSpace.Gameplay.Modules
{
    internal sealed class BackendRequestError
    {
        public long Status { get; }
        public string Message { get; }
        public BackendRequestError(long status, string message) { Status = status; Message = message; }
    }
    internal sealed class BackendHttpClient
    {
        private readonly MonoBehaviour runner;

        public BackendHttpClient(MonoBehaviour runner)
        {
            this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        public Coroutine Post<T>(string baseUrl, string path, object body, string bearer,
            Action<T> completed, Action<BackendRequestError> failed)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                failed?.Invoke(new BackendRequestError(0, "Backend endpoint is not configured."));
                return null;
            }
            var url = baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
            return runner.StartCoroutine(PostRoutine(url, body, bearer, completed, failed));
        }

        private static IEnumerator PostRoutine<T>(string url, object body, string bearer,
            Action<T> completed, Action<BackendRequestError> failed)
        {
            string json;
            try { json = JsonUtility.ToJson(body); }
            catch (Exception) { failed?.Invoke(new BackendRequestError(0, "Request serialization failed.")); yield break; }

            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 15;
                request.redirectLimit = 0;
                request.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrWhiteSpace(bearer)) request.SetRequestHeader("Authorization", "Bearer " + bearer);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    failed?.Invoke(new BackendRequestError(request.responseCode, "请求失败（HTTP " + request.responseCode + "）。请检查网络、手机号、验证码或会话有效期。"));
                    yield break;
                }
                try
                {
                    var value = JsonUtility.FromJson<T>(request.downloadHandler.text);
                    if (value == null) throw new InvalidOperationException("The backend returned an empty response.");
                    completed?.Invoke(value);
                }
                catch (Exception) { failed?.Invoke(new BackendRequestError(0, "Backend response was invalid.")); }
            }
        }
    }
}
