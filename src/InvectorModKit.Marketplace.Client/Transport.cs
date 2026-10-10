using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace InvectorModKit.Marketplace.Client
{
    public sealed class HttpResult
    {
        public int Status;
        public string Body;
    }

    /// <summary>One HTTP exchange; the game provides its own (UnityWebRequest), tools and tests use <see cref="HttpClientTransport"/>.</summary>
    public interface IHttpTransport
    {
        /// <param name="jsonBody">Null for no body.</param>
        /// <param name="bearer">Null when not signed in.</param>
        Task<HttpResult> SendAsync(string method, string url, string jsonBody, string bearer);
    }

    public sealed class HttpClientTransport : IHttpTransport
    {
        private readonly HttpClient _client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        public async Task<HttpResult> SendAsync(string method, string url, string jsonBody, string bearer)
        {
            using (var request = new HttpRequestMessage(new HttpMethod(method), url))
            {
                if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                if (jsonBody != null) request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                using (var response = await _client.SendAsync(request).ConfigureAwait(false))
                    return new HttpResult { Status = (int)response.StatusCode, Body = await response.Content.ReadAsStringAsync().ConfigureAwait(false) };
            }
        }
    }
}
