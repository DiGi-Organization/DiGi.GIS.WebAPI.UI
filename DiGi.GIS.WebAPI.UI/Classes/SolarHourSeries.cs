using DiGi.Geometry.Spatial.Classes;
using System;

namespace DiGi.GIS.WebAPI.UI.Classes
{
    /// <summary>
    /// Holds the hourly weather and sun of one site, shared by every receiving surface of a building: one element per weather hour, in weather order.
    /// <para>Both the solve and the rebuild of a stored result aggregate through it (<see cref="Create.SurfaceSolarRadiation(SolarHourSeries?, string?, double, Vector3D?, double, double, System.Collections.Generic.IList{double}?)"/>), so the two read every hour identically. Built by <see cref="Create.SolarHourSeries(Weather.Classes.Weather?, Core.Classes.Coordinates?, Core.Enums.UTC)"/>.</para>
    /// </summary>
    public class SolarHourSeries
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SolarHourSeries"/> class. The arrays are kept, not copied, and must have one common length.
        /// </summary>
        /// <param name="dateTimes">The mid-hour reference time of each hour.</param>
        /// <param name="sunDirections">The sun direction of each hour, night included.</param>
        /// <param name="sunUps">Whether the sun is up in each hour.</param>
        /// <param name="globalHorizontalRadiations">The global horizontal radiation of each hour, in Wh/m².</param>
        /// <param name="directNormalRadiations">The direct normal radiation of each hour, in Wh/m².</param>
        /// <param name="diffuseHorizontalRadiations">The diffuse horizontal radiation of each hour, in Wh/m².</param>
        /// <param name="albedos">The albedo of each hour, the default substituted for a missing reading.</param>
        public SolarHourSeries(DateTime[] dateTimes, Vector3D?[] sunDirections, bool[] sunUps, double[] globalHorizontalRadiations, double[] directNormalRadiations, double[] diffuseHorizontalRadiations, double[] albedos)
        {
            DateTimes = dateTimes;
            SunDirections = sunDirections;
            SunUps = sunUps;
            GlobalHorizontalRadiations = globalHorizontalRadiations;
            DirectNormalRadiations = directNormalRadiations;
            DiffuseHorizontalRadiations = diffuseHorizontalRadiations;
            Albedos = albedos;
        }

        /// <summary>
        /// Gets the albedo of each hour.
        /// </summary>
        public double[] Albedos { get; }

        /// <summary>
        /// Gets the number of hours.
        /// </summary>
        public int Count => DateTimes.Length;

        /// <summary>
        /// Gets the mid-hour reference time of each hour.
        /// </summary>
        public DateTime[] DateTimes { get; }

        /// <summary>
        /// Gets the diffuse horizontal radiation of each hour, in Wh/m².
        /// </summary>
        public double[] DiffuseHorizontalRadiations { get; }

        /// <summary>
        /// Gets the direct normal radiation of each hour, in Wh/m².
        /// </summary>
        public double[] DirectNormalRadiations { get; }

        /// <summary>
        /// Gets the global horizontal radiation of each hour, in Wh/m².
        /// </summary>
        public double[] GlobalHorizontalRadiations { get; }

        /// <summary>
        /// Gets the sun direction of each hour, night included: a night hour still needs one for its irradiance, whose beam is zeroed by passing no direct radiation.
        /// </summary>
        public Vector3D?[] SunDirections { get; }

        /// <summary>
        /// Gets whether the sun is up in each hour.
        /// </summary>
        public bool[] SunUps { get; }
    }
}
