namespace DiGi.GIS.WebAPI.UI.Classes
{
    /// <summary>
    /// The outcome of a single binary-body request relayed to a Web API: the status it answered with, and the body bytes when the answer carried any.
    /// <para>The bytes counterpart of <see cref="WebAPIResponse"/>, which can only hold a text body: reading an image through that one would decode the JPEG as text and corrupt it. It is also status-preserving for the same reason that class is - the Orto Area image relay has to tell an epoch with no imagery (the upstream 404, the page's "indeterminate" badge) from a service that could not answer at all (a 503), and both must stay distinct from the image itself.</para>
    /// </summary>
    public class WebAPIBytesResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WebAPIBytesResponse"/> class.
        /// </summary>
        /// <param name="statusCode">The HTTP status code the Web API answered with.</param>
        /// <param name="bytes">The response body, or null when the response carried none.</param>
        public WebAPIBytesResponse(int statusCode, byte[]? bytes = null)
        {
            StatusCode = statusCode;
            Bytes = bytes;
        }

        /// <summary> Gets the response body, or null when the response carried none. </summary>
        public byte[]? Bytes { get; }

        /// <summary> Gets the HTTP status code the Web API answered with. </summary>
        public int StatusCode { get; }
    }
}
