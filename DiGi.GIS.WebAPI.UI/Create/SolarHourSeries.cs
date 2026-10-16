using DiGi.Core.Classes;
using DiGi.Core.Enums;
using DiGi.Geometry.Spatial.Classes;
using DiGi.GIS.WebAPI.UI.Classes;
using DiGi.Weather;
using DiGi.Weather.Classes;
using System;
using System.Collections.Generic;

namespace DiGi.GIS.WebAPI.UI
{
    public static partial class Create
    {
        /// <summary>
        /// Builds the hourly weather and sun of a site from a weather series: one hour per weather record that carries all three radiation readings, mapped to the solar reference year (<see cref="Query.SolarReferenceDateTime(DateTime)"/>), the first record of each reference hour kept.
        /// <para>Readings are taken through the DiGi.Weather missing-value helpers (<c>GlobalHorizontalRadiationValue</c>, <c>DirectNormalRadiationValue</c>, <c>DiffuseHorizontalRadiationValue</c>, <c>AlbedoValue</c>): a record missing a radiation reading is skipped, and a missing albedo takes the default of <c>Solar.Query.Albedo</c>. An <see cref="EPW.Classes.EPWFile"/> is a <see cref="Weather.Classes.Weather"/> and is passed as is.</para>
        /// </summary>
        /// <param name="weather">The weather series.</param>
        /// <param name="coordinates">The location of the site.</param>
        /// <param name="uTC">The fixed UTC offset the sun is positioned with - the shading model's, so that the solve and a rebuild of its stored result see the same sun.</param>
        /// <returns>The series, or null when an input is missing, the coordinates are the unstamped (0, 0), or no record is usable.</returns>
        public static SolarHourSeries? SolarHourSeries(this Weather.Classes.Weather? weather, Coordinates? coordinates, UTC uTC)
        {
            // (0, 0) is the default of an unstamped BuildingInformation, not a location in Poland.
            IList<WeatherRecord>? weatherRecords = weather?.WeatherRecords;
            if (weatherRecords is null || coordinates is null || (coordinates.Latitude == 0 && coordinates.Longitude == 0))
            {
                return null;
            }

            List<DateTime> dateTimes = [];
            List<double> globalHorizontalRadiations = [];
            List<double> directNormalRadiations = [];
            List<double> diffuseHorizontalRadiations = [];
            List<double> albedos = [];

            HashSet<DateTime> dateTimes_Unique = [];
            foreach (WeatherRecord weatherRecord in weatherRecords)
            {
                if (weatherRecord is null)
                {
                    continue;
                }

                float? globalHorizontalRadiation = weatherRecord.GlobalHorizontalRadiationValue();
                float? directNormalRadiation = weatherRecord.DirectNormalRadiationValue();
                float? diffuseHorizontalRadiation = weatherRecord.DiffuseHorizontalRadiationValue();
                if (globalHorizontalRadiation is null || directNormalRadiation is null || diffuseHorizontalRadiation is null)
                {
                    continue;
                }

                DateTime? dateTime = weatherRecord.DateTime.SolarReferenceDateTime();
                if (dateTime is null || dateTime.Value.SolarReferenceHourIndex() is null || !dateTimes_Unique.Add(dateTime.Value))
                {
                    continue;
                }

                dateTimes.Add(dateTime.Value);
                globalHorizontalRadiations.Add(globalHorizontalRadiation.Value);
                directNormalRadiations.Add(directNormalRadiation.Value);
                diffuseHorizontalRadiations.Add(diffuseHorizontalRadiation.Value);
                albedos.Add(Solar.Query.Albedo(weatherRecord.AlbedoValue(), false));
            }

            int count = dateTimes.Count;
            if (count == 0)
            {
                return null;
            }

            // The sun depends only on the hour; the night direction is kept for the irradiance of a night hour.
            Vector3D?[] sunDirections = new Vector3D?[count];
            bool[] sunUps = new bool[count];
            for (int i = 0; i < count; i++)
            {
                sunDirections[i] = Solar.Query.SunDirection(coordinates, uTC, dateTimes[i], true);
                sunUps[i] = Solar.Query.SunDirection(coordinates, uTC, dateTimes[i], false) is not null;
            }

            return new SolarHourSeries([.. dateTimes], sunDirections, sunUps, [.. globalHorizontalRadiations], [.. directNormalRadiations], [.. diffuseHorizontalRadiations], [.. albedos]);
        }
    }
}
