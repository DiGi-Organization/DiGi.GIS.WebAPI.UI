using DiGi.Analytical.Building.HVAC.Interfaces;
using DiGi.GIS.WebAPI.UI.Enums;
using System.Collections.Generic;

namespace DiGi.GIS.WebAPI.UI.ViewModels
{
    /// <summary>
    /// Represents a view model of the regulated heat transfer coefficients.
    /// </summary>
    public class RegulatedHeatTransferCoefficientsViewModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RegulatedHeatTransferCoefficientsViewModel"/> class.
        /// </summary>
        public RegulatedHeatTransferCoefficientsViewModel()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RegulatedHeatTransferCoefficientsViewModel"/> class.
        /// </summary>
        /// <param name="year">The year associated with the regulated heat transfer coefficients.</param>
        /// <param name="regulatedHeatTransferCoefficients">The regulated heat transfer coefficients.</param>
        /// <param name="regulatedBuildingCategory">The building category the limits were selected for.</param>
        /// <param name="names">The names of the heat transfer coefficients U(max) that apply to the building. This value can be null.</param>
        /// <param name="minimumThermalResistanceNames">The names of the minimum thermal resistances R(min) that apply to the building. This value can be null.</param>
        /// <param name="energyIndicatorRequirement">A value indicating whether the regulation limits the building by its seasonal heat demand indicator E instead of the single-family limits left out.</param>
        public RegulatedHeatTransferCoefficientsViewModel(short year, IRegulatedHeatTransferCoefficients? regulatedHeatTransferCoefficients, RegulatedBuildingCategory regulatedBuildingCategory, IEnumerable<string>? names, IEnumerable<string>? minimumThermalResistanceNames, bool energyIndicatorRequirement)
        {
            Year = year;
            RegulatedHeatTransferCoefficients = regulatedHeatTransferCoefficients;
            RegulatedBuildingCategory = regulatedBuildingCategory;
            Names = names is null ? [] : [.. names];
            MinimumThermalResistanceNames = minimumThermalResistanceNames is null ? [] : [.. minimumThermalResistanceNames];
            EnergyIndicatorRequirement = energyIndicatorRequirement;
        }

        /// <summary> Gets a value indicating whether the regulation limits the building by its seasonal heat demand indicator E (E ≤ E0) instead of the single-family limits left out of <see cref="Names"/>. </summary>
        public bool EnergyIndicatorRequirement { get; }

        /// <summary> Gets the names of the minimum thermal resistances R(min) [(m2K)/W] that apply to the building. </summary>
        public List<string> MinimumThermalResistanceNames { get; } = [];

        /// <summary> Gets the names of the heat transfer coefficients U(max) [W/(m2K)] that apply to the building. </summary>
        public List<string> Names { get; } = [];

        /// <summary> Gets the building category the limits were selected for. </summary>
        public RegulatedBuildingCategory RegulatedBuildingCategory { get; }

        /// <summary> Gets the regulated heat transfer coefficients. </summary>
        public IRegulatedHeatTransferCoefficients? RegulatedHeatTransferCoefficients { get; }

        /// <summary> Gets the year associated with the regulated heat transfer coefficients. </summary>
        public short Year { get; }
    }
}
