using Microsoft.AspNetCore.Mvc;

namespace DiGi.GIS.WebAPI.UI.Controllers
{
    /// <summary>
    /// Provides the controller logic for handling requests to the home page of the GIS WebAPI user interface.
    /// </summary>
    [Route("")]
    public class HomeController : Controller
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="HomeController"/> class.
        /// <para>The landing pages carry no data of their own, so nothing is injected here.</para>
        /// </summary>
        public HomeController()
        {
        }

        // This action will trigger for: gis.digiproject.uk/
        /// <summary>
        /// Returns the view for the Start page.
        /// </summary>
        /// <returns>An <see cref="Microsoft.AspNetCore.Mvc.IActionResult"/> result.</returns>
        [HttpGet("")]
        public IActionResult Start()
        {
            // This will look for Views/Home/Start.cshtml
            return View();
        }

        // This action will trigger for: gis.digiproject.uk/about
        /// <summary>
        /// Returns the view for the About page.
        /// </summary>
        /// <returns>An <see cref="Microsoft.AspNetCore.Mvc.IActionResult"/> result.</returns>
        [HttpGet("about")]
        public IActionResult About()
        {
            return View();
        }

        // This action will trigger for: gis.digiproject.uk/assistant
        /// <summary>
        /// Returns the view for the GIS Assistant page: the AI assistant on a page of its own, opened in a new window
        /// by the assistant panel and by the home page (DiGi.GIS.WebAPI.UI#90).
        /// <para>The page carries no conversation of its own: its script continues the one whose id the opening window
        /// left in session storage, which a browser copies into a window opened by script.</para>
        /// </summary>
        /// <returns>An <see cref="IActionResult"/> holding the assistant view, or a redirect to the sign-in page for an anonymous visitor.</returns>
        [HttpGet("assistant")]
        public IActionResult Assistant()
        {
            string? tokenString = Request.Cookies[Constants.Default.UserTokenCookieName];
            if (string.IsNullOrWhiteSpace(tokenString))
            {
                // The sign-in pattern of OrtoDataController.Start: only an address inside this application is ever
                // returned to, so a crafted address cannot turn this redirect into an open redirector.
                string returnUrl = HttpContext.Request.Path + HttpContext.Request.QueryString;
                string loginUrl = Url.Content("~/login") + "?returnUrl=" + Uri.EscapeDataString(Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Content("~/"));

                return Redirect(loginUrl);
            }

            return View();
        }
    }
}