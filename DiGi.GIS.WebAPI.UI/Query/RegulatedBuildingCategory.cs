using DiGi.GIS.Classes;
using DiGi.GIS.Enums;
using DiGi.GIS.WebAPI.UI.Enums;
using System.Collections.Generic;

namespace DiGi.GIS.WebAPI.UI
{
    public static partial class Query
    {
        /// <summary>
        /// Determines the building category that decides which limits of a technical-conditions regulation apply to the building (DiGi-Organization/DiGi.GIS.WebAPI.UI#82).
        /// <para>A residential building (<see cref="GIS.Query.IsResidential(Building2D?)"/>) is <see cref="Enums.RegulatedBuildingCategory.SingleFamily"/> or <see cref="Enums.RegulatedBuildingCategory.MultiFamily"/> when exactly one of the two specific functions is set, and <see cref="Enums.RegulatedBuildingCategory.Residential"/> otherwise. Any other building is <see cref="Enums.RegulatedBuildingCategory.NonResidential"/>.</para>
        /// </summary>
        /// <param name="building2D">The building footprint. This value can be null.</param>
        /// <returns>The building category, or <see cref="Enums.RegulatedBuildingCategory.Undefined"/> when <paramref name="building2D"/> is null.</returns>
        public static RegulatedBuildingCategory RegulatedBuildingCategory(this Building2D? building2D)
        {
            if (building2D is null)
            {
                return Enums.RegulatedBuildingCategory.Undefined;
            }

            if (!GIS.Query.IsResidential(building2D))
            {
                return Enums.RegulatedBuildingCategory.NonResidential;
            }

            HashSet<BuildingSpecificFunction>? buildingSpecificFunctions = building2D.BuildingSpecificFunctions;
            bool singleFamily = buildingSpecificFunctions is not null && buildingSpecificFunctions.Contains(BuildingSpecificFunction.single_family_building);
            bool multiFamily = buildingSpecificFunctions is not null && buildingSpecificFunctions.Contains(BuildingSpecificFunction.multi_family_building);

            if (singleFamily == multiFamily)
            {
                return Enums.RegulatedBuildingCategory.Residential;
            }

            return singleFamily ? Enums.RegulatedBuildingCategory.SingleFamily : Enums.RegulatedBuildingCategory.MultiFamily;
        }
    }
}
