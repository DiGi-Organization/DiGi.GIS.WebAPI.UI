using System.Collections.Generic;

namespace DiGi.GIS.WebAPI.UI.ViewModels
{
    /// <summary>
    /// The answer of <c>POST /typology/representative</c>: the real building chosen to stand for a typology node, how typical it is, and the dimensions of the synthetic archetype derived from it.
    /// <para>The real representative is the medoid over the definition's parameters (<see cref="Query.Representative"/>). The synthetic archetype is that building's footprint scaled to the node's median floor area, with the node's median storeys (<see cref="Create.Building2D(GIS.Classes.Building2D?, double, ushort)"/>). It is rendered on demand by <c>GET /typology/representative/view</c> from <see cref="Id"/>, <see cref="CountyId"/>, <see cref="SyntheticFloorArea"/> and <see cref="SyntheticStoreys"/>, so nothing is kept on the server between the two requests.</para>
    /// </summary>
    public class TypologyRepresentativeViewModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TypologyRepresentativeViewModel"/> class.
        /// </summary>
        /// <param name="reference">The representative's reference key.</param>
        /// <param name="id">The representative's database identifier, or 0 when unknown.</param>
        /// <param name="countyId">The county part the representative is filed under.</param>
        /// <param name="buildingCount">The number of the node's buildings whose parameters were read.</param>
        /// <param name="representative">The selection's typicality figures.</param>
        /// <param name="parameters">The representative's value beside the node's typical value, one entry per definition level.</param>
        /// <param name="syntheticFloorArea">The node's median floor area in square metres, or null when no building carries one.</param>
        /// <param name="syntheticStoreys">The node's rounded median storeys, or null when no building carries one.</param>
        public TypologyRepresentativeViewModel(
            string reference,
            long id,
            int countyId,
            int buildingCount,
            Classes.Representative representative,
            List<TypologyRepresentativeParameterViewModel> parameters,
            double? syntheticFloorArea,
            int? syntheticStoreys)
        {
            Reference = reference;
            Id = id;
            CountyId = countyId;
            BuildingCount = buildingCount;
            CandidateCount = representative.CandidateCount;
            ParameterCount = representative.ParameterCount;
            Sampled = representative.Sampled;
            MeanDistance = representative.MeanDistance;
            Percentile90Distance = representative.Percentile90Distance;
            Parameters = parameters;
            SyntheticFloorArea = syntheticFloorArea;
            SyntheticStoreys = syntheticStoreys;
        }

        /// <summary> Gets the number of the node's buildings whose parameters were read. </summary>
        public int BuildingCount { get; }

        /// <summary> Gets the number of buildings that were eligible to be chosen. </summary>
        public int CandidateCount { get; }

        /// <summary> Gets the county part the representative is filed under. </summary>
        public int CountyId { get; }

        /// <summary> Gets the representative's database identifier, or 0 when unknown. </summary>
        public long Id { get; }

        /// <summary> Gets the mean distance from the representative to the node's other buildings, in interquartile ranges per parameter. </summary>
        public double MeanDistance { get; }

        /// <summary> Gets the number of numeric parameters that varied within the node and took part in the choice. </summary>
        public int ParameterCount { get; }

        /// <summary> Gets the representative's value beside the node's typical value, one entry per definition level. </summary>
        public List<TypologyRepresentativeParameterViewModel> Parameters { get; }

        /// <summary> Gets the 90th percentile of the distances from the representative to the node's other buildings. </summary>
        public double Percentile90Distance { get; }

        /// <summary> Gets the representative's reference key. </summary>
        public string Reference { get; }

        /// <summary> Gets a value indicating whether the candidates were narrowed to those nearest the median profile before the exact choice. </summary>
        public bool Sampled { get; }

        /// <summary> Gets the node's median floor area in square metres - the synthetic archetype's footprint area - or null when no building carries one. </summary>
        public double? SyntheticFloorArea { get; }

        /// <summary> Gets the node's rounded median storeys - the synthetic archetype's storeys - or null when no building carries one. </summary>
        public int? SyntheticStoreys { get; }
    }
}
