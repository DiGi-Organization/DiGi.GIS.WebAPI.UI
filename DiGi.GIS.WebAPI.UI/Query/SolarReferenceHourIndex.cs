using System;

namespace DiGi.GIS.WebAPI.UI
{
    public static partial class Query
    {
        /// <summary>
        /// Gets the hour of the solar reference year a reference date and time falls in: 0 for the hour starting on 1 January at midnight, <see cref="Constants.Default.SolarReferenceHourCount"/> - 1 for the last hour of 31 December.
        /// <para>The index of a surface's stored shading factors. A time from <see cref="SolarReferenceDateTime(DateTime)"/> lies at mid-hour, so the index is the whole hours elapsed since the start of the year.</para>
        /// </summary>
        /// <param name="dateTime">A date and time in <see cref="Constants.Default.SolarReferenceYear"/>.</param>
        /// <returns>The hour index, or null when <paramref name="dateTime"/> lies outside the reference year.</returns>
        public static int? SolarReferenceHourIndex(this DateTime dateTime)
        {
            double hours = (dateTime - new DateTime(Constants.Default.SolarReferenceYear, 1, 1, 0, 0, 0, dateTime.Kind)).TotalHours;
            if (hours < 0 || hours >= Constants.Default.SolarReferenceHourCount)
            {
                return null;
            }

            return (int)Math.Floor(hours);
        }
    }
}
