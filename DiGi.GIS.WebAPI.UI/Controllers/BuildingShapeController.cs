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
    /// Provides the Building Shape labelling page (DiGi.GIS.WebAPI.UI#86) and the endpoints it uses, each relaying to <c>gis/buildingshape</c> of the GIS Web API with the visitor's session presented as a bearer token.
    /// <para>The page is part of the Building Shape Accuracy feature (DiGi.GIS#12): a signed-in annotator labels the footprints of the fixed sample <see cref="Constants.Default.BuildingShapeSampleKey"/> one by one, blind to the solver's answer. The page action redirects an anonymous visitor to the sign-in page, and every other action answers 401, which the page script answers with one refresh attempt and then a sign-out (<c>wwwroot/js/user.js</c>).</para>
    /// <para>The relays store nothing and reinterpret nothing but one status: an upstream 404 of the next-item read means the annotator has labelled every item, answered here as the empty object <c>{}</c> so the page shows its finished state instead of an error. Every other status passes through; a service that answered nothing at all is 503.</para>
    /// </summary>
    [Route("[controller]")]
    public class BuildingShapeController : Controller
    {
        private readonly IHttpClientFactory httpClientFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="BuildingShapeController"/> class.
        /// </summary>
        /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/> used to create HTTP clients.</param>
        public BuildingShapeController(IHttpClientFactory httpClientFactory)
        {
            this.httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Returns the Building Shape labelling page, or redirects an anonymous visitor to the sign-in page.
        /// </summary>
        /// <returns>An <see cref="IActionResult"/> holding the labelling view, or a redirect to the sign-in page.</returns>
        [HttpGet("")]
        public IActionResult Start()
        {
            string? tokenString = Request.Cookies[Constants.Default.UserTokenCookieName];
            if (string.IsNullOrWhiteSpace(tokenString))
            {
                // Only an address inside this application is ever returned to (UserController.Login), so a crafted
                // address cannot turn this redirect into an open redirector.
                string returnUrl = HttpContext.Request.Path + HttpContext.Request.QueryString;
                string loginUrl = Url.Content("~/login") + "?returnUrl=" + Uri.EscapeDataString(Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Content("~/"));

                return Redirect(loginUrl);
            }

            return View();
        }

        /// <summary>
        /// Relays the annotator's next unlabelled footprint of the sample, without the solver shape.
        /// </summary>
        /// <param name="afterOrdinal">The ordinal of the item last seen or skipped, so the search starts after it and wraps around; omitted to start from the beginning.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>An <see cref="IActionResult"/> holding the item as the service answered it, <c>{}</c> when every item is labelled, 401 without a session, 503 when the service answered nothing at all, or the status the service answered with.</returns>
        [HttpGet("next")]
        public async Task<IActionResult> GetNextAsync([FromQuery(Name = "afterordinal")] int? afterOrdinal, CancellationToken cancellationToken = default)
        {
            UrlBuilder urlBuilder = new(Constants.Default.BuildingShapeNextSampleUri);
            urlBuilder = urlBuilder.AddParameter("samplekey", Constants.Default.BuildingShapeSampleKey);
            if (afterOrdinal.HasValue)
            {
                urlBuilder = urlBuilder.AddParameter("afterordinal", afterOrdinal.Value);
            }

            return await RelayGetAsync(urlBuilder.ToString(), true, cancellationToken);
        }

        /// <summary>
        /// Relays the annotator's progress through the sample as <c>{ Labelled, Total }</c>.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>An <see cref="IActionResult"/> holding the counts, 401 without a session, 503 when the service answered nothing at all, or the status the service answered with.</returns>
        [HttpGet("progress")]
        public async Task<IActionResult> GetProgressAsync(CancellationToken cancellationToken = default)
        {
            UrlBuilder urlBuilder = new(Constants.Default.BuildingShapeProgressUri);
            urlBuilder = urlBuilder.AddParameter("samplekey", Constants.Default.BuildingShapeSampleKey);

            return await RelayGetAsync(urlBuilder.ToString(), false, cancellationToken);
        }

        /// <summary>
        /// Relays every label of the sample, by every annotator, for the export.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>An <see cref="IActionResult"/> holding the labels as the service answered them, 401 without a session, 503 when the service answered nothing at all, or the status the service answered with.</returns>
        [HttpGet("labels")]
        public async Task<IActionResult> GetLabelsAsync(CancellationToken cancellationToken = default)
        {
            UrlBuilder urlBuilder = new(Constants.Default.BuildingShapeLabelsUri);
            urlBuilder = urlBuilder.AddParameter("samplekey", Constants.Default.BuildingShapeSampleKey);

            return await RelayGetAsync(urlBuilder.ToString(), false, cancellationToken);
        }

        /// <summary>
        /// Relays every item of the sample with its solver shape, for the export only; the page never reads it while labelling.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>An <see cref="IActionResult"/> holding the items as the service answered them, 401 without a session, 503 when the service answered nothing at all, or the status the service answered with.</returns>
        [HttpGet("sampleitems")]
        public async Task<IActionResult> GetSampleItemsAsync(CancellationToken cancellationToken = default)
        {
            UrlBuilder urlBuilder = new(Constants.Default.BuildingShapeSampleItemsUri);
            urlBuilder = urlBuilder.AddParameter("samplekey", Constants.Default.BuildingShapeSampleKey);

            return await RelayGetAsync(urlBuilder.ToString(), false, cancellationToken);
        }

        /// <summary>
        /// Relays the annotator's label of one sample item, forwarding the posted body verbatim and answering with the status the service mirrored.
        /// <para>The interpreter and the timestamp are stamped by the service from the bearer token and its clock, never read from the body.</para>
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>An <see cref="IActionResult"/> that is empty on success or carries the status the service answered with; 400 for an empty body, 401 without a session, 503 when the service answered nothing at all.</returns>
        [HttpPost("label")]
        public async Task<IActionResult> SetLabelAsync(CancellationToken cancellationToken = default)
        {
            string? tokenString = Request.Cookies[Constants.Default.UserTokenCookieName];
            if (string.IsNullOrWhiteSpace(tokenString))
            {
                return Unauthorized();
            }

            // No [FromBody]: the payload is relayed as the bytes it arrived as, never re-serialized.
            using StreamReader streamReader = new(Request.Body);
            string? body = await streamReader.ReadToEndAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body))
            {
                return BadRequest();
            }

            HttpClient httpClient = httpClientFactory.CreateClient();

            WebAPIResponse? webAPIResponse = await httpClient.ResponseRawAsync(HttpMethod.Post, Constants.Default.BuildingShapeLabelUri, tokenString, body, cancellationToken);
            if (webAPIResponse is null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            return StatusCode(webAPIResponse.StatusCode);
        }

        /// <summary>
        /// Reads the newest orthophoto of a footprint as JPEG bytes, the context drawn under the outline.
        /// <para>The photo years are read first (with the reference fallback) and the newest one is fetched; a footprint without photos answers 404, which the page turns into an outline on a plain background.</para>
        /// </summary>
        /// <param name="countyId">The county part the footprint is filed under, or null when the query carries none.</param>
        /// <param name="reference">The reference of the footprint.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>An <see cref="IActionResult"/> holding the image, 404 when there is none, 400 without a reference, or 401 without a session.</returns>
        [HttpGet("image")]
        public async Task<IActionResult> GetImageAsync([FromQuery(Name = "countyid")] int? countyId, [FromQuery(Name = "reference")] string? reference, CancellationToken cancellationToken = default)
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

            UrlBuilder urlBuilder_Years = new(Constants.Default.OrtoDataYearsByReferenceUri);
            urlBuilder_Years = urlBuilder_Years.AddParameter("reference", reference);
            if (countyId.HasValue)
            {
                urlBuilder_Years = urlBuilder_Years.AddParameter("countyid", countyId.Value);
            }
            urlBuilder_Years = urlBuilder_Years.AddParameter("fallbackbyreference", true);

            WebAPIResponse? webAPIResponse = await httpClient.ResponseAsync(HttpMethod.Get, urlBuilder_Years.ToString(), tokenString, cancellationToken);
            if (webAPIResponse?.StatusCode != StatusCodes.Status200OK || string.IsNullOrWhiteSpace(webAPIResponse.Json))
            {
                return NotFound();
            }

            List<short>? years;
            try
            {
                years = JsonSerializer.Deserialize<List<short>>(webAPIResponse.Json);
            }
            catch (JsonException)
            {
                return NotFound();
            }

            if (years is null || years.Count == 0)
            {
                return NotFound();
            }

            UrlBuilder urlBuilder_Image = new(Constants.Default.OrtoDataImageByReferenceUri);
            urlBuilder_Image = urlBuilder_Image.AddParameter("reference", reference);
            urlBuilder_Image = urlBuilder_Image.AddParameter("year", years.Max());
            if (countyId.HasValue)
            {
                urlBuilder_Image = urlBuilder_Image.AddParameter("countyid", countyId.Value);
            }
            urlBuilder_Image = urlBuilder_Image.AddParameter("fallbackbyreference", true);

            byte[]? bytes = await httpClient.BytesAsync(urlBuilder_Image.ToString(), cancellationToken);
            if (bytes is null || bytes.Length == 0)
            {
                return NotFound();
            }

            return File(bytes, "image/jpeg");
        }

        /// <summary>
        /// Relays one session-gated GET to the GIS Web API and answers its JSON body verbatim with its status.
        /// </summary>
        /// <param name="requestUri">The GIS Web API URL to relay to.</param>
        /// <param name="notFoundAsEmpty">True to answer an upstream 404 as the empty object <c>{}</c> (the next-item read's "every item labelled").</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>The relayed answer: the body on 200, <c>{}</c> on a 404 when <paramref name="notFoundAsEmpty"/> is set, 401 without a session, 503 when the service answered nothing at all, otherwise the upstream status.</returns>
        private async Task<IActionResult> RelayGetAsync(string requestUri, bool notFoundAsEmpty, CancellationToken cancellationToken)
        {
            string? tokenString = Request.Cookies[Constants.Default.UserTokenCookieName];
            if (string.IsNullOrWhiteSpace(tokenString))
            {
                return Unauthorized();
            }

            HttpClient httpClient = httpClientFactory.CreateClient();

            WebAPIResponse? webAPIResponse = await httpClient.ResponseAsync(HttpMethod.Get, requestUri, tokenString, cancellationToken);
            if (webAPIResponse is null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (notFoundAsEmpty && webAPIResponse.StatusCode == StatusCodes.Status404NotFound)
            {
                return Content("{}", "application/json");
            }

            if (webAPIResponse.StatusCode != StatusCodes.Status200OK)
            {
                return StatusCode(webAPIResponse.StatusCode);
            }

            return Content(string.IsNullOrWhiteSpace(webAPIResponse.Json) ? "null" : webAPIResponse.Json, "application/json");
        }
    }
}
