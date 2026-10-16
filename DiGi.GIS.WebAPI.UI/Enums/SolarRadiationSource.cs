namespace DiGi.GIS.WebAPI.UI.Enums
{
    /// <summary>
    /// Specifies where the solar radiation a response shows came from.
    /// </summary>
    public enum SolarRadiationSource
    {
        /// <summary>
        /// Solved for this request: neighbours fetched, shading model built, shading solver run.
        /// </summary>
        Solved,

        /// <summary>
        /// Loaded from a stored result solved for the same building model, radius, solver version and weather source; nothing was solved.
        /// </summary>
        Stored,

        /// <summary>
        /// Rebuilt from the shading factors of a stored result for another weather source; no neighbours, no shading model, no solve.
        /// </summary>
        Rebuilt,
    }
}
