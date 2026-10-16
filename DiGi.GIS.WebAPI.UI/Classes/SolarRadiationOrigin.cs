using DiGi.GIS.WebAPI.UI.Enums;
using System;

namespace DiGi.GIS.WebAPI.UI.Classes
{
    /// <summary>
    /// Describes where a solar radiation response came from and whether it was stored: what the panel tells the visitor beside the colours.
    /// </summary>
    public class SolarRadiationOrigin
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SolarRadiationOrigin"/> class.
        /// </summary>
        /// <param name="solarRadiationSource">Where the radiation came from.</param>
        /// <param name="storedAt">When the stored result was calculated; null for a fresh solve.</param>
        /// <param name="saved">True when this request stored its result.</param>
        /// <param name="signedIn">True when the request carried a user token, so a result could be stored.</param>
        public SolarRadiationOrigin(SolarRadiationSource solarRadiationSource, DateTimeOffset? storedAt, bool saved, bool signedIn)
        {
            SolarRadiationSource = solarRadiationSource;
            StoredAt = storedAt;
            Saved = saved;
            SignedIn = signedIn;
        }

        /// <summary>
        /// Gets a value indicating whether this request stored its result.
        /// </summary>
        public bool Saved { get; }

        /// <summary>
        /// Gets a value indicating whether the request carried a user token.
        /// </summary>
        public bool SignedIn { get; }

        /// <summary>
        /// Gets where the radiation came from.
        /// </summary>
        public SolarRadiationSource SolarRadiationSource { get; }

        /// <summary>
        /// Gets when the stored result was calculated, or null.
        /// </summary>
        public DateTimeOffset? StoredAt { get; }
    }
}
