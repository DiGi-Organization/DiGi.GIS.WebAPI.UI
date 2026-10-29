using DiGi.GIS.WebAPI.UI.Enums;
using System;

namespace DiGi.GIS.WebAPI.UI
{
    public static partial class Query
    {
        /// <summary>
        /// Determines whether a limit of a technical-conditions regulation, identified by its name, applies to a building of the given category (DiGi-Organization/DiGi.GIS.WebAPI.UI#82).
        /// <para>The category is read from the name, the description DiGi.Analytical gives each limit: "Public building …" applies to a non-residential building, "Residential building (single-family) …" to a single-family building only, and any other "Residential building …" to every residential building. A name without one of these prefixes (the 2014 regulation's "Wall", "Window" …) applies to every building. With <see cref="Enums.RegulatedBuildingCategory.Undefined"/> every limit applies, and with <see cref="Enums.RegulatedBuildingCategory.Residential"/> every residential limit, the single-family ones included.</para>
        /// <para>The 1997 and 2002 regulations set the wall, roof and basement ceiling limits (1997: every limit) of a residential building for a single-family building only; a multi-family building meets them by its seasonal heat demand indicator E ≤ E0 instead (DiGi-Organization/DiGi.Analytical#12, #14).</para>
        /// </summary>
        /// <param name="name">The name of the limit. This value can be null.</param>
        /// <param name="regulatedBuildingCategory">The category of the building.</param>
        /// <returns><see langword="true"/> if the limit applies to the building; <see langword="false"/> if it does not or <paramref name="name"/> is null or whitespace.</returns>
        public static bool IsRegulated(string? name, RegulatedBuildingCategory regulatedBuildingCategory)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (regulatedBuildingCategory == Enums.RegulatedBuildingCategory.Undefined)
            {
                return true;
            }

            if (name.StartsWith("Public building", StringComparison.OrdinalIgnoreCase))
            {
                return regulatedBuildingCategory == Enums.RegulatedBuildingCategory.NonResidential;
            }

            if (name.StartsWith("Residential building (single-family)", StringComparison.OrdinalIgnoreCase))
            {
                return regulatedBuildingCategory is Enums.RegulatedBuildingCategory.SingleFamily or Enums.RegulatedBuildingCategory.Residential;
            }

            if (name.StartsWith("Residential building", StringComparison.OrdinalIgnoreCase))
            {
                return regulatedBuildingCategory is Enums.RegulatedBuildingCategory.SingleFamily or Enums.RegulatedBuildingCategory.MultiFamily or Enums.RegulatedBuildingCategory.Residential;
            }

            return true;
        }
    }
}
