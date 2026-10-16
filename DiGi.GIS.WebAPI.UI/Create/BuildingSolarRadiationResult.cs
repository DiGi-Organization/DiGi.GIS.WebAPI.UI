using DiGi.Analytical.Building.Classes;
using System.Collections.Generic;
using System.Globalization;

namespace DiGi.GIS.WebAPI.UI
{
    public static partial class Create
    {
        /// <summary>
        /// Builds the stored form of a building's solar radiation, as <c>gis/buildingsolarradiation/setsolarradiation</c> takes it: keyed by county part, building model id (<see cref="Query.SolarBuildingModelId(BuildingModel?)"/>), radius and <see cref="Constants.Default.SolarSolverVersion"/>, with an EPW weather source.
        /// <para>The user email and the creation time are left null: the server stamps both from the token and its clock. The weather period is null, a typical year.</para>
        /// </summary>
        /// <param name="buildingModel">The solved building model; it supplies the <c>building_2d</c> reference (<c>BuildingModelParameter.Reference</c>) and the model id.</param>
        /// <param name="countyId">The county part the panel named; the server resolves the actual part.</param>
        /// <param name="radius">The neighbour radius, already rounded (<see cref="Query.SolarRadiusKey(double)"/>).</param>
        /// <param name="ePWFileId">The id of the <c>epw_file</c> row the summary was calculated from.</param>
        /// <param name="surfaceSolarRadiations">The surfaces in their stored form.</param>
        /// <returns>The result, or null when the model carries no reference, no surface is given, or a surface has no stored shading factors of a whole reference year - such a result cannot be stored.</returns>
        public static GIS.Classes.BuildingSolarRadiationResult? BuildingSolarRadiationResult(this BuildingModel? buildingModel, int countyId, double radius, int ePWFileId, IEnumerable<GIS.Classes.SurfaceSolarRadiation>? surfaceSolarRadiations)
        {
            long? buildingModelId = buildingModel.SolarBuildingModelId();
            if (buildingModelId is null || surfaceSolarRadiations is null)
            {
                return null;
            }

            if (!buildingModel!.TryGetValue<string>(Analytical.Enums.BuildingModelParameter.Reference, out string? reference) || string.IsNullOrWhiteSpace(reference))
            {
                return null;
            }

            List<GIS.Classes.SurfaceSolarRadiation> surfaceSolarRadiations_List = [];
            foreach (GIS.Classes.SurfaceSolarRadiation surfaceSolarRadiation in surfaceSolarRadiations)
            {
                if (surfaceSolarRadiation?.ShadingFactors?.Length != Constants.Default.SolarReferenceHourCount)
                {
                    return null;
                }

                surfaceSolarRadiations_List.Add(surfaceSolarRadiation);
            }

            if (surfaceSolarRadiations_List.Count == 0)
            {
                return null;
            }

            return new GIS.Classes.BuildingSolarRadiationResult(reference, countyId, buildingModelId.Value, radius, Constants.Default.SolarSolverVersion, GIS.Enums.WeatherSourceType.EPW, ePWFileId.ToString(CultureInfo.InvariantCulture), null, null, null, null, surfaceSolarRadiations_List);
        }
    }
}
