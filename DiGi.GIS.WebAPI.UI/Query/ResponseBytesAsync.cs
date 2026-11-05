using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace DiGi.GIS.WebAPI.UI
{
    public static partial class Query
    {
        /// <summary>
        /// Asynchronously relays a bodyless request to a Web API and reads back the status it answered with together with the body it returned as bytes.
        /// <para>The binary-body counterpart of <see cref="ResponseAsync(HttpClient, HttpMethod, string, string, CancellationToken)"/>: it keeps the status rather than collapsing failures into <see langword="null"/> (see <see cref="Classes.WebAPIResponse"/> for why), and it reads the body raw rather than as text, which is what <see cref="BytesAsync(HttpClient, string, CancellationToken)"/> cannot do - that helper decodes the body as a string and is anonymous, while the Orto Area image endpoint requires the session and distinguishes an epoch with no imagery (404) from an outage (503).</para>
        /// <para>The bearer variant <see cref="BytesAsync(HttpClient, string, CancellationToken)"/> names as the one to add if ever needed: the token is attached as <c>Authorization: Bearer</c> when one is given and omitted entirely when it is not, and the status is always answered with, so an unsuccessful response reports its status with no bytes.</para>
        /// </summary>
        /// <param name="httpClient">The HTTP client used for the request. This value can be null.</param>
        /// <param name="requestUri">The Web API URL to relay to. This value can be null.</param>
        /// <param name="bearerToken">The session token to present, or null to make the request anonymously.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>The status and body the Web API answered with, or <see langword="null"/> when it answered nothing at all.</returns>
        public static async Task<Classes.WebAPIBytesResponse?> ResponseBytesAsync(this HttpClient? httpClient, string? requestUri, string? bearerToken, CancellationToken cancellationToken = default)
        {
            if (httpClient is null || string.IsNullOrWhiteSpace(requestUri))
            {
                return null;
            }

            try
            {
                using HttpRequestMessage httpRequestMessage = new(HttpMethod.Get, requestUri);

                if (!string.IsNullOrWhiteSpace(bearerToken))
                {
                    httpRequestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                }

                using HttpResponseMessage httpResponseMessage = await httpClient.SendAsync(httpRequestMessage, cancellationToken);

                int statusCode = (int)httpResponseMessage.StatusCode;
                if (!httpResponseMessage.IsSuccessStatusCode)
                {
                    return new Classes.WebAPIBytesResponse(statusCode);
                }

                byte[] bytes = await httpResponseMessage.Content.ReadAsByteArrayAsync(cancellationToken);
                return new Classes.WebAPIBytesResponse(statusCode, bytes.Length == 0 ? null : bytes);
            }
            catch
            {
                return null;
            }
        }
    }
}
