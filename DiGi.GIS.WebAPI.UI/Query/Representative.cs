using System;
using System.Collections.Generic;
using System.Linq;

namespace DiGi.GIS.WebAPI.UI
{
    public static partial class Query
    {
        /// <summary>
        /// Chooses the most representative row of a group of buildings: the medoid over the group's parameters, which is always a real building.
        /// <para>The selection runs in four steps.</para>
        /// <para>1. Categorical parameters: only rows carrying the modal value of every categorical column stay eligible. Ties go to the ordinal-first value. A building of an untypical category can then never be chosen, however close its numbers are. Within a typology leaf such a column is constant and filters nothing.</para>
        /// <para>2. Numeric parameters: each one is centred on the group's median and divided by its interquartile range. When the interquartile range is zero, the median absolute deviation is used, and then the full range. A column with no spread at all is dropped, so a parameter the leaf itself pins (a narrow range rule) does not dilute the others. The statistics are taken over the whole group, not only the eligible rows.</para>
        /// <para>3. Candidates: an eligible row with every remaining numeric parameter present. A row missing a value still counts as a neighbour, over the parameters it carries, but cannot be chosen. When no eligible row is complete, any complete row is a candidate.</para>
        /// <para>4. Medoid: the candidate with the smallest sum of distances to every row of the group. The distance between two rows is the mean absolute scaled difference over the parameters both carry. With more than <paramref name="exactCandidateCount"/> candidates, only the <paramref name="sampleCount"/> nearest the median profile are evaluated, which bounds the cost at sampleCount × row count. Ties are broken by the ordinal reference, then by index, so the choice is deterministic.</para>
        /// <para>Nothing is cached between calls (Coding - General, section 1.20).</para>
        /// </summary>
        /// <param name="references">The reference of every row; its count is the row count. This value can be null.</param>
        /// <param name="numericColumns">The numeric parameters, one list per parameter holding one value per row (null when missing). A list whose count differs from the row count is ignored. This value can be null.</param>
        /// <param name="categoricalColumns">The categorical parameters, one list per parameter holding one invariant-culture text value per row (null when missing). A list whose count differs from the row count is ignored. This value can be null.</param>
        /// <param name="exactCandidateCount">The number of candidates up to which every candidate is evaluated exactly.</param>
        /// <param name="sampleCount">The number of candidates nearest the median profile evaluated when there are more than <paramref name="exactCandidateCount"/>.</param>
        /// <returns>The chosen row and its typicality figures, or <see langword="null"/> when there are no rows or no row is complete.</returns>
        public static Classes.Representative? Representative(IReadOnlyList<string?>? references, IReadOnlyList<IReadOnlyList<double?>>? numericColumns, IReadOnlyList<IReadOnlyList<string?>>? categoricalColumns, int exactCandidateCount = Constants.Default.RepresentativeExactCandidateCount, int sampleCount = Constants.Default.RepresentativeSampleCount)
        {
            if (references is null || references.Count == 0)
            {
                return null;
            }

            int count = references.Count;

            // 1. The modal value of every categorical parameter.
            bool[] eligibles = new bool[count];
            Array.Fill(eligibles, true);

            if (categoricalColumns is not null)
            {
                foreach (IReadOnlyList<string?>? categoricalColumn in categoricalColumns)
                {
                    if (categoricalColumn is null || categoricalColumn.Count != count)
                    {
                        continue;
                    }

                    Dictionary<string, int> counts = new(StringComparer.Ordinal);
                    for (int i = 0; i < count; i++)
                    {
                        string? value = categoricalColumn[i];
                        if (value is not null)
                        {
                            counts[value] = counts.GetValueOrDefault(value) + 1;
                        }
                    }

                    if (counts.Count == 0)
                    {
                        continue;
                    }

                    string modal = counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).First().Key;
                    for (int i = 0; i < count; i++)
                    {
                        if (!string.Equals(categoricalColumn[i], modal, StringComparison.Ordinal))
                        {
                            eligibles[i] = false;
                        }
                    }
                }
            }

            // 2. Every numeric parameter centred on its median and divided by a robust scale; NaN marks a missing value.
            List<double[]> features = [];

