using DiGi.GIS.PostgreSQL.Classes;
using DiGi.GIS.WebAPI.UI.Classes;
using DiGi.WebAPI.Classes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DiGi.GIS.WebAPI.UI.Controllers
{
    /// <summary>
    /// Provides the Orto Data verification page and the endpoints it uses, each relaying to the GIS Web API with the visitor's session presented as a bearer token.
    /// <para>Distinct from <see cref="OrtoDatasController"/>, which serves the orthophoto coverage panel of the building details page. This controller is the whole of a standalone page: it draws a building that has photos but no user year built answer yet, shows one card per photo year, and records the reviewer's answer.</para>
    /// <para>The page requires a session: the page action redirects an anonymous visitor to the sign-in page, and every other action answers 401, which the page script answers with one refresh attempt and then a sign-out (<c>wwwroot/js/user.js</c>).</para>
    /// <para>The <c>area/</c> actions serve the page's second mode, the Full-interpretation test areas (issue #84): epoch years and imagery for a bounding box, the area with its sampled cells and drawn objects, the footprint overlay, and the label read and upsert that record present / absent / indeterminate per object &#215; epoch. They are relays and nothing more - query names, bodies and statuses pass through as the sibling <c>/gis/ortoarea/</c> routes define them (DiGi.GIS.WebAPI#74), so the UI stays a store-nothing session gate over that service.</para>
    /// </summary>
    [Route("[controller]")]
    public class OrtoDataController : Controller
    {
        private readonly IHttpClientFactory httpClientFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="OrtoDataController"/> class.
        /// </summary>
        /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/> used to create HTTP clients.</param>
        public OrtoDataController(IHttpClientFactory httpClientFactory)
        {
            this.httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Returns the Orto Data verification page.
        /// <para>The page itself carries no building: its script draws one on load, either the next unverified building or the one named by the <c>countyid</c> and <c>reference</c> query values it reads itself. The optional repeated <c>countyids</c> confines the script's draws to those <c>building_2d</c> parts.</para>
        /// </summary>
        /// <returns>An <see cref="IActionResult"/> holding the verification view, or a redirect to the sign-in page for an anonymous visitor.</returns>
        [HttpGet("")]
        public IActionResult Start()
        {
            string? tokenString = Request.Cookies[Constants.Default.UserTokenCookieName];
            if (string.IsNullOrWhiteSpace(tokenString))
            {
                // The sign-in pattern of UserController.Login: only an address inside this application is ever
                // returned to, so a crafted address cannot turn this redirect into an open redirector.
                string returnUrl = HttpContext.Request.Path + HttpContext.Request.QueryString;
                string loginUrl = Url.Content("~/login") + "?returnUrl=" + Uri.EscapeDataString(Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Content("~/"));

                return Redirect(loginUrl);
            }

            return View();
        }

        /// <summary>
        /// Draws the next building to verify and reads the photo years it holds.
        /// </summary>
        /// <param name="countyIds">Optional <c>building_2d</c> part ids confining the draw; omitted or empty draws from every covered part.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>An <see cref="IActionResult"/> holding the drawn building with its years, the empty state when nothing unverified remains, 401 without a session, or the status the service answered with.</returns>
        [HttpGet("random")]
        public async Task<IActionResult> GetRandomAsync([FromQuery(Name = "countyids")] int[]? countyIds, CancellationToken cancellationToken = default)
        {
            string? tokenString = Request.Cookies[Constants.Default.UserTokenCookieName];
            if (string.IsNullOrWhiteSpace(tokenString))
            {
                return Unauthorized();
            }

            HttpClient httpClient = httpClientFactory.CreateClient();

            UrlBuilder urlBuilder = new(Constants.Default.OrtoDataRandomBuilding2DReferenceUri);
            if (countyIds is { Length: > 0 })
            {
                urlBuilder = urlBuilder.AddParameter("countyids", countyIds);
            }

            WebAPIResponse? webAPIResponse = await httpClient.ResponseAsync(HttpMethod.Get, urlBuilder.ToString(), tokenString, cancellationToken);
            if (webAPIResponse is null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (webAPIResponse.StatusCode == StatusCodes.Status404NotFound)
            {
                // Nothing unverified remains. The empty state, not an error: the page celebrates rather than reporting.
                return Ok(new OrtoDataBuildingResponse());
            }

            if (webAPIResponse.StatusCode != StatusCodes.Status200OK)
            {
                return StatusCode(webAPIResponse.StatusCode);
            }

            List<Building2DReference>? building2DReferences = Core.Convert.ToDiGi<Building2DReference>(webAPIResponse.Json);
            Building2DReference? building2DReference = building2DReferences?.FirstOrDefault();
            if (building2DReference is null)
            {
                return Ok(new OrtoDataBuildingResponse());
            }

            List<short> years = await YearsAsync(httpClient, building2DReference.Reference, building2DReference.CountyId, tokenString, cancellationToken) ?? [];

            return Ok(new OrtoDataBuildingResponse { CountyId = building2DReference.CountyId, Reference = building2DReference.Reference, Years = years });
        }

        /// <summary>
        /// Reads a named building with its photo years and the answer already recorded for it.
        /// <para>Direct mode: reached from the query values the page was opened with, and the only mode that can carry an existing answer - a freshly drawn building has none by construction.</para>
        /// </summary>
        /// <param name="countyId">The optional identifier of the county part the building is filed under; omitted, the service resolves the reference to the lowest part holding it.</param>
        /// <param name="reference">The reference of the building to read.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>An <see cref="IActionResult"/> holding the building with its years and any recorded answer, 404 when no such building exists, 400 without a reference, 401 without a session, 503 when the service answered nothing at all, or the status the service answered with.</returns>
        [HttpGet("building")]
        public async Task<IActionResult> GetBuildingAsync([FromQuery(Name = "countyid")] int? countyId, [FromQuery(Name = "reference")] string? reference, CancellationToken cancellationToken = default)
        {
            string? tokenString = Request.Cookies[Constants.Default.UserTokenCookieName];
            if (string.IsNullOrWhiteSpace(tokenString))
            {
                return Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(reference))
            {
                return BadRequest();
            }

            HttpClient httpClient = httpClientFactory.CreateClient();

            // The reference read is status-preserving rather than collapsing to null: "no such building" and
            // "the service is down" are different answers to the reviewer, and only this one may become a 404
            // (issue #48; the same conflation class as #40).
            WebAPIResponse? webAPIResponse = await httpClient.Building2DReferenceResponseAsync(reference, countyId, cancellationToken);
            if (webAPIResponse is null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (webAPIResponse.StatusCode == StatusCodes.Status404NotFound)
            {
                return NotFound();
            }

            if (webAPIResponse.StatusCode != StatusCodes.Status200OK)
            {
                return StatusCode(webAPIResponse.StatusCode);
            }

            Building2DReference? building2DReference = Core.Convert.ToDiGi<Building2DReference>(webAPIResponse.Json)?.FirstOrDefault();
            if (building2DReference is null)
            {
                return NotFound();
            }

            List<short> years = await YearsAsync(httpClient, building2DReference.Reference, building2DReference.CountyId, tokenString, cancellationToken) ?? [];

            // The user entry is read from the full year built record; predicted years are never surfaced. Deserialized
            // as the concrete class because the user entry reader lives there, not on IYearBuiltData. The DiGi.GIS
            // class is named in full: DiGi.GIS.PostgreSQL.Classes holds a same-named YearBuiltData (Coding - General, section 1.9).
            UrlBuilder urlBuilder = new($"{Constants.Default.GISWebAPIUri}/gis/yearbuiltdata/itemsbyreference");
            urlBuilder = urlBuilder.AddParameter("reference", building2DReference.Reference);
            if (building2DReference.CountyId.HasValue)
            {
                urlBuilder = urlBuilder.AddParameter("countyid", building2DReference.CountyId.Value);
            }

            List<GIS.Classes.YearBuiltData>? yearBuiltDatas = await httpClient.ItemsAsync<GIS.Classes.YearBuiltData>(urlBuilder.ToString(), cancellationToken);
            GIS.Classes.UserYearBuilt? userYearBuilt = yearBuiltDatas?.FirstOrDefault()?.GetUserYearBuilt();

            OrtoDataBuildingResponse ortoDataBuildingResponse = new() { CountyId = building2DReference.CountyId, Reference = building2DReference.Reference, Years = years };
            if (userYearBuilt is not null)
            {
                ortoDataBuildingResponse.Selected = new OrtoDataBuildingResponse.Selection { Year = userYearBuilt.Year, Relation = (int)userYearBuilt.YearBuiltRelation, UserName = userYearBuilt.UserName, DateTime = userYearBuilt.DateTime };
            }

            return Ok(ortoDataBuildingResponse);
        }

        /// <summary>
        /// Reads the orthophoto image of a building for one year, as JPEG bytes.
        /// <para>The image is requested only for years the years read listed, and a year the service holds no photo for answers 404 here, which the page turns into a hidden card.</para>
        /// </summary>
        /// <param name="countyId">The optional identifier of the county part the building is filed under.</param>
        /// <param name="reference">The reference of the building.</param>
        /// <param name="year">The photo year to read.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>An <see cref="IActionResult"/> holding the image, 404 when there is none, 400 without a reference or year, or 401 without a session.</returns>
        [HttpGet("image")]
        public async Task<IActionResult> GetImageAsync([FromQuery(Name = "countyid")] int? countyId, [FromQuery(Name = "reference")] string? reference, [FromQuery(Name = "year")] short? year, CancellationToken cancellationToken = default)
        {
            string? tokenString = Request.Cookies[Constants.Default.UserTokenCookieName];
            if (string.IsNullOrWhiteSpace(tokenString))
            {
                return Unauthorized();
            }

            // Nullable bindings on purpose: an absent reference or year must be refused rather than read as 0 or an empty string.
            if (string.IsNullOrWhiteSpace(reference) || !year.HasValue)
            {
                return BadRequest();
            }

            HttpClient httpClient = httpClientFactory.CreateClient();

            UrlBuilder urlBuilder = new(Constants.Default.OrtoDataImageByReferenceUri);
            urlBuilder = urlBuilder.AddParameter("reference", reference);
            urlBuilder = urlBuilder.AddParameter("year", year.Value);
            if (countyId.HasValue)
            {
                urlBuilder = urlBuilder.AddParameter("countyid", countyId.Value);
            }
            urlBuilder = urlBuilder.AddParameter("fallbackbyreference", true);

            byte[]? bytes = await httpClient.BytesAsync(urlBuilder.ToString(), cancellationToken);
            if (bytes is null || bytes.Length == 0)
            {
                return NotFound();
            }

            return File(bytes, "image/jpeg");
        }

        /// <summary>
        /// Records a reviewer's year built answer for a building, relaying it to the GIS Web API.
        /// <para>The upstream status is mirrored rather than reinterpreted: 404 is the page's "building not found", 400 the service's own refusal of an incomplete answer, 401 a spent session, and a 500 or 503 a server fault that must not be reported as the caller's mistake.</para>
        /// </summary>
        /// <param name="userYearBuiltParameter">The submitted answer.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>An <see cref="IActionResult"/> that is empty on success or carries the status the service answered with.</returns>
        [HttpPost("useryearbuilt")]
        public async Task<IActionResult> SetUserYearBuiltAsync([FromBody] UserYearBuiltParameter? userYearBuiltParameter, CancellationToken cancellationToken = default)
        {
            string? tokenString = Request.Cookies[Constants.Default.UserTokenCookieName];
            if (string.IsNullOrWhiteSpace(tokenString))
            {
                return Unauthorized();
            }

            // Refused the way the service refuses an incomplete answer, so the page cannot distinguish this 400 from its own.
            if (userYearBuiltParameter is null || !userYearBuiltParameter.CountyId.HasValue || string.IsNullOrWhiteSpace(userYearBuiltParameter.Reference) || !userYearBuiltParameter.Year.HasValue)
            {
                return BadRequest();
            }

            HttpClient httpClient = httpClientFactory.CreateClient();

            WebAPIResponse? webAPIResponse = await httpClient.ResponseAsync(HttpMethod.Post, Constants.Default.YearBuiltDataSetUserYearBuiltUri, tokenString, userYearBuiltParameter, cancellationToken);
            if (webAPIResponse is null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            return StatusCode(webAPIResponse.StatusCode);
        }

        /// <summary>
        /// Relays the epoch years whose imagery covers a bounding box - the years the area mode of the page renders one epoch card for.
        /// <para>The answer is parsed into the year array rather than passed through as text, so the page is handed exactly the contract the table promises: an upstream 200 <c>[]</c> is an area with no epochs, not an error.</para>
        /// </summary>
        /// <param name="x_1">The X coordinate of the first bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="y_1">The Y coordinate of the first bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="x_2">The X coordinate of the second bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="y_2">The Y coordinate of the second bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>An <see cref="IActionResult"/> holding the years, 400 when a corner is missing or not a number, 401 without a session, 502 when a 200 body is not a year array, 503 when the service answered nothing at all, or the status the service answered with.</returns>
        [HttpGet("area/availableyears")]
        public async Task<IActionResult> GetAreaAvailableYearsAsync(double? x_1, double? y_1, double? x_2, double? y_2, CancellationToken cancellationToken = default)
        {
            string? tokenString = Request.Cookies[Constants.Default.UserTokenCookieName];
            if (string.IsNullOrWhiteSpace(tokenString))
            {
                return Unauthorized();
            }

            // Nullable bindings on purpose: an absent corner must be refused rather than read as a zero-area
            // query the service would happily answer with no years.
            if (!x_1.HasValue || !y_1.HasValue || !x_2.HasValue || !y_2.HasValue)
            {
                return BadRequest();
            }

            HttpClient httpClient = httpClientFactory.CreateClient();

            UrlBuilder urlBuilder = new(Constants.Default.OrtoAreaAvailableYearsUri);
            urlBuilder = urlBuilder.AddParameter("x_1", x_1.Value);
            urlBuilder = urlBuilder.AddParameter("y_1", y_1.Value);
            urlBuilder = urlBuilder.AddParameter("x_2", x_2.Value);
            urlBuilder = urlBuilder.AddParameter("y_2", y_2.Value);

            WebAPIResponse? webAPIResponse = await httpClient.ResponseAsync(HttpMethod.Get, urlBuilder.ToString(), tokenString, cancellationToken);
            if (webAPIResponse is null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (webAPIResponse.StatusCode != StatusCodes.Status200OK)
            {
                return StatusCode(webAPIResponse.StatusCode);
            }

            if (string.IsNullOrWhiteSpace(webAPIResponse.Json))
            {
                return Ok(new List<short>());
            }

            try
            {
                List<short>? years = JsonSerializer.Deserialize<List<short>>(webAPIResponse.Json);
                return Ok(years ?? []);
            }
            catch (JsonException)
            {
                // A 200 whose body is not a year array is a content fault, answered as the fault it is (the
                // mapping idiom of Query.RelayUpstream) and never as [] - an empty year list would read on the
                // page as "no epochs cover this area".
                return StatusCode(StatusCodes.Status502BadGateway);
            }
        }

        /// <summary>
        /// Relays the orthophoto image of a test area for one epoch year, as JPEG bytes.
        /// <para>Status-preserving rather than collapsing through <see cref="Query.BytesAsync(HttpClient, string, CancellationToken)"/>: unlike the building image read, whose upstream endpoint is anonymous, this upstream route is session-gated, and the page must be able to tell an epoch the negative cache holds no imagery for (the upstream 404, rendered as an "indeterminate" badge - a missing image is data, not a hidden card) from a service that answered nothing (503).</para>
        /// </summary>
        /// <param name="x_1">The X coordinate of the first bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="y_1">The Y coordinate of the first bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="x_2">The X coordinate of the second bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="y_2">The Y coordinate of the second bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="year">The epoch year to read, or null when the query carries none.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>An <see cref="IActionResult"/> holding the image, 400 when a corner or the year is missing, 401 without a session, 404 when there is no imagery for the epoch, 502 when a 200 carried no bytes, 503 when the service answered nothing at all, or the status the service answered with.</returns>
        [HttpGet("area/image")]
        public async Task<IActionResult> GetAreaImageAsync(
            double? x_1,
            double? y_1,
            double? x_2,
            double? y_2,
            short? year,
            CancellationToken cancellationToken = default)
        {
            string? tokenString = Request.Cookies[Constants.Default.UserTokenCookieName];
            if (string.IsNullOrWhiteSpace(tokenString))
            {
                return Unauthorized();
            }

            // Nullable bindings on purpose: an absent corner or year must be refused rather than read as 0.
            if (!x_1.HasValue || !y_1.HasValue || !x_2.HasValue || !y_2.HasValue || !year.HasValue)
            {
                return BadRequest();
            }

            HttpClient httpClient = httpClientFactory.CreateClient();

            UrlBuilder urlBuilder = new(Constants.Default.OrtoAreaImageUri);
            urlBuilder = urlBuilder.AddParameter("x_1", x_1.Value);
            urlBuilder = urlBuilder.AddParameter("y_1", y_1.Value);
            urlBuilder = urlBuilder.AddParameter("x_2", x_2.Value);
            urlBuilder = urlBuilder.AddParameter("y_2", y_2.Value);
            urlBuilder = urlBuilder.AddParameter("year", year.Value);

            WebAPIBytesResponse? webAPIBytesResponse = await httpClient.ResponseBytesAsync(urlBuilder.ToString(), tokenString, cancellationToken);
            if (webAPIBytesResponse is null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (webAPIBytesResponse.StatusCode == StatusCodes.Status404NotFound)
            {
                // No imagery for the epoch: the same 404 shape the building image action answers with, which the
                // page turns into its badge rather than into a hidden card.
                return NotFound();
            }

            if (webAPIBytesResponse.StatusCode != StatusCodes.Status200OK)
            {
                return StatusCode(webAPIBytesResponse.StatusCode);
            }

            if (webAPIBytesResponse.Bytes is null || webAPIBytesResponse.Bytes.Length == 0)
            {
                // A 200 that carried no bytes is a content fault of the relay, not a missing epoch (issue #48
                // conflation class): 502 keeps it distinct from the 404 above.
                return StatusCode(StatusCodes.Status502BadGateway);
            }

            return File(webAPIBytesResponse.Bytes, "image/jpeg");
        }

        /// <summary>
        /// Relays the area read behind the page's area mode: the test area with its sampled cells, its drawn objects and the label rows of every interpreter, verbatim.
        /// </summary>
        /// <param name="x_1">The X coordinate of the first bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="y_1">The Y coordinate of the first bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="x_2">The X coordinate of the second bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="y_2">The Y coordinate of the second bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>An <see cref="IActionResult"/> holding the upstream body, 400 when a corner is missing or not a number, 401 without a session, 503 when the service answered nothing at all, or the status the service answered with.</returns>
        [HttpGet("area/items")]
        public async Task<IActionResult> GetAreaItemsAsync(double? x_1, double? y_1, double? x_2, double? y_2, CancellationToken cancellationToken = default)
        {
            string? tokenString = Request.Cookies[Constants.Default.UserTokenCookieName];
            if (string.IsNullOrWhiteSpace(tokenString))
            {
                return Unauthorized();
            }

            // Nullable bindings on purpose: an absent corner must be refused rather than read as zero.
            if (!x_1.HasValue || !y_1.HasValue || !x_2.HasValue || !y_2.HasValue)
            {
                return BadRequest();
            }

            HttpClient httpClient = httpClientFactory.CreateClient();

            UrlBuilder urlBuilder = new(Constants.Default.OrtoAreaItemsUri);
            urlBuilder = urlBuilder.AddParameter("x_1", x_1.Value);
            urlBuilder = urlBuilder.AddParameter("y_1", y_1.Value);
            urlBuilder = urlBuilder.AddParameter("x_2", x_2.Value);
            urlBuilder = urlBuilder.AddParameter("y_2", y_2.Value);

            WebAPIResponse? webAPIResponse = await httpClient.ResponseAsync(HttpMethod.Get, urlBuilder.ToString(), tokenString, cancellationToken);
            if (webAPIResponse is null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (webAPIResponse.StatusCode != StatusCodes.Status200OK)
            {
                return StatusCode(webAPIResponse.StatusCode);
            }

            return Ok(webAPIResponse.Json);
        }

        /// <summary>
        /// Relays the building footprints within a bounding box - the area mode's footprint overlay - passing the spatial tolerance through, invariantly, and defaulting to the upstream default when the caller names none.
        /// <para>The upstream endpoint is the existing session-agnostic footprint read; this relay still gates it on this application's session, so the whole page keeps one session depth.</para>
        /// </summary>
        /// <param name="x_1">The X coordinate of the first bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="y_1">The Y coordinate of the first bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="x_2">The X coordinate of the second bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="y_2">The Y coordinate of the second bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="tolerance">The spatial tolerance in metres, or null / NaN to relay <see cref="Constants.Default.OrtoAreaTolerance"/> - the upstream default, spelled out rather than left implicit.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>An <see cref="IActionResult"/> holding the upstream body, 400 when a corner is missing or not a number, 401 without a session, 404 when no footprint lies in the bounding box, 503 when the service answered nothing at all, or the status the service answered with.</returns>
        [HttpGet("area/buildings")]
        public async Task<IActionResult> GetAreaBuildingsAsync(
            double? x_1,
            double? y_1,
            double? x_2,
            double? y_2,
            double? tolerance,
            CancellationToken cancellationToken = default)
        {
            string? tokenString = Request.Cookies[Constants.Default.UserTokenCookieName];
            if (string.IsNullOrWhiteSpace(tokenString))
            {
                return Unauthorized();
            }

            // Nullable bindings on purpose: an absent corner must be refused rather than read as zero.
            if (!x_1.HasValue || !y_1.HasValue || !x_2.HasValue || !y_2.HasValue)
            {
                return BadRequest();
            }

            HttpClient httpClient = httpClientFactory.CreateClient();

            double tolerance_Value = tolerance.HasValue && !double.IsNaN(tolerance.Value) ? tolerance.Value : Constants.Default.OrtoAreaTolerance;

            UrlBuilder urlBuilder = new(Constants.Default.OrtoAreaBuildingsUri);
            urlBuilder = urlBuilder.AddParameter("x_1", x_1.Value);
            urlBuilder = urlBuilder.AddParameter("y_1", y_1.Value);
            urlBuilder = urlBuilder.AddParameter("x_2", x_2.Value);
            urlBuilder = urlBuilder.AddParameter("y_2", y_2.Value);
            urlBuilder = urlBuilder.AddParameter("tolerance", tolerance_Value);

            WebAPIResponse? webAPIResponse = await httpClient.ResponseAsync(HttpMethod.Get, urlBuilder.ToString(), tokenString, cancellationToken);
            if (webAPIResponse is null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (webAPIResponse.StatusCode != StatusCodes.Status200OK)
            {
                return StatusCode(webAPIResponse.StatusCode);
            }

            return Ok(webAPIResponse.Json);
        }

        /// <summary>
        /// Relays the label read behind the page's area mode: every label row of the area, all interpreters', verbatim - one export carries them all for the agreement computation.
        /// </summary>
        /// <param name="x_1">The X coordinate of the first bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="y_1">The Y coordinate of the first bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="x_2">The X coordinate of the second bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="y_2">The Y coordinate of the second bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>An <see cref="IActionResult"/> holding the upstream body, 400 when a corner is missing or not a number, 401 without a session, 503 when the service answered nothing at all, or the status the service answered with.</returns>
        [HttpGet("area/labels")]
        public async Task<IActionResult> GetAreaLabelsAsync(double? x_1, double? y_1, double? x_2, double? y_2, CancellationToken cancellationToken = default)
        {
            string? tokenString = Request.Cookies[Constants.Default.UserTokenCookieName];
            if (string.IsNullOrWhiteSpace(tokenString))
            {
                return Unauthorized();
            }

            // Nullable bindings on purpose: an absent corner must be refused rather than read as zero.
            if (!x_1.HasValue || !y_1.HasValue || !x_2.HasValue || !y_2.HasValue)
            {
                return BadRequest();
            }

            HttpClient httpClient = httpClientFactory.CreateClient();

            UrlBuilder urlBuilder = new(Constants.Default.OrtoAreaLabelsUri);
            urlBuilder = urlBuilder.AddParameter("x_1", x_1.Value);
            urlBuilder = urlBuilder.AddParameter("y_1", y_1.Value);
            urlBuilder = urlBuilder.AddParameter("x_2", x_2.Value);
            urlBuilder = urlBuilder.AddParameter("y_2", y_2.Value);

            WebAPIResponse? webAPIResponse = await httpClient.ResponseAsync(HttpMethod.Get, urlBuilder.ToString(), tokenString, cancellationToken);
            if (webAPIResponse is null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (webAPIResponse.StatusCode != StatusCodes.Status200OK)
            {
                return StatusCode(webAPIResponse.StatusCode);
            }

            return Ok(webAPIResponse.Json);
        }

        /// <summary>
        /// Relays the creation of a drawn polygon of a test area, forwarding the posted body verbatim and answering with the status the service mirrored.
        /// <para>The body reaches the service byte-identically (<see cref="Query.ResponseRawAsync(HttpClient, HttpMethod, string, string, string?, CancellationToken)"/>): nothing the page posts is re-serialized, so the geometry and the identifiers travel exactly as sent, and only the status comes back - the created object with its server-assigned identifier and cell index is re-read from the area items afterwards.</para>
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>An <see cref="IActionResult"/> that is empty on success or carries the status the service answered with; 400 for an empty body, 401 without a session, 503 when the service answered nothing at all.</returns>
        [HttpPost("area/object")]
        public async Task<IActionResult> SetAreaObjectAsync(CancellationToken cancellationToken = default)
        {
            return await RelayAreaPostAsync(Constants.Default.OrtoAreaObjectsUri, cancellationToken);
        }

        /// <summary>
        /// Relays the removal of a drawn polygon of a test area and answers with the status the service mirrored.
        /// <para>The rules are the service's (<c>DELETE /gis/ortoarea/objects</c>): only the interpreter who drew the polygon may remove it, and only while no other interpreter has labelled it - 403 and 409 come back as they are, so the page can say which rule refused. The caller's own labels on the polygon go with it.</para>
        /// </summary>
        /// <param name="x_1">The X coordinate of the first bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="y_1">The Y coordinate of the first bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="x_2">The X coordinate of the second bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="y_2">The Y coordinate of the second bounding-box corner (EPSG:2180), or null when the query carries none.</param>
        /// <param name="objectId">The server-assigned identifier of the polygon, or null when the query carries none.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>An <see cref="IActionResult"/> that is empty on success or carries the status the service answered with; 400 when a corner or the identifier is missing, 401 without a session, 503 when the service answered nothing at all.</returns>
        [HttpDelete("area/object")]
        public async Task<IActionResult> RemoveAreaObjectAsync(double? x_1, double? y_1, double? x_2, double? y_2, string? objectId, CancellationToken cancellationToken = default)
        {
            string? tokenString = Request.Cookies[Constants.Default.UserTokenCookieName];
            if (string.IsNullOrWhiteSpace(tokenString))
            {
                return Unauthorized();
            }

            if (!x_1.HasValue || !y_1.HasValue || !x_2.HasValue || !y_2.HasValue || string.IsNullOrWhiteSpace(objectId))
            {
                return BadRequest();
            }

            HttpClient httpClient = httpClientFactory.CreateClient();

            // Hand-matched to [FromQuery(Name = "...")] of OrtoAreaController.RemoveObjectAsync: an unknown name
            // is silently ignored upstream, so these spellings are the contract.
            UrlBuilder urlBuilder = new(Constants.Default.OrtoAreaObjectsUri);
            urlBuilder = urlBuilder.AddParameter("x_1", x_1.Value);
            urlBuilder = urlBuilder.AddParameter("y_1", y_1.Value);
            urlBuilder = urlBuilder.AddParameter("x_2", x_2.Value);
            urlBuilder = urlBuilder.AddParameter("y_2", y_2.Value);
            urlBuilder = urlBuilder.AddParameter("objectid", objectId);

            WebAPIResponse? webAPIResponse = await httpClient.ResponseAsync(HttpMethod.Delete, urlBuilder.ToString(), tokenString, cancellationToken);
            if (webAPIResponse is null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            return StatusCode(webAPIResponse.StatusCode);
        }

        /// <summary>
        /// Relays one object &#215; epoch label of a test area, forwarding the posted body verbatim and answering with the status the service mirrored.
        /// <para>The state stays the integer code the page sent (0 present, 1 absent, 2 indeterminate) because the body is relayed byte-identically; the interpreter and the observation dates are stamped server-side from the bearer token and the covering sheets, never read from the body.</para>
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>An <see cref="IActionResult"/> that is empty on success or carries the status the service answered with; 400 for an empty body, 401 without a session, 503 when the service answered nothing at all.</returns>
        [HttpPost("area/label")]
        public async Task<IActionResult> SetAreaLabelAsync(CancellationToken cancellationToken = default)
        {
            return await RelayAreaPostAsync(Constants.Default.OrtoAreaLabelUri, cancellationToken);
        }

        /// <summary>
        /// Reads this request's raw JSON body and relays it to a GIS Web API POST route verbatim, presenting the session token and mirroring the status that came back.
        /// <para>The shared body of the two area POST relays (<see cref="SetAreaObjectAsync(CancellationToken)"/> and <see cref="SetAreaLabelAsync(CancellationToken)"/>): the cookie gate first, then a body that must already carry something to relay, then the byte-identical forward of <see cref="Query.ResponseRawAsync(HttpClient, HttpMethod, string, string, string?, CancellationToken)"/>.</para>
        /// </summary>
        /// <param name="requestUri">The GIS Web API URL to relay to. This value can be null.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>An <see cref="IActionResult"/> that is empty on success or carries the status the service answered with.</returns>
        private async Task<IActionResult> RelayAreaPostAsync(string? requestUri, CancellationToken cancellationToken)
        {
            string? tokenString = Request.Cookies[Constants.Default.UserTokenCookieName];
            if (string.IsNullOrWhiteSpace(tokenString))
            {
                return Unauthorized();
            }

            // No [FromBody]: the payload is relayed as the bytes it arrived as, never re-serialized. An empty
            // body is refused the way the service refuses one, before a round trip is spent on it.
            using StreamReader streamReader = new(Request.Body);
            string? body = await streamReader.ReadToEndAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body))
            {
                return BadRequest();
            }

            HttpClient httpClient = httpClientFactory.CreateClient();

            WebAPIResponse? webAPIResponse = await httpClient.ResponseRawAsync(HttpMethod.Post, requestUri, tokenString, body, cancellationToken);
            if (webAPIResponse is null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            return StatusCode(webAPIResponse.StatusCode);
        }

        /// <summary>
        /// Reads the photo years the GIS Web API holds for a building, presenting the session token the read requires.
        /// <para>Asked with <c>fallbackbyreference</c>, so a reference filed under a different county part than the one named still answers with the part that holds it.</para>
        /// </summary>
        /// <param name="httpClient">The HTTP client used for the request.</param>
        /// <param name="reference">The reference of the building.</param>
        /// <param name="countyId">The identifier of the county part the building is filed under. This value can be null.</param>
        /// <param name="tokenString">The session token to present to the service.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>The sorted years that hold a photo, or <see langword="null"/> when the service answered with none.</returns>
        private static async Task<List<short>?> YearsAsync(HttpClient httpClient, string? reference, int? countyId, string? tokenString, CancellationToken cancellationToken)
        {
            UrlBuilder urlBuilder = new(Constants.Default.OrtoDataYearsByReferenceUri);
            urlBuilder = urlBuilder.AddParameter("reference", reference);
            if (countyId.HasValue)
            {
                urlBuilder = urlBuilder.AddParameter("countyid", countyId.Value);
            }
            urlBuilder = urlBuilder.AddParameter("fallbackbyreference", true);

            WebAPIResponse? webAPIResponse = await httpClient.ResponseAsync(HttpMethod.Get, urlBuilder.ToString(), tokenString, cancellationToken);
            if (webAPIResponse?.StatusCode != StatusCodes.Status200OK || string.IsNullOrWhiteSpace(webAPIResponse.Json))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<List<short>>(webAPIResponse.Json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
