namespace DiGi.GIS.WebAPI.UI.ViewModels
{
    /// <summary>
    /// One parameter of a typology node's representative: the representative's value beside the node's typical value.
    /// <para>The values are rendered on the server with the invariant culture: numbers with at most two decimals, categories as their text. The typical value is the median for a numeric parameter and the modal value for a categorical one.</para>
    /// </summary>
    public class TypologyRepresentativeParameterViewModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TypologyRepresentativeParameterViewModel"/> class.
        /// </summary>
        /// <param name="name">The display name of the parameter's column.</param>
        /// <param name="isNumeric">A value indicating whether the parameter was compared as a number.</param>
        /// <param name="value">The representative's value, or null when it carries none.</param>
        /// <param name="typical">The node's median (numeric) or modal (categorical) value, or null when no building carries one.</param>
        public TypologyRepresentativeParameterViewModel(string? name, bool isNumeric, string? value, string? typical)
        {
            Name = name;
            IsNumeric = isNumeric;
            Value = value;
            Typical = typical;
        }

        /// <summary> Gets a value indicating whether the parameter was compared as a number. </summary>
        public bool IsNumeric { get; }

        /// <summary> Gets the display name of the parameter's column. </summary>
        public string? Name { get; }

        /// <summary> Gets the node's median (numeric) or modal (categorical) value, or null when no building carries one. </summary>
        public string? Typical { get; }

        /// <summary> Gets the representative's value, or null when it carries none. </summary>
        public string? Value { get; }
    }
}
