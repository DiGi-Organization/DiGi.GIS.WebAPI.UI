using System.ComponentModel;

namespace DiGi.GIS.WebAPI.UI.Enums
{
    /// <summary>
    /// Specifies the building category that decides which limits of a technical-conditions regulation apply to a building (DiGi-Organization/DiGi.GIS.WebAPI.UI#82).
    /// <para>The 1997, 2002 and 2008 regulations set limits per category: single-family building rows, residential rows for every residential building, and public building rows, which the panel also shows for any other non-residential building.</para>
    /// </summary>
    public enum RegulatedBuildingCategory
    {
        /// <summary>
        /// The category is not known (no footprint was found); every limit is shown.
        /// </summary>
        [Description("Undefined")] Undefined,

        /// <summary>
        /// A single-family residential building.
        /// </summary>
        [Description("Single-family building")] SingleFamily,

        /// <summary>
        /// A multi-family residential building; the single-family limits do not apply to it.
        /// </summary>
        [Description("Multi-family building")] MultiFamily,

        /// <summary>
        /// A residential building that is neither marked single-family nor multi-family, or is marked both; every residential limit is shown.
        /// </summary>
        [Description("Residential building")] Residential,

        /// <summary>
        /// A non-residential building; the public building limits are shown.
        /// </summary>
        [Description("Non-residential building")] NonResidential,
    }
}
