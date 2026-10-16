using DiGi.Geometry.Spatial.Classes;
using DiGi.GIS.WebAPI.UI.Classes;
using DiGi.Solar.Classes;
using System;
using System.Collections.Generic;

namespace DiGi.GIS.WebAPI.UI
{
    public static partial class Create
    {
        /// <summary>
        /// Aggregates the hourly radiation of one receiving surface over a weather series into its stored form: the summary values and the quantized shading factors.
        /// <para>The one aggregation behind both a solve and a rebuild from stored factors, so the two read every hour identically. Per hour: the beam reaches the unshaded area only (<c>UnshadedArea · I_beam</c>), the diffuse and ground-reflected components the unblocked share of the sky and ground (<c>A · I_diff · SkyVisibility</c>, <c>A · I_ground · GroundVisibility</c>); a blocked share contributes 0, as reflections off neighbours are ignored (DiGi-Organization/DiGi.Solar#15). A night hour has a factor of 0 and no direct radiation.</para>
        /// <para>A sun-up hour whose factor is <see cref="double.NaN"/> - the solver produced none - is skipped, and the surface is then given no stored factors (<see cref="GIS.Classes.SurfaceSolarRadiation.ShadingFactors"/> null), so it cannot be stored: a stored byte cannot say "missing", and reading it as 0 would count the hour as unshaded on a rebuild.</para>
        /// </summary>
        /// <param name="solarHourSeries">The hourly weather and sun of the site.</param>
        /// <param name="reference">The <c>GuidReference</c> string of the surface's component.</param>
        /// <param name="area">The area of the surface, in m².</param>
        /// <param name="normal">The outward unit normal of the surface.</param>
        /// <param name="skyVisibility">The unblocked share of the sky, 0..1.</param>
        /// <param name="groundVisibility">The unblocked share of the ground, 0..1.</param>
        /// <param name="shadingFactors">The shading factor of every hour of the solar reference year (<see cref="Constants.Default.SolarReferenceHourCount"/> values, index = <see cref="Query.SolarReferenceHourIndex(DateTime)"/>), 0 unshaded to 1 shaded; NaN for a sun-up hour without one.</param>
        /// <returns>The surface, or null when an input is missing, the area is not a positive number or the factors do not cover the reference year.</returns>
        public static GIS.Classes.SurfaceSolarRadiation? SurfaceSolarRadiation(this SolarHourSeries? solarHourSeries, string? reference, double area, Vector3D? normal, double skyVisibility, double groundVisibility, IList<double>? shadingFactors)
        {
            if (solarHourSeries is null || normal is null || !double.IsFinite(area) || area <= 0 || shadingFactors is null || shadingFactors.Count != Constants.Default.SolarReferenceHourCount)
            {
                return null;
            }

            // Energies in Wh: each weather value is a mean power over one hour.
            double beam = 0;
            double diffuse = 0;
            double ground = 0;
            double unshaded = 0;
            bool missing = false;

            for (int i = 0; i < solarHourSeries.Count; i++)
            {
                int index = solarHourSeries.DateTimes[i].SolarReferenceHourIndex()!.Value;
                bool sunUp = solarHourSeries.SunUps[i];

                double shadingFactor = 0;
                if (sunUp)
                {
                    double shadingFactor_Hour = shadingFactors[index];
                    if (double.IsNaN(shadingFactor_Hour))
                    {
                        missing = true;
                        continue;
                    }

                    // A fully shaded receiver can read 1 plus a few ulps.
                    shadingFactor = Math.Clamp(shadingFactor_Hour, 0, 1);
                }

                IrradianceResult? irradianceResult = Solar.Create.IrradianceResult(normal, solarHourSeries.SunDirections[i], solarHourSeries.GlobalHorizontalRadiations[i], sunUp ? solarHourSeries.DirectNormalRadiations[i] : 0, solarHourSeries.DiffuseHorizontalRadiations[i], solarHourSeries.Albedos[i]);
                if (irradianceResult is null)
                {
                    continue;
                }

                SolarPowerResult? solarPowerResult = Solar.Create.SolarPowerResult_ByShadingFactor(irradianceResult, area, shadingFactor);
                SolarPowerResult? solarPowerResult_Unshaded = Solar.Create.SolarPowerResult_ByShadingFactor(irradianceResult, area, 0);
                if (solarPowerResult is null || solarPowerResult_Unshaded is null)
                {
                    continue;
                }

                // The local irradiance result is used because the property clones on every access.
                beam += solarPowerResult.UnshadedArea * irradianceResult.Beam;
                diffuse += solarPowerResult.TotalArea * irradianceResult.Diffuse * skyVisibility;
                ground += solarPowerResult.TotalArea * irradianceResult.Ground * groundVisibility;
                unshaded += solarPowerResult_Unshaded.Power;
            }

            byte[]? shadingFactors_Stored = null;
            if (!missing)
            {
                for (int i = 0; i < shadingFactors.Count; i++)
                {
                    if (double.IsNaN(shadingFactors[i]))
                    {
                        missing = true;
                        break;
                    }
                }

                shadingFactors_Stored = missing ? null : GIS.Convert.ToSystem_Bytes(shadingFactors);
            }

            double energy = (beam + diffuse + ground) / 1000;

            return new GIS.Classes.SurfaceSolarRadiation(
                reference,
                area,
                normal,
                skyVisibility,
                groundVisibility,
                energy / area,
                beam / 1000 / area,
                diffuse / 1000 / area,
                ground / 1000 / area,
                unshaded / 1000 / area,
                energy,
                shadingFactors_Stored);
        }
    }
}
