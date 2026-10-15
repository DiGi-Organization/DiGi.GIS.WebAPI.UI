namespace DiGi.GIS.WebAPI.UI.Classes
{
    /// <summary>
    /// One building of a <see cref="TypologyRepresentativeParameter"/>: the identity the solve answered for it.
    /// </summary>
    public class TypologyRepresentativeBuildingParameter
    {
        /// <summary>
        /// Gets or sets the building reference key, unique within its county part.
        /// </summary>
        public string? Reference { get; set; }

        /// <summary>
        /// Gets or sets the building's database identifier, or 0 when unknown.
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Gets or sets the county part identifier the building is filed under, or 0 when unknown.
        /// </summary>
        public int CountyId { get; set; }
    }
}
