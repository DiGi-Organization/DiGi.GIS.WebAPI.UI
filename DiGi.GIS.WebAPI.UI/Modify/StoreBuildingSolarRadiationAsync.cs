using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace DiGi.GIS.WebAPI.UI
{
    public static partial class Modify
    {
        /// <summary>
        /// Stores the solar radiation of a building in the GIS Web API (<see cref="Constants.Default.BuildingSolarRadiationSetUri"/>) on behalf of the signed-in visitor whose token is given.
        /// <para>The body is the DiGi JSON of the result (<c>ToJsonObject()</c>, with <c>_type</c>), sent as a <see cref="JsonObject"/>, which <c>JsonContent</c> writes verbatim; a typed object would go through System.Text.Json and lose the DiGi layout (<c>Coding - WebAPI Contracts.md</c> §2). The server stamps the user and the time. A refusal or failure is the caller's to log; it never fails the visitor's response.</para>
        /// </summary>
        /// <param name="httpClient">The HTTP client.</param>
        /// <param name="buildingSolarRadiationResult">The result to store.</param>
        /// <param name="tokenString">The visitor's bearer token (<see cref="Constants.Default.UserTokenCookieName"/>).</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to observe for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the response - 200 with the row id on success - or null when an input is missing or the request did not complete.</returns>
        public static async Task<Classes.WebAPIResponse?> StoreBuildingSolarRadiationAsync(this HttpClient? httpClient, GIS.Classes.BuildingSolarRadiationResult? buildingSolarRadiationResult, string? tokenString, CancellationToken cancellationToken = default)
        {
            if (httpClient is null || string.IsNullOrWhiteSpace(tokenString) || buildingSolarRadiationResult?.ToJsonObject() is not JsonObject jsonObject)
            {
                return null;
            }

            return await httpClient.ResponseAsync(HttpMethod.Post, Constants.Default.BuildingSolarRadiationSetUri, tokenString, jsonObject, cancellationToken);
        }
    }
}
