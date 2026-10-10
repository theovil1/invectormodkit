using System.Text;
using System.Threading.Tasks;
using InvectorModKit.Marketplace.Client;
using UnityEngine.Networking;

namespace InvectorModKit.Marketplace
{
    /// <summary>HTTP through Unity (uses the game's own TLS stack); callable from any thread.</summary>
    internal sealed class UnityWebRequestTransport : IHttpTransport
    {
        public Task<HttpResult> SendAsync(string method, string url, string jsonBody, string bearer)
        {
            var done = new TaskCompletionSource<HttpResult>();
            MainThread.Post(() =>
            {
                var request = new UnityWebRequest(url, method) { downloadHandler = new DownloadHandlerBuffer(), timeout = 30 };
                if (jsonBody != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                if (bearer != null) request.SetRequestHeader("Authorization", "Bearer " + bearer);
                request.SendWebRequest().completed += _ =>
                {
                    if (request.result == UnityWebRequest.Result.ConnectionError)
                        done.TrySetException(new System.Net.WebException(request.error));
                    else
                        done.TrySetResult(new HttpResult { Status = (int)request.responseCode, Body = request.downloadHandler.text });
                    request.Dispose();
                };
            });
            return done.Task;
        }
    }
}
