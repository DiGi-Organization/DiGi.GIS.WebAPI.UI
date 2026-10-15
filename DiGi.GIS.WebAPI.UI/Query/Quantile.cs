using System.Collections.Generic;
using System.Linq;

namespace DiGi.GIS.WebAPI.UI
{
    public static partial class Query
    {
        /// <summary>
        /// Calculates the quantile of a set of values at the given probability, interpolating linearly between the two closest ranks (the R-7 / spreadsheet definition, so the 0.5 quantile is the median).
        /// <para>Non-finite values are ignored, so a missing cell carried as <see cref="double.NaN"/> does not shift the result.</para>
        /// </summary>
        /// <param name="values">The values. This value can be null.</param>
        /// <param name="probability">The probability in the range [0, 1]: 0.25 for the first quartile, 0.5 for the median, 0.9 for the 90th percentile.</param>
        /// <returns>The quantile, or <see cref="double.NaN"/> when there is no finite value or the probability is outside [0, 1].</returns>
        public static double Quantile(this IEnumerable<double>? values, double probability)
        {
            if (values is null || double.IsNaN(probability) || probability < 0 || probability > 1)
            {
                return double.NaN;
            }

            List<double> sortedValues = [.. values.Where(double.IsFinite)];
            if (sortedValues.Count == 0)
            {
                return double.NaN;
            }

            sortedValues.Sort();

            double position = probability * (sortedValues.Count - 1);
            int index_Lower = (int)System.Math.Floor(position);
            int index_Upper = System.Math.Min(index_Lower + 1, sortedValues.Count - 1);

            return sortedValues[index_Lower] + ((sortedValues[index_Upper] - sortedValues[index_Lower]) * (position - index_Lower));
        }
    }
}
