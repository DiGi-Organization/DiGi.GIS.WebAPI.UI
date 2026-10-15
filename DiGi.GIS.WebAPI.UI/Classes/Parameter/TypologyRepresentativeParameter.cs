using System.Collections.Generic;

namespace DiGi.GIS.WebAPI.UI.Classes
{
    /// <summary>
    /// The request body of <c>POST /typology/representative</c>: the Typology definition the node was solved with, and the node's buildings.
    /// <para>The area view already holds the node's buildings from the solve, so they are sent rather than solved again. The definition names the parameters the representative is chosen by - the columns of its levels.</para>
    /// </summary>
    public class TypologyRepresentativeParameter
    {
        /// <summary>
        /// Gets or sets the Typology definition page state the node was solved with.
        /// </summary>
        public TypologyDefinitionParameter? Definition { get; set; }

        /// <summary>
        /// Gets or sets the buildings of the node.
        /// </summary>
        public List<TypologyRepresentativeBuildingParameter>? Buildings { get; set; }
    }
}
