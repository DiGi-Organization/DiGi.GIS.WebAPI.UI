using DiGi.WebAPI.Classes;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DiGi.GIS.WebAPI.UI
{
    public static partial class Query
    {
        /// <summary>
        /// Reads the stored solar radiation of a building model (<see cref="Constants.Default.BuildingSolarRadiationUri"/>).
        /// <para>The query names are the endpoint's - <c>id</c>, <c>countyid</c>, <c>radius</c>, <c>solverversion</c>, <c>shadingfactors</c> - and the radius is sent as it is, already rounded by <see cref="SolarRadiusKey(double)"/>; the endpoint compares it exactly. A 204, any failure and an unreadable body all answer null: the caller treats them as a miss and solves.</para>
        /// </summary>
        /// <param name="httpClient">The HTTP client.</param>
        /// <param name="buildingModelId">The building model id (<see cref="SolarBuildingModelId(DiGi.Analytical.Building.Classes.BuildingModel)"/>).</param>
        /// <param name="countyId">The county part the building is filed under.</param>
        /// <param name="radius">The rounded neighbour radius, in m.</param>
        /// <param name="includeShadingFactors">True to read every surface's shading factors too, which only a rebuild for another weather source needs.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to observe for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the stored result for <see cref="Constants.Default.SolarSolverVersion"/>, or null.</returns>
        public static async Task<GIS.Classes.BuildingSolarRadiationResult?> BuildingSolarRadiationAsync(this HttpClient? httpClient, long buildingModelId, int countyId, double radius, bool includeShadingFactors = false, CancellationToken cancellationToken = default)
        {
            if (httpClient is null)
            {
                return null;
            }

            UrlBuilder urlBuilder = new(Constants.Default.BuildingSolarRadiationUri);
            urlBuilder = urlBuilder.AddParameter("id", buildingModelId);
            urlBuilder = urlBuilder.AddParameter("countyid", countyId);
            urlBuilder = urlBuilder.AddParameter("radius", radius);
            urlBuilder = urlBuilder.AddParameter("solverversion", Constants.Default.SolarSolverVersion);
            if (includeShadingFactors)
            {
                urlBuilder = urlBuilder.AddParameter("shadingfactors", true);
            }

            string? json = await httpClient.JsonAsync(urlBuilder.ToString(), cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return Core.Convert.ToDiGi<GIS.Classes.BuildingSolarRadiationResult>(json)?.FirstOrDefault();
        }
    }
}
