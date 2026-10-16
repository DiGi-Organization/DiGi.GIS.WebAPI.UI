using System;
using System.Collections.Generic;

namespace DiGi.GIS.WebAPI.UI.ViewModels
{
    /// <summary>
    /// The solar radiation of one building as the Building Viewer's "Solar radiation" panel applies it: the colour and result of each receiving surface, the radius and weather station, and where the result came from.
    /// </summary>
    public class SolarRadiationViewModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SolarRadiationViewModel"/> class.
        /// </summary>
        /// <param name="radius">The neighbour radius the result was solved with, in m.</param>
        /// <param name="stationName">The name of the weather station, or null.</param>
        /// <param name="stationUrl">The station page of the weather file, or null.</param>
        /// <param name="surfaces">The coloured surfaces.</param>
        /// <param name="source">Where the result came from: <c>Solved</c>, <c>Stored</c> or <c>Rebuilt</c> (<see cref="Enums.SolarRadiationSource"/>).</param>
        /// <param name="storedAt">When a stored result was calculated, or null.</param>
        /// <param name="saved">True when this request stored its result.</param>
        /// <param name="signedIn">True when the request carried a user token, so a result could be stored.</param>
        public SolarRadiationViewModel(
            double radius,
            string? stationName,
            string? stationUrl,
            List<SolarSurfaceViewModel> surfaces,
            string source,
            DateTimeOffset? storedAt,
            bool saved,
            bool signedIn)
        {
            Radius = radius;
            StationName = stationName;
            StationUrl = stationUrl;
            Surfaces = surfaces;
            Source = source;
            StoredAt = storedAt;
            Saved = saved;
            SignedIn = signedIn;
        }

        /// <summary>
        /// Gets the neighbour radius the result was solved with, in m.
        /// </summary>
        public double Radius { get; }

        /// <summary>
        /// Gets a value indicating whether this request stored its result.
        /// </summary>
        public bool Saved { get; }

        /// <summary>
        /// Gets a value indicating whether the request carried a user token.
        /// </summary>
        public bool SignedIn { get; }

        /// <summary>
        /// Gets where the result came from: <c>Solved</c>, <c>Stored</c> or <c>Rebuilt</c>.
        /// </summary>
        public string Source { get; }

        /// <summary>
        /// Gets the name of the weather station, or null.
        /// </summary>
        public string? StationName { get; }

        /// <summary>
        /// Gets the station page of the weather file, or null.
        /// </summary>
        public string? StationUrl { get; }

        /// <summary>
        /// Gets when a stored result was calculated, or null.
        /// </summary>
        public DateTimeOffset? StoredAt { get; }

        /// <summary>
        /// Gets the coloured surfaces.
        /// </summary>
        public List<SolarSurfaceViewModel> Surfaces { get; }
    }
}
