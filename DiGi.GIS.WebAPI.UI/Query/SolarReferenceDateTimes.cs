using System;
using System.Collections.Generic;

namespace DiGi.GIS.WebAPI.UI
{
    public static partial class Query
    {
        /// <summary>
        /// Gets the <see cref="Constants.Default.SolarReferenceHourCount"/> mid-hour times of the solar reference year, one per hour in order, as <see cref="SolarReferenceDateTime(DateTime)"/> maps EPW records: 1 January 00:30 to 31 December 23:30.
        /// <para>The time series the shading solver samples. A surface's shading factors depend on the sun position only, so they are solved for every hour of the year, independent of which hours a weather source happens to hold (DiGi.GIS.WebAPI.UI#73).</para>
        /// </summary>
        /// <returns>The times, in order; element <c>i</c> lies in hour <c>i</c> (<see cref="SolarReferenceHourIndex(DateTime)"/>).</returns>
        public static List<DateTime> SolarReferenceDateTimes()
        {
            DateTime dateTime_Start = new(Constants.Default.SolarReferenceYear, 1, 1, 0, 30, 0, DateTimeKind.Unspecified);

            List<DateTime> result = [];
            for (int i = 0; i < Constants.Default.SolarReferenceHourCount; i++)
            {
                result.Add(dateTime_Start.AddHours(i));
            }

            return result;
        }
    }
}
