using DiGi.EPW.Classes;
using DiGi.Geometry.Planar.Classes;
using DiGi.WebAPI.Classes;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DiGi.GIS.WebAPI.UI
{
    public static partial class Query
    {
        /// <summary>
        /// Reads the EPW weather file of the station serving a location (<see cref="Constants.Default.EPWFileItemUri"/>) together with the id of its <c>epw_file</c> row, from the <see cref="Constants.Default.EPWFileIdHeaderName"/> response header.
        /// <para><c>ItemAsync</c> drops the response headers, so the id - the weather source of a stored solar radiation summary - needs this read. A host without the header (a build older than DiGi-Organization/DiGi.GIS.WebAPI#67) answers the file with a null id: the caller then solves and shows, but neither stores nor reuses.</para>
        /// </summary>
        /// <param name="httpClient">The HTTP client.</param>
        /// <param name="point2D">The location, in EPSG:2180.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to observe for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the row id (or null) and the file, or null when the request failed or answered no file.</returns>
        public static async Task<(int? Id, EPWFile EPWFile)?> EPWFileAsync(this HttpClient? httpClient, Point2D? point2D, CancellationToken cancellationToken = default)
        {
            if (httpClient is null || point2D is null)
            {
                return null;
            }

            UrlBuilder urlBuilder = new(Constants.Default.EPWFileItemUri);
            urlBuilder = urlBuilder.AddParameter("x", point2D.X);
            urlBuilder = urlBuilder.AddParameter("y", point2D.Y);

            try
            {
                using HttpResponseMessage httpResponseMessage = await httpClient.GetAsync(urlBuilder.ToString(), cancellationToken);
                if (!httpResponseMessage.IsSuccessStatusCode)
                {
                    return null;
                }

                string json = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(json) || Core.Convert.ToDiGi<EPWFile>(json)?.FirstOrDefault() is not EPWFile ePWFile)
                {
                    return null;
                }

                int? id = null;
                if (httpResponseMessage.Headers.TryGetValues(Constants.Default.EPWFileIdHeaderName, out IEnumerable<string>? values) && int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id_Parsed))
                {
                    id = id_Parsed;
                }

                return (id, ePWFile);
            }
            catch
            {
                return null;
            }
        }
    }
}