            if (numericColumns is not null)
            {
                foreach (IReadOnlyList<double?>? numericColumn in numericColumns)
                {
                    if (numericColumn is null || numericColumn.Count != count)
                    {
                        continue;
                    }

                    List<double> values = [];
                    for (int i = 0; i < count; i++)
                    {
                        double? value = numericColumn[i];
                        if (value.HasValue && double.IsFinite(value.Value))
                        {
                            values.Add(value.Value);
                        }
                    }

                    if (values.Count == 0)
                    {
                        continue;
                    }

                    double median = values.Quantile(0.5);
                    double scale = values.Quantile(0.75) - values.Quantile(0.25);
                    if (!(scale > 0))
                    {
                        scale = values.ConvertAll(x => System.Math.Abs(x - median)).Quantile(0.5);
                    }

                    if (!(scale > 0))
                    {
                        scale = values.Max() - values.Min();
                    }

                    if (!(scale > 0))
                    {
                        continue;
                    }

                    double[] scaledValues = new double[count];
                    for (int i = 0; i < count; i++)
                    {
                        double? value = numericColumn[i];
                        scaledValues[i] = value.HasValue && double.IsFinite(value.Value) ? (value.Value - median) / scale : double.NaN;
                    }

                    features.Add(scaledValues);
                }
            }

            // 3. Complete eligible rows; any complete row when no eligible one is.
            List<int> candidates = [];
            for (int i = 0; i < count; i++)
            {
                if (eligibles[i] && IsComplete(i))
                {
                    candidates.Add(i);
                }
            }

            if (candidates.Count == 0)
            {
                for (int i = 0; i < count; i++)
                {
                    if (IsComplete(i))
                    {
                        candidates.Add(i);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            int candidateCount = candidates.Count;

            // 4. The medoid, over the candidates nearest the median profile when there are too many to evaluate exactly.
            bool sampled = false;
            if (candidates.Count > exactCandidateCount && sampleCount > 0)
            {
                candidates = [.. candidates.OrderBy(DistanceToMedian).ThenBy(i => references[i], StringComparer.Ordinal).ThenBy(i => i).Take(sampleCount)];
                sampled = true;
            }

            int index_Best = -1;
            double total_Best = double.MaxValue;
            foreach (int candidate in candidates)
            {
                double total = 0;
                for (int i = 0; i < count; i++)
                {
                    total += Distance(candidate, i);
                }

                if (index_Best == -1 || total < total_Best || (total == total_Best && Compare(candidate, index_Best) < 0))
                {
                    index_Best = candidate;
                    total_Best = total;
                }
            }

            List<double> distances = new(count - 1);
            for (int i = 0; i < count; i++)
            {
                if (i != index_Best)
                {
                    distances.Add(Distance(index_Best, i));
                }
            }

            double meanDistance = distances.Count == 0 ? 0 : distances.Average();
            double percentile90Distance = distances.Count == 0 ? 0 : distances.Quantile(0.9);

            return new Classes.Representative(index_Best, features.Count, candidateCount, sampled, meanDistance, percentile90Distance);

            bool IsComplete(int index)
            {
                foreach (double[] feature in features)
                {
                    if (double.IsNaN(feature[index]))
                    {
                        return false;
                    }
                }

                return true;
            }

            double DistanceToMedian(int index)
            {
                if (features.Count == 0)
                {
                    return 0;
                }

                double sum = 0;
                foreach (double[] feature in features)
                {
                    sum += System.Math.Abs(feature[index]);
                }

                return sum / features.Count;
            }

            double Distance(int index_1, int index_2)
            {
                double sum = 0;
                int shared = 0;
                foreach (double[] feature in features)
                {
                    double value_1 = feature[index_1];
                    double value_2 = feature[index_2];
                    if (double.IsNaN(value_1) || double.IsNaN(value_2))
                    {
                        continue;
                    }

                    sum += System.Math.Abs(value_1 - value_2);
                    shared++;
                }

                return shared == 0 ? 0 : sum / shared;
            }

            int Compare(int index_1, int index_2)
            {
                int result = string.CompareOrdinal(references[index_1], references[index_2]);
                return result != 0 ? result : index_1.CompareTo(index_2);
            }
        }
    }
}
