using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiGi.GIS.WebAPI.UI
{
    public static partial class Query
    {
        /// <summary>
        /// Asynchronously relays a request whose body is already a serialized JSON document, sending those bytes verbatim, and reads back the status the Web API answered with together with the body it returned.
        /// <para>Behaves exactly as <see cref="ResponseAsync(HttpClient, HttpMethod, string, string, CancellationToken)"/> in how failures and statuses are reported; the one difference is the body: where that method serializes the declared type, this one forwards what it was given, so a caller whose contract is "relay the body byte-identically" - the Orto Area object and label upserts, where <c>state</c> stays the integer the page sent - never re-serializes, re-cases or re-orders a single token of it.</para>
        /// <para>The body is sent as <c>application/json</c>; a null body is sent as an empty one rather than omitted, so the receiver's refusal of an incomplete request comes back to be mirrored.</para>
        /// </summary>
        /// <param name="httpClient">The HTTP client used for the request. This value can be null.</param>
        /// <param name="httpMethod">The HTTP method to use. This value can be null.</param>
        /// <param name="requestUri">The Web API URL to relay to. This value can be null.</param>
        /// <param name="bearerToken">The session token to present, or null to make the request anonymously.</param>
        /// <param name="jsonBody">The request body, already serialized as JSON. This value can be null.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>The status and body the Web API answered with, or <see langword="null"/> when it answered nothing at all.</returns>
        public static async Task<Classes.WebAPIResponse?> ResponseRawAsync(this HttpClient? httpClient, HttpMethod? httpMethod, string? requestUri, string? bearerToken, string? jsonBody, CancellationToken cancellationToken = default)
        {
            if (httpClient is null || httpMethod is null || string.IsNullOrWhiteSpace(requestUri))
            {
                return null;
            }

            try
            {
                using HttpRequestMessage httpRequestMessage = new(httpMethod, requestUri);

                if (!string.IsNullOrWhiteSpace(bearerToken))
                {
                    httpRequestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                }

                httpRequestMessage.Content = new StringContent(jsonBody ?? string.Empty, Encoding.UTF8, "application/json");

                using HttpResponseMessage httpResponseMessage = await httpClient.SendAsync(httpRequestMessage, cancellationToken);

                string json = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken);

                return new Classes.WebAPIResponse((int)httpResponseMessage.StatusCode, string.IsNullOrWhiteSpace(json) ? null : json);
            }
            catch
            {
                return null;
            }
        }
    }
}
