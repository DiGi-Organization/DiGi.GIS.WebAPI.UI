using DiGi.Analytical.Building.Classes;
using DiGi.Analytical.Building.Solar;
using DiGi.Core.Classes;
using DiGi.Core.Enums;
using DiGi.Core.Interfaces;
using DiGi.EPW.Classes;
using DiGi.Geometry.Planar.Classes;
using DiGi.Geometry.Spatial.Classes;
using DiGi.GIS.WebAPI.UI.Classes;
using DiGi.GIS.WebAPI.UI.Enums;
using DiGi.GIS.WebAPI.UI.ViewModels;
using DiGi.Solar.Classes;
using DiGi.WebAPI.Classes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DiGi.GIS.WebAPI.UI.Controllers
{
    /// <summary>
    /// Solar calculations for the 3D viewers: the sun position for the Lighting panel, and the annual solar radiation on the external walls and roofs of one building, calculated on this host's CPU with DiGi.Solar over one EPW year, with the building itself and its neighbours casting shade.
    /// <para>The radiation is shown by the "Solar radiation" panel of the Building Viewer (DiGi.GIS.WebAPI.UI#66): <see cref="GetViewByBuildingModelIdAsync(long, int?, double?, CancellationToken)"/> and <see cref="GetJobView(Guid)"/> answer a <see cref="SolarRadiationViewModel"/> that recolours the receiving surfaces of the scene already loaded, so no scene of its own is streamed.</para>
    /// <para>The radiation routes answer the same refusals: 400 for a neighbour radius outside (0, <see cref="Constants.Default.SolarSurroundingRadiusMax"/>], 204 when the building or its weather file is not found, 422 when the building cannot be located or has no closed external envelope, 413 above <see cref="Constants.Default.SolarReceiverCountMax"/> receiving surfaces or <see cref="Constants.Default.SolarCasterTriangleCountMax"/> caster triangles, 502 when the neighbours cannot be read, 503 with <c>Retry-After</c> when the solve gate stays busy for <see cref="Constants.Default.SolarSolveGateWaitSeconds"/> seconds, and 499 / 504 / 500 for a client cancel, an upstream timeout and any other failure.</para>
    /// <para><b>Stored results</b> (DiGi.GIS.WebAPI.UI#73): before any neighbour is fetched, the routes read the result stored for the building model, the rounded radius (<see cref="Query.SolarRadiusKey(double)"/>) and <see cref="Constants.Default.SolarSolverVersion"/>. A summary stored for the same EPW file is answered as it is; one for another weather source is rebuilt from its stored shading factors; either way nothing is solved and no ceiling applies, and the job route answers 200 with the view instead of queuing a job. Otherwise the building is solved as before and, when the request carries the <see cref="Constants.Default.UserTokenCookieName"/> cookie, its result is stored on behalf of that user; a failed save is logged and never fails the response. The view says which of these happened (<see cref="SolarRadiationViewModel.Source"/>, <see cref="SolarRadiationViewModel.Saved"/>).</para>
    /// <para>Buildings above those ceilings are calculated as background jobs (<c>solar/jobs</c>, DiGi.GIS.WebAPI.UI#60): the POST runs the same checks with the job ceilings (<see cref="Constants.Default.SolarJobReceiverCountMax"/>, <see cref="Constants.Default.SolarJobCasterTriangleCountMax"/>), queues the prepared calculation and answers 202, or 503 with <c>Retry-After</c> when <see cref="Constants.Default.SolarJobQueueLengthMax"/> jobs are already waiting. The job routes answer 404 for an unknown or expired job and 409 for results of a job that has not completed.</para>
    /// </summary>
    [Route("[controller]")]
    public class SolarController : Controller
    {
        private readonly IHttpClientFactory httpClientFactory;
        private readonly ILogger<SolarController> logger;
        private readonly SemaphoreSlim semaphoreSlim;
        private readonly SolarJobQueue solarJobQueue;

        /// <summary>
        /// Initializes a new instance of the <see cref="SolarController"/> class.
        /// </summary>
        /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/> used to create <see cref="HttpClient"/> instances.</param>
        /// <param name="semaphoreSlim">The gate shared by every solar radiation solve on this host, registered under <see cref="Constants.Default.SolarSolveGateKey"/>.</param>
        /// <param name="solarJobQueue">The background solar radiation jobs of this host.</param>
        /// <param name="logger">The logger receiving one line per solar radiation request.</param>
        public SolarController(IHttpClientFactory httpClientFactory, [FromKeyedServices(Constants.Default.SolarSolveGateKey)] SemaphoreSlim semaphoreSlim, SolarJobQueue solarJobQueue, ILogger<SolarController> logger)
        {
            this.httpClientFactory = httpClientFactory;
            this.semaphoreSlim = semaphoreSlim;
            this.solarJobQueue = solarJobQueue;
            this.logger = logger;
        }

        /// <summary>
        /// Gets or sets how long a synchronous request waits for the solve gate before it is refused with a 503; <see cref="Constants.Default.SolarSolveGateWaitSeconds"/> outside the tests.
        /// </summary>
        internal TimeSpan SolveGateWait { get; set; } = TimeSpan.FromSeconds(Constants.Default.SolarSolveGateWaitSeconds);

        /// <summary>
        /// Calculates the annual solar radiation on the external walls and roofs of a building: one <see cref="SurfaceSolarRadiationResult"/> per receiving surface, over one EPW year, with the building itself and its neighbours within <paramref name="radius"/> casting shade.
        /// </summary>
        /// <param name="id">The unique identifier of the building.</param>
        /// <param name="countyId">The optional unique identifier of the county associated with the building.</param>
        /// <param name="radius">The neighbour radius in metres, measured from the edge of the footprint; omitted means <see cref="Constants.Default.SolarSurroundingRadius"/>.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>The results as DiGi JSON (<c>Core.Convert.ToSystem_String</c>, readable by <c>Core.Convert.ToDiGi</c>), or one of the refusals listed on <see cref="SolarController"/>.</returns>
        [HttpGet("radiationbybuildingmodelid")]
        public async Task<IActionResult> GetRadiationByBuildingModelIdAsync([FromQuery(Name = "id")] long id, [FromQuery(Name = "countyid")] int? countyId, [FromQuery(Name = "radius")] double? radius, CancellationToken cancellationToken = default)
        {
            return await SolveAsync(id, countyId, radius, (buildingModel, surfaceSolarRadiationResults, radius_Value, ePWFile, center, solarRadiationOrigin) =>
            {
                return Content(Core.Convert.ToSystem_String(surfaceSolarRadiationResults) ?? "[]", "application/json");
            }, cancellationToken);
        }

        /// <summary>
        /// Calculates the annual solar radiation on the external walls and roofs of a building, like <see cref="GetRadiationByBuildingModelIdAsync(long, int?, double?, CancellationToken)"/>, and answers it in the shape the Building Viewer's "Solar radiation" panel applies to its scene in place: one <see cref="SolarSurfaceViewModel"/> per receiving surface, referenced exactly like the node of its component on <c>buildingmodel/buildingmodelbyid</c> (root <c>PostgreSQL.Create.Reference(buildingModel, null, countyId)</c>), with its irradiation colour and its <see cref="SurfaceSolarRadiationResult"/>.
        /// </summary>
        /// <param name="id">The unique identifier of the building.</param>
        /// <param name="countyId">The optional unique identifier of the county associated with the building.</param>
        /// <param name="radius">The neighbour radius in metres, measured from the edge of the footprint; omitted means <see cref="Constants.Default.SolarSurroundingRadius"/>.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>200 with the <see cref="SolarRadiationViewModel"/>, or one of the refusals listed on <see cref="SolarController"/>.</returns>
        [HttpGet("viewbybuildingmodelid")]
        public async Task<IActionResult> GetViewByBuildingModelIdAsync([FromQuery(Name = "id")] long id, [FromQuery(Name = "countyid")] int? countyId, [FromQuery(Name = "radius")] double? radius, CancellationToken cancellationToken = default)
        {
            return await SolveAsync(id, countyId, radius, (buildingModel, surfaceSolarRadiationResults, radius_Value, ePWFile, center, solarRadiationOrigin) =>
            {
                return ViewResult(buildingModel, surfaceSolarRadiationResults, countyId, radius_Value, StationName(ePWFile), StationUrl(center), solarRadiationOrigin);
            }, cancellationToken);
        }

        /// <summary>
        /// Queues the annual solar radiation of a building as a background job, for buildings above the synchronous ceilings. The building, its neighbours and its weather are fetched and checked in this request, with the job ceilings (<see cref="Constants.Default.SolarJobReceiverCountMax"/>, <see cref="Constants.Default.SolarJobCasterTriangleCountMax"/>), so every refusal is immediate; the solve runs later, one job at a time, behind the gate shared with the synchronous routes.
        /// </summary>
        /// <param name="id">The unique identifier of the building.</param>
        /// <param name="countyId">The optional unique identifier of the county associated with the building.</param>
        /// <param name="radius">The neighbour radius in metres, measured from the edge of the footprint; omitted means <see cref="Constants.Default.SolarSurroundingRadius"/>.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation; it does not reach the queued job.</param>
        /// <returns>202 with the <see cref="SolarJobViewModel"/> of the job and its <c>Location</c> (<c>solar/jobs/{jobId}</c>); 503 with <c>Retry-After</c> when the queue is full; otherwise one of the refusals listed on <see cref="SolarController"/>.</returns>
        [HttpPost("jobs")]
        public async Task<IActionResult> PostJobAsync([FromQuery(Name = "id")] long id, [FromQuery(Name = "countyid")] int? countyId, [FromQuery(Name = "radius")] double? radius, CancellationToken cancellationToken = default)
        {
            double radius_Requested = radius ?? Constants.Default.SolarSurroundingRadius;
            double radius_Value = Query.SolarRadiusKey(radius_Requested);
            if (!IsValidRadius(radius_Requested) || !IsValidRadius(radius_Value))
            {
                return RadiusBadRequest();
            }

            // A full queue is refused before the upstream requests and the shading model, which would
            // compete with the running job for nothing; TryEnqueue checks again for a race.
            if (solarJobQueue.QueuedCount() >= Constants.Default.SolarJobQueueLengthMax)
            {
                return QueueFull();
            }

            // A stored result is answered at once, 200 with its view, and no job is queued.
            return await PrepareAsync(id, countyId, radius_Value, true, (buildingModel, surfaceSolarRadiationResults, ePWFile, center, solarRadiationOrigin) =>
            {
                return ViewResult(buildingModel, surfaceSolarRadiationResults, countyId, radius_Value, StationName(ePWFile), StationUrl(center), solarRadiationOrigin);
            }, (buildingModel, normals, ePWFile, center, shadingModel, casterTriangleCount, ePWFileId) =>
            {
                // The calculation holds its inputs until it has run; nothing is shared with another job. The storage step
                // reads the surfaces the calculation leaves behind and carries the visitor's token until it has run.
                List<GIS.Classes.SurfaceSolarRadiation>? surfaceSolarRadiations = null;
                Func<List<SurfaceSolarRadiationResult>?> calculation = () =>
                {
                    surfaceSolarRadiations = shadingModel.SurfaceSolarRadiations(normals, ePWFile, SolarShadingSolverOptions(), message => logger.LogInformation("{Message}", message));
                    return Create.SurfaceSolarRadiationResults(surfaceSolarRadiations);
                };

                string? tokenString = Request.Cookies[Constants.Default.UserTokenCookieName];
                Func<CancellationToken, Task<bool>>? storage = null;
                if (tokenString is not null && countyId is not null && ePWFileId is not null)
                {
                    int countyId_Value = countyId.Value;
                    int ePWFileId_Value = ePWFileId.Value;
                    storage = token => StoreAsync(buildingModel.BuildingSolarRadiationResult(countyId_Value, radius_Value, ePWFileId_Value, surfaceSolarRadiations), tokenString, id, token);
                }

                SolarJob solarJob = new(Guid.NewGuid(), id, countyId, radius_Value, normals.Count, buildingModel, StationName(ePWFile), StationUrl(center), calculation, storage);
                if (!solarJobQueue.TryEnqueue(solarJob))
                {
                    return Task.FromResult(QueueFull());
                }

                logger.LogInformation("Solar radiation job {JobId} queued for building {Id} (county {CountyId}): radius {Radius} m, {ReceiverCount} receivers, {CasterTriangleCount} caster triangles.", solarJob.Id, id, countyId, radius_Value, normals.Count, casterTriangleCount);

                return Task.FromResult<IActionResult>(Accepted($"{Request.PathBase}/solar/jobs/{solarJob.Id.ToString("D", CultureInfo.InvariantCulture)}", solarJobQueue.SolarJobViewModel(solarJob.Id)));
            }, cancellationToken);
        }

        /// <summary>
        /// Gets the state of a background solar radiation job: its status, its place in the queue, the seconds spent in its current state and, for a failed job, the error text.
        /// </summary>
        /// <param name="jobId">The unique identifier of the job.</param>
        /// <returns>200 with the <see cref="SolarJobViewModel"/> of the job; 404 when it is unknown or has expired.</returns>
        [HttpGet("jobs/{jobId:guid}")]
        public IActionResult GetJob([FromRoute(Name = "jobId")] Guid jobId)
        {
            SolarJobViewModel? solarJobViewModel = solarJobQueue.SolarJobViewModel(jobId);
            if (solarJobViewModel is null)
            {
                return JobNotFound(jobId);
            }

            return Ok(solarJobViewModel);
        }

        /// <summary>
        /// Gets the results of a completed background solar radiation job, in the shape of <see cref="GetRadiationByBuildingModelIdAsync(long, int?, double?, CancellationToken)"/>.
        /// </summary>
        /// <param name="jobId">The unique identifier of the job.</param>
        /// <returns>The results as DiGi JSON; 409 when the job has not completed; 404 when it is unknown or has expired.</returns>
        [HttpGet("jobs/{jobId:guid}/result")]
        public IActionResult GetJobResult([FromRoute(Name = "jobId")] Guid jobId)
        {
            SolarJob? solarJob = CompletedJob(jobId, out IActionResult? actionResult);
            if (solarJob?.SurfaceSolarRadiationResults is not List<SurfaceSolarRadiationResult> surfaceSolarRadiationResults)
            {
                return actionResult ?? JobNotFound(jobId);
            }

            return Content(Core.Convert.ToSystem_String(surfaceSolarRadiationResults) ?? "[]", "application/json");
        }

        /// <summary>
        /// Gets the results of a completed background solar radiation job in the shape of <see cref="GetViewByBuildingModelIdAsync(long, int?, double?, CancellationToken)"/>, built from the stored results without solving again.
        /// </summary>
        /// <param name="jobId">The unique identifier of the job.</param>
        /// <returns>200 with the <see cref="SolarRadiationViewModel"/>; 409 when the job has not completed; 404 when it is unknown or has expired.</returns>
        [HttpGet("jobs/{jobId:guid}/view")]
        public IActionResult GetJobView([FromRoute(Name = "jobId")] Guid jobId)
        {
            SolarJob? solarJob = CompletedJob(jobId, out IActionResult? actionResult);
            if (solarJob?.SurfaceSolarRadiationResults is not List<SurfaceSolarRadiationResult> surfaceSolarRadiationResults || solarJob.BuildingModel is not BuildingModel buildingModel)
            {
                return actionResult ?? JobNotFound(jobId);
            }

            return ViewResult(buildingModel, surfaceSolarRadiationResults, solarJob.CountyId, solarJob.Radius, solarJob.StationName, solarJob.StationUrl, new SolarRadiationOrigin(SolarRadiationSource.Solved, null, solarJob.Saved, solarJob.SignedIn));
        }

        /// <summary>
        /// Cancels a background solar radiation job: a queued job is never calculated; a running calculation cannot be interrupted, so it finishes and its results are discarded.
        /// </summary>
        /// <param name="jobId">The unique identifier of the job.</param>
        /// <returns>204; 404 when the job is unknown or has expired.</returns>
        [HttpDelete("jobs/{jobId:guid}")]
        public IActionResult DeleteJob([FromRoute(Name = "jobId")] Guid jobId)
        {
            if (!solarJobQueue.Cancel(jobId))
            {
                return JobNotFound(jobId);
            }

            logger.LogInformation("Solar radiation job {JobId} cancelled.", jobId);

            return NoContent();
        }

        /// <summary>
        /// [TEMPORARY] Calculates the sun position for a world location and a local date and time, for the 3D viewer Lighting panel.
        /// <para>Hosted locally until a DiGi.Solar backed endpoint is available on the central GIS Web API. The route contract (solar/sundirection) is final - when the central endpoint exists this action becomes a proxy like the other actions in this project, and the consuming frontend (gltf-viewer.js) stays unchanged.</para>
        /// </summary>
        /// <param name="x">The X coordinate in the EPSG:2180 coordinate system [m].</param>
        /// <param name="y">The Y coordinate in the EPSG:2180 coordinate system [m].</param>
        /// <param name="date">The local calendar date in the yyyy-MM-dd format.</param>
        /// <param name="hour">The local time of day as a decimal hour in the 0-24 range.</param>
        /// <returns>JSON with the true solar angles: azimuth [deg] (0 = north, clockwise) and altitude [deg] above the horizon (negative at night).</returns>
        [HttpGet("sundirection")]
        public IActionResult GetSunDirection([FromQuery(Name = "x")] double x, [FromQuery(Name = "y")] double y, [FromQuery(Name = "date")] string? date, [FromQuery(Name = "hour")] double hour)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(hour) || hour < 0 || hour > 24)
            {
                return BadRequest();
            }

            if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dateTime))
            {
                return BadRequest();
            }

            dateTime = dateTime.AddHours(hour);

            // GIS qualifier: this project's own Query class (DiGi.GIS.WebAPI.UI.Query) shadows
            // DiGi.GIS.Query in the enclosing-namespace lookup.
            Coordinates? coordinates = GIS.Query.Coordinates(new Point2D(x, y));
            if (coordinates is null)
            {
                return NoContent();
            }

            // EPSG:2180 scenes are Polish, so the local time zone is CET/CEST. The offset follows
            // the daylight saving state of the requested date, because this endpoint reports
            // wall-clock sun angles for a single date. A shading model, in contrast, holds one
            // fixed offset (UTC.Plus0100, no DST) for its whole solve - see UpdateBuildingInformation.
            // The identifier is the Windows form; .NET resolves it on any platform through the built-in IANA mapping.
            TimeZoneInfo timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
            UTC uTC = timeZoneInfo.GetUtcOffset(dateTime).TotalHours == 2 ? UTC.Plus0200 : UTC.Plus0100;

            Vector3D? vector3D = Solar.Query.SunDirection(coordinates, uTC, dateTime, true);
            if (vector3D is null)
            {
                return NoContent();
            }

            // SunDirection returns the direction sunlight travels, built from the solar angles as
            // x = cos(el) * cos(az + 90deg), y = -cos(el) * sin(az + 90deg), z = -sin(el);
            // inverted here back to the true solar angles the frontend contract expects.
            double altitude = Math.Asin(Math.Clamp(-vector3D.Z, -1, 1)) * (180.0 / Math.PI);
            double azimuth = (Math.Atan2(-vector3D.Y, vector3D.X) * (180.0 / Math.PI)) - 90.0;
            azimuth = ((azimuth % 360.0) + 360.0) % 360.0;

            return Ok(new { azimuth, altitude });
        }

        private static bool IsValidRadius(double radius)
        {
            return double.IsFinite(radius) && radius > 0 && radius <= Constants.Default.SolarSurroundingRadiusMax;
        }

        private static string? StationName(EPWFile ePWFile)
        {
            Location? location = ePWFile.Location;
            if (location is null)
            {
                return null;
            }

            List<string> values = [];
            foreach (string? value in new string?[] { location.City, location.Source, string.IsNullOrWhiteSpace(location.WHO) ? null : $"WMO {location.WHO}" })
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value.Trim());
                }
            }

            return values.Count == 0 ? null : string.Join(", ", values);
        }

        // The station page of the EPW file the calculation reads, relative to the application root. It is resolved per
        // response (Request.PathBase), so a job stores it without knowing the base of the request that reads it.
        private static string StationUrl(Point2D center)
        {
            return $"/epwfile/item?x={center.X.ToString(CultureInfo.InvariantCulture)}&y={center.Y.ToString(CultureInfo.InvariantCulture)}";
        }

        // The view of a solved building, shared by the synchronous route and the job route. The root reference is the
        // one of the Building Viewer scene (buildingmodel/buildingmodelbyid), so each surface names the node it recolours.
        private IActionResult ViewResult(BuildingModel buildingModel, List<SurfaceSolarRadiationResult> surfaceSolarRadiationResults, int? countyId, double radius, string? stationName, string? stationUrl, SolarRadiationOrigin? solarRadiationOrigin)
        {
            IReference? reference = PostgreSQL.Create.Reference(buildingModel, null, countyId);

            SolarRadiationViewModel? solarRadiationViewModel = buildingModel.SolarRadiationViewModel(surfaceSolarRadiationResults, reference, radius, stationName, stationUrl is null ? null : $"{Request.PathBase}{stationUrl}", solarRadiationOrigin);
            if (solarRadiationViewModel is null)
            {
                return NoContent();
            }

            return Ok(solarRadiationViewModel);
        }

        // A job whose results can be read: the job, or null with the refusal - 404 for an unknown or expired
        // job, 409 for one that has not completed. The state is read under the queue's lock, and a completed
        // job's results never change afterwards.
        private SolarJob? CompletedJob(Guid jobId, out IActionResult? actionResult)
        {
            SolarJobViewModel? solarJobViewModel = solarJobQueue.SolarJobViewModel(jobId);
            if (solarJobViewModel is null)
            {
                actionResult = JobNotFound(jobId);
                return null;
            }

            if (solarJobViewModel.Status != nameof(SolarJobStatus.Completed))
            {
                string message = $"Job {jobId:D} is {solarJobViewModel.Status.ToLowerInvariant()}; its results are available once it has completed.";
                if (!string.IsNullOrWhiteSpace(solarJobViewModel.Error))
                {
                    message += $" {solarJobViewModel.Error}";
                }

                actionResult = Conflict(new List<string>() { message });
                return null;
            }

            SolarJob? solarJob = solarJobQueue.SolarJob(jobId);
            actionResult = solarJob is null ? JobNotFound(jobId) : null;
            return solarJob;
        }

        private IActionResult JobNotFound(Guid jobId)
        {
            return NotFound(new List<string>() { string.Format(CultureInfo.InvariantCulture, "Job {0:D} is unknown or has expired: results are kept for {1} minutes after a job finishes, and jobs are lost when the server restarts.", jobId, Constants.Default.SolarJobResultRetentionMinutes) });
        }

        private IActionResult PayloadTooLarge(string message)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new List<string>() { message });
        }

        private IActionResult QueueFull()
        {
            return ServiceUnavailable(Constants.Default.SolarJobRetryAfterSeconds, string.Format(CultureInfo.InvariantCulture, "{0} background calculations are already waiting, the most the server queues. Try again in a few minutes.", Constants.Default.SolarJobQueueLengthMax));
        }

        private IActionResult RadiusBadRequest()
        {
            return BadRequest(new List<string>() { string.Format(CultureInfo.InvariantCulture, "The neighbour radius must be a number above 0 and at most {0} metres.", Constants.Default.SolarSurroundingRadiusMax) });
        }

        private IActionResult ServiceUnavailable(int retryAfterSeconds, string message)
        {
            Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new List<string>() { message });
        }

        private static ShadingSolverOptions SolarShadingSolverOptions()
        {
            return new ShadingSolverOptions()
            {
                AngleTolerance = Constants.Default.SolarAngleTolerance,
            };
        }

        // The pipeline shared by the synchronous routes and the job route, up to the solve: fetch and stamp the building,
        // fetch its weather, answer from a stored result when one exists (respondStored, without any ceiling, neighbour or
        // solve - a stored building is answered whatever its size), and otherwise refuse what the ceilings do not cover
        // (the job ceilings when background is true), fetch the neighbours, build the shading model and hand everything to
        // next - a synchronous solve, or a queued job. The weather is fetched before the neighbours because the stored
        // lookup needs its id; a missing weather file still answers 204 where it did before, after the neighbours.
        private async Task<IActionResult> PrepareAsync(long id, int? countyId, double radius, bool background, Func<BuildingModel, List<SurfaceSolarRadiationResult>, EPWFile, Point2D, SolarRadiationOrigin, IActionResult> respondStored, Func<BuildingModel, Dictionary<string, Vector3D>, EPWFile, Point2D, ShadingModel, int, int?, Task<IActionResult>> next, CancellationToken cancellationToken)
        {
            try
            {
                HttpClient httpClient = httpClientFactory.CreateClient();

                // Every fetch below answers null for a cancelled request too, so the token is checked
                // after each one: a client that went away is a 499, not a missing building.
                BuildingModel? buildingModel = await httpClient.BuildingModelAsync(id, countyId, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (buildingModel is null)
                {
                    return NoContent();
                }

                // GIS qualifier: this project's own Modify class shadows DiGi.GIS.Analytical.Modify.
                if (!GIS.Analytical.Modify.UpdateBuildingInformation(buildingModel))
                {
                    return UnprocessableEntity(new List<string>() { $"Building {id} could not be located, so the sun cannot be positioned for it." });
                }

                Dictionary<string, Vector3D>? normals = buildingModel.SolarReceiverNormals();
                if (normals is null || normals.Count == 0)
                {
                    return UnprocessableEntity(new List<string>() { $"Building {id} has no closed external envelope (a degenerate model), so its walls and roofs have no outward side to receive the sun." });
                }

                // The neighbour circle is centred on the footprint and reaches the radius beyond its farthest corner.
                Circle2D? circle2D = buildingModel.TerrainCircle(radius, 0);
                if (circle2D?.Center is not Point2D center)
                {
                    return UnprocessableEntity(new List<string>() { $"Building {id} has no geometry." });
                }

                (int? Id, EPWFile EPWFile)? ePWFileWithId = await httpClient.EPWFileAsync(center, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (ePWFileWithId is not null)
                {
                    IActionResult? actionResult_Stored = await StoredAsync(httpClient, id, countyId, radius, buildingModel, ePWFileWithId.Value.EPWFile, ePWFileWithId.Value.Id, center, respondStored, cancellationToken);
                    if (actionResult_Stored is not null)
                    {
                        return actionResult_Stored;
                    }
                }

                if (normals.Count > (background ? Constants.Default.SolarJobReceiverCountMax : Constants.Default.SolarReceiverCountMax))
                {
                    return PayloadTooLarge(background || normals.Count > Constants.Default.SolarJobReceiverCountMax
                        ? $"Building {id} has {normals.Count} external walls and roofs; a background calculation covers at most {Constants.Default.SolarJobReceiverCountMax}."
                        : $"Building {id} has {normals.Count} external walls and roofs; a calculation on request covers at most {Constants.Default.SolarReceiverCountMax}. Calculate it in the background instead, which covers up to {Constants.Default.SolarJobReceiverCountMax}.");
                }

                UrlBuilder urlBuilder = new(Constants.Default.BuildingModelItemsByCircleUri);
                urlBuilder = urlBuilder.AddParameter("x", center.X);
                urlBuilder = urlBuilder.AddParameter("y", center.Y);
                urlBuilder = urlBuilder.AddParameter("radius", circle2D.Radius);

                // The circle contains the building itself, so a successful answer is never empty: an
                // absent or empty list is a failed read, and solving without neighbours would report
                // an unshaded result as if it were the answer.
                List<BuildingModel>? buildingModels_Surrounding = await httpClient.ItemsAsync<BuildingModel>(urlBuilder.ToString(), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (buildingModels_Surrounding is null || buildingModels_Surrounding.Count == 0)
                {
                    return StatusCode(StatusCodes.Status502BadGateway, new List<string>() { $"The neighbours of building {id} could not be read, so its shading cannot be calculated." });
                }

                if (ePWFileWithId is null)
                {
                    return NoContent();
                }

                EPWFile ePWFile = ePWFileWithId.Value.EPWFile;

                ShadingModel? shadingModel = buildingModel.ToSolar(buildingModels_Surrounding, x => new GuidReference(x).ToString() is string reference && normals.ContainsKey(reference));
                if (shadingModel is null)
                {
                    return UnprocessableEntity(new List<string>() { $"Building {id} could not be converted into a shading model." });
                }

                int casterTriangleCount = shadingModel.CasterTriangleCount();
                if (casterTriangleCount > (background ? Constants.Default.SolarJobCasterTriangleCountMax : Constants.Default.SolarCasterTriangleCountMax))
                {
                    string radiusText = radius.ToString(CultureInfo.InvariantCulture);
                    return PayloadTooLarge(background || casterTriangleCount > Constants.Default.SolarJobCasterTriangleCountMax
                        ? $"The surroundings of building {id} within {radiusText} m hold {casterTriangleCount} shading triangles; a background calculation covers at most {Constants.Default.SolarJobCasterTriangleCountMax}. Choose a smaller radius."
                        : $"The surroundings of building {id} within {radiusText} m hold {casterTriangleCount} shading triangles; a calculation on request covers at most {Constants.Default.SolarCasterTriangleCountMax}. Choose a smaller radius, or calculate it in the background, which covers up to {Constants.Default.SolarJobCasterTriangleCountMax}.");
                }

                return await next(buildingModel, normals, ePWFile, center, shadingModel, casterTriangleCount, ePWFileWithId.Value.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return StatusCode(499, "Calculation cancelled by the client.");
            }
            catch (TaskCanceledException exception)
            {
                return StatusCode(504, $"Building data request timed out: {exception.Message}");
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Solar radiation for building {Id} (county {CountyId}) failed.", id, countyId);
                return StatusCode(500, $"Internal server error: {exception.GetType().Name}: {exception.Message}");
            }
        }

        // A stored result for the building, answered through respondStored, or null to solve (DiGi.GIS.WebAPI.UI#73). The
        // key is the county part, the building model id derived from the model's Guid (Query.SolarBuildingModelId), the
        // rounded radius and the solver version; the weather is not part of it. A stored summary for this EPW file is
        // answered as it is; one for another weather source is rebuilt from the stored shading factors and, for a
        // signed-in visitor, stored again. A request without a county part or without the EPW id (an older GIS Web API)
        // neither reads nor stores; a read failure or a rebuild that cannot run is a miss.
        private async Task<IActionResult?> StoredAsync(HttpClient httpClient, long id, int? countyId, double radius, BuildingModel buildingModel, EPWFile ePWFile, int? ePWFileId, Point2D center, Func<BuildingModel, List<SurfaceSolarRadiationResult>, EPWFile, Point2D, SolarRadiationOrigin, IActionResult> respondStored, CancellationToken cancellationToken)
        {
            long? buildingModelId = buildingModel.SolarBuildingModelId();
            if (countyId is null || ePWFileId is null || buildingModelId is null)
            {
                return null;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            string? tokenString = Request.Cookies[Constants.Default.UserTokenCookieName];
            string weatherSourceId = ePWFileId.Value.ToString(CultureInfo.InvariantCulture);

            GIS.Classes.BuildingSolarRadiationResult? buildingSolarRadiationResult = await httpClient.BuildingSolarRadiationAsync(buildingModelId.Value, countyId.Value, radius, false, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (buildingSolarRadiationResult is null)
            {
                return null;
            }

            if (buildingSolarRadiationResult.WeatherSourceType == GIS.Enums.WeatherSourceType.EPW && buildingSolarRadiationResult.WeatherSourceId == weatherSourceId)
            {
                List<SurfaceSolarRadiationResult>? surfaceSolarRadiationResults = Create.SurfaceSolarRadiationResults(buildingSolarRadiationResult.SurfaceSolarRadiations);
                if (surfaceSolarRadiationResults is null || surfaceSolarRadiationResults.Count == 0)
                {
                    return null;
                }

                logger.LogInformation("Solar radiation for building {Id} (county {CountyId}): loaded the stored result of {StoredAt} - radius {Radius} m, {ResultCount} results, weather {WeatherSourceId}, no solve, total {Elapsed} ms.", id, countyId, buildingSolarRadiationResult.CreatedAt, radius, surfaceSolarRadiationResults.Count, weatherSourceId, stopwatch.ElapsedMilliseconds);

                return respondStored(buildingModel, surfaceSolarRadiationResults, ePWFile, center, new SolarRadiationOrigin(SolarRadiationSource.Stored, buildingSolarRadiationResult.CreatedAt, false, tokenString is not null));
            }

            GIS.Classes.BuildingSolarRadiationResult? buildingSolarRadiationResult_Factors = await httpClient.BuildingSolarRadiationAsync(buildingModelId.Value, countyId.Value, radius, true, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            List<GIS.Classes.SurfaceSolarRadiation>? surfaceSolarRadiations = buildingSolarRadiationResult_Factors.SurfaceSolarRadiations(buildingModel.BuildingInformation, ePWFile);
            List<SurfaceSolarRadiationResult>? surfaceSolarRadiationResults_Rebuilt = Create.SurfaceSolarRadiationResults(surfaceSolarRadiations);
            if (surfaceSolarRadiations is null || surfaceSolarRadiationResults_Rebuilt is null || surfaceSolarRadiationResults_Rebuilt.Count == 0)
            {
                logger.LogWarning("Solar radiation for building {Id} (county {CountyId}): the stored result for weather {WeatherSourceId} could not be rebuilt for weather {WeatherSourceIdNew}, so it is solved again.", id, countyId, buildingSolarRadiationResult.WeatherSourceId, weatherSourceId);
                return null;
            }

            bool saved = tokenString is not null && await StoreAsync(buildingModel.BuildingSolarRadiationResult(countyId.Value, radius, ePWFileId.Value, surfaceSolarRadiations), tokenString, id, cancellationToken);

            logger.LogInformation("Solar radiation for building {Id} (county {CountyId}): rebuilt the stored result of {StoredAt} for weather {WeatherSourceId} (stored for {WeatherSourceIdStored}) - radius {Radius} m, {ResultCount} results, no solve, {Saved}, total {Elapsed} ms.", id, countyId, buildingSolarRadiationResult.CreatedAt, weatherSourceId, buildingSolarRadiationResult.WeatherSourceId, radius, surfaceSolarRadiationResults_Rebuilt.Count, saved ? "stored" : "not stored", stopwatch.ElapsedMilliseconds);

            return respondStored(buildingModel, surfaceSolarRadiationResults_Rebuilt, ePWFile, center, new SolarRadiationOrigin(SolarRadiationSource.Rebuilt, buildingSolarRadiationResult.CreatedAt, saved, tokenString is not null));
        }

        // Stores a result on behalf of the visitor whose token is given, answering whether it was stored. A refusal (401
        // for an expired session, 400 for a disabled flag or a refused body, 404 for a reference in no county part) or a
        // failure is logged with its status and never fails the visitor's response; the token is never logged.
        private async Task<bool> StoreAsync(GIS.Classes.BuildingSolarRadiationResult? buildingSolarRadiationResult, string tokenString, long id, CancellationToken cancellationToken)
        {
            if (buildingSolarRadiationResult is null)
            {
                logger.LogWarning("Solar radiation for building {Id}: the result cannot be stored - the model carries no reference, or a surface has no shading factor for some sun-up hour.", id);
                return false;
            }

            WebAPIResponse? webAPIResponse = await httpClientFactory.CreateClient().StoreBuildingSolarRadiationAsync(buildingSolarRadiationResult, tokenString, cancellationToken);
            if (webAPIResponse?.StatusCode == StatusCodes.Status200OK)
            {
                return true;
            }

            logger.LogWarning("Solar radiation for building {Id}: the result was not stored, the GIS Web API answered {StatusCode}.", id, webAPIResponse is null ? "no response" : webAPIResponse.StatusCode.ToString(CultureInfo.InvariantCulture));
            return false;
        }

        // The synchronous routes: prepare with the synchronous ceilings, then solve in this request behind the
        // host-wide gate and hand the results to the route's response. The gate is also held by background
        // jobs for many minutes, so the wait is bounded (SolarSolveGateWaitSeconds) and answers 503 rather
        // than holding the request until the front end gives up. A signed-in visitor's result is stored before
        // the response, which says whether it was.
        private async Task<IActionResult> SolveAsync(long id, int? countyId, double? radius, Func<BuildingModel, List<SurfaceSolarRadiationResult>, double, EPWFile, Point2D, SolarRadiationOrigin, IActionResult> respond, CancellationToken cancellationToken)
        {
            double radius_Requested = radius ?? Constants.Default.SolarSurroundingRadius;
            double radius_Value = Query.SolarRadiusKey(radius_Requested);
            if (!IsValidRadius(radius_Requested) || !IsValidRadius(radius_Value))
            {
                return RadiusBadRequest();
            }

            Stopwatch stopwatch = Stopwatch.StartNew();

            return await PrepareAsync(id, countyId, radius_Value, false, (buildingModel, surfaceSolarRadiationResults, ePWFile, center, solarRadiationOrigin) =>
            {
                return respond(buildingModel, surfaceSolarRadiationResults, radius_Value, ePWFile, center, solarRadiationOrigin);
            }, async (buildingModel, normals, ePWFile, center, shadingModel, casterTriangleCount, ePWFileId) =>
            {
                if (!await semaphoreSlim.WaitAsync(SolveGateWait, cancellationToken))
                {
                    logger.LogInformation("Solar radiation for building {Id} (county {CountyId}) refused: the solve gate stayed busy for {Wait} s.", id, countyId, SolveGateWait.TotalSeconds);
                    return ServiceUnavailable(Constants.Default.SolarSolveGateWaitSeconds, $"Another solar radiation calculation is running on the server. Try again in a minute, or calculate building {id} in the background.");
                }

                List<GIS.Classes.SurfaceSolarRadiation>? surfaceSolarRadiations;
                try
                {
                    surfaceSolarRadiations = shadingModel.SurfaceSolarRadiations(normals, ePWFile, SolarShadingSolverOptions(), message => logger.LogInformation("{Message}", message));
                }
                finally
                {
                    semaphoreSlim.Release();
                }

                List<SurfaceSolarRadiationResult>? surfaceSolarRadiationResults = Create.SurfaceSolarRadiationResults(surfaceSolarRadiations);

                logger.LogInformation("Solar radiation for building {Id} (county {CountyId}): radius {Radius} m, {ReceiverCount} receivers, {CasterTriangleCount} caster triangles, {ResultCount} results, total {Elapsed} ms.", id, countyId, radius_Value, normals.Count, casterTriangleCount, surfaceSolarRadiationResults?.Count, stopwatch.ElapsedMilliseconds);

                if (surfaceSolarRadiationResults is null)
                {
                    return UnprocessableEntity(new List<string>() { $"The solar radiation of building {id} could not be calculated from its model and weather file." });
                }

                string? tokenString = Request.Cookies[Constants.Default.UserTokenCookieName];
                bool saved = tokenString is not null && countyId is not null && ePWFileId is not null && await StoreAsync(buildingModel.BuildingSolarRadiationResult(countyId.Value, radius_Value, ePWFileId.Value, surfaceSolarRadiations), tokenString, id, cancellationToken);

                return respond(buildingModel, surfaceSolarRadiationResults, radius_Value, ePWFile, center, new SolarRadiationOrigin(SolarRadiationSource.Solved, null, saved, tokenString is not null));
            }, cancellationToken);
        }
    }
}
