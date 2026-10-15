namespace DiGi.GIS.WebAPI.UI.Classes
{
    /// <summary>
    /// The building chosen to stand for a group of buildings, and how typical it is of the group.
    /// <para>Built by <see cref="Query.Representative"/>, which does the selection; the constructor only assigns the values. Distances are mean per-parameter L1 distances in robust scale units: each parameter is centred on the group's median and divided by its interquartile range, so 1 means "one interquartile range apart on an average parameter".</para>
    /// </summary>
    public class Representative
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Representative"/> class.
        /// </summary>
        /// <param name="index">The index of the chosen row in the input the selection was made from.</param>
        /// <param name="parameterCount">The number of numeric parameters that varied within the group and took part in the distance.</param>
        /// <param name="candidateCount">The number of rows that were eligible to be chosen.</param>
        /// <param name="sampled">A value indicating whether the candidates were narrowed to those nearest the median profile before the exact evaluation.</param>
        /// <param name="meanDistance">The mean distance from the chosen row to every other row of the group.</param>
        /// <param name="percentile90Distance">The 90th percentile of the distances from the chosen row to every other row of the group.</param>
        public Representative(int index, int parameterCount, int candidateCount, bool sampled, double meanDistance, double percentile90Distance)
        {
            Index = index;
            ParameterCount = parameterCount;
            CandidateCount = candidateCount;
            Sampled = sampled;
            MeanDistance = meanDistance;
            Percentile90Distance = percentile90Distance;
        }

        /// <summary> Gets the number of rows that were eligible to be chosen. </summary>
        public int CandidateCount { get; }

        /// <summary> Gets the index of the chosen row in the input the selection was made from. </summary>
        public int Index { get; }

        /// <summary> Gets the mean distance from the chosen row to every other row of the group. </summary>
        public double MeanDistance { get; }

        /// <summary> Gets the number of numeric parameters that varied within the group and took part in the distance. </summary>
        public int ParameterCount { get; }

        /// <summary> Gets the 90th percentile of the distances from the chosen row to every other row of the group. </summary>
        public double Percentile90Distance { get; }

        /// <summary> Gets a value indicating whether the candidates were narrowed to those nearest the median profile before the exact evaluation. </summary>
        public bool Sampled { get; }
    }
}
