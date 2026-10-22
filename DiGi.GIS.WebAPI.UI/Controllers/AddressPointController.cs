using DiGi.WebAPI.Classes;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DiGi.GIS.WebAPI.UI.Controllers
{
    /// <summary>
    /// Relays the address search of the GIS Web API (gis/addresspoint) to the pages of this application.
    /// <para>The ranking is owned by the GIS Web API; this controller only forwards the query and the answer.</para>
    /// </summary>
    [Route("[controller]")]
    public class AddressPointController : Controller
    {
        private readonly IHttpClientFactory httpClientFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="AddressPointController"/> class.
        /// </summary>
        /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/> used to create HTTP clients.</param>
        public AddressPointController(IHttpClientFactory httpClientFactory)
        {
            this.httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Finds the buildings matching a free text address, in rank order.
        /// <para>Status-preserving, like the area search: an upstream failure (502) or no answer (503) is distinct from the empty result (204). The body is relayed verbatim, so the <c>_type</c> discriminators of the matches are kept.</para>
        /// <para>The query parameter names are the upstream contract (<c>[FromQuery(Name = "…")]</c> of DiGi.GIS.WebAPI's address point controller), which has no compile-time link to this client and ignores an unknown name silently - keep both sides in step by hand.</para>
        /// </summary>
        /// <param name="text">The free text to search for. At least <see cref="Constants.Default.AddressSearchMinimumLength"/> characters.</param>
        /// <param name="houseNumber">The optional house number narrowing the search.</param>
        /// <param name="countyId">The optional unique identifier of the county the search is limited to.</param>
        /// <param name="limit">The optional largest number of matches, from 1 to <see cref="Constants.Default.AddressSearchLimitMaximum"/>. When omitted, the upstream default applies.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>A <see cref="Task{IActionResult}"/> carrying the matches, a 400 Bad Request response for a text that is too short or a limit out of range, a 204 No Content response when nothing matches, a 502 Bad Gateway response when the upstream answers a failure, or a 503 Service Unavailable response when it answers nothing at all.</returns>
        [HttpGet("buildingsbyaddress")]
        public async Task<IActionResult> GetBuildingsByAddressAsync([FromQuery(Name = "text")] string? text, [FromQuery(Name = "housenumber")] string? houseNumber, [FromQuery(Name = "countyid")] int? countyId, [FromQuery(Name = "limit")] int? limit, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < Constants.Default.AddressSearchMinimumLength)
            {
                return BadRequest();
            }

            if (limit.HasValue && (limit.Value < 1 || limit.Value > Constants.Default.AddressSearchLimitMaximum))
            {
                return BadRequest();
            }

            HttpClient httpClient = httpClientFactory.CreateClient();

            UrlBuilder urlBuilder = new(Constants.Default.AddressPointBuildingsByAddressUri);
            urlBuilder = urlBuilder.AddParameter("text", text.Trim());
            if (!string.IsNullOrWhiteSpace(houseNumber))
            {
                urlBuilder = urlBuilder.AddParameter("housenumber", houseNumber.Trim());
            }

            if (countyId.HasValue)
            {
                urlBuilder = urlBuilder.AddParameter("countyid", countyId.Value);
            }

            if (limit.HasValue)
            {
                urlBuilder = urlBuilder.AddParameter("limit", limit.Value);
            }

            return Query.RelayUpstream(await httpClient.ResponseAsync(HttpMethod.Get, urlBuilder.ToString(), null, cancellationToken));
        }
    }
}
