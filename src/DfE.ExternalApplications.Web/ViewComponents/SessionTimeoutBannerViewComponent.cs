using System;
using System.Threading.Tasks;
using DfE.ExternalApplications.Web.Security;
using GovUK.Dfe.ExternalApplications.Api.Client.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DfE.ExternalApplications.Web.ViewComponents
{
    /// <summary>
    /// View component that provides session timeout configuration to the client.
    /// The actual inactivity tracking is done client-side via JavaScript.
    /// </summary>
    public class SessionTimeoutBannerViewComponent(
        IHttpContextAccessor httpContextAccessor,
        IOptions<TokenRefreshSettings> tokenRefreshSettings) : ViewComponent
    {
        private readonly TokenRefreshSettings _settings = tokenRefreshSettings.Value;

        /// <summary>
        /// Warning window in minutes - show the overlay this many minutes before logout
        /// </summary>
        private const int WarningWindowMinutes = 1;

        public Task<IViewComponentResult> InvokeAsync()
        {
            var model = new SessionTimeoutViewModel();
            var context = httpContextAccessor.HttpContext;

            if (context?.User?.Identity?.IsAuthenticated != true)
            {
                return Task.FromResult<IViewComponentResult>(View(model));
            }

            // Provide configuration to the client-side JavaScript
            model.IsAuthenticated = true;
            model.InactivityTimeoutSeconds = _settings.InactivityThresholdMinutes * 60;
            model.WarningBeforeTimeoutSeconds = WarningWindowMinutes * 60;

            return Task.FromResult<IViewComponentResult>(View(model));
        }
    }

    /// <summary>
    /// View model for the session timeout banner.
    /// Provides configuration for client-side inactivity tracking.
    /// </summary>
    public class SessionTimeoutViewModel
    {
        /// <summary>
        /// Whether the user is authenticated (banner only applies to authenticated users)
        /// </summary>
        public bool IsAuthenticated { get; set; }

        /// <summary>
        /// Total seconds of inactivity before forcing logout
        /// </summary>
        public int InactivityTimeoutSeconds { get; set; }

        /// <summary>
        /// Seconds before timeout to show the warning
        /// </summary>
        public int WarningBeforeTimeoutSeconds { get; set; }

        // Legacy properties kept for backward compatibility
        public bool Show { get; set; }
        public int AutoRedirectSeconds { get; set; }
        public string DisplayTime { get; set; } = string.Empty;
        public int PreOverlayRefreshSeconds { get; set; }
    }
}
