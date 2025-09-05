using System;
using System.Threading.Tasks;
using GovUK.Dfe.ExternalApplications.Api.Client.Security;
using Microsoft.AspNetCore.Mvc;

namespace DfE.ExternalApplications.Web.ViewComponents
{
    public class SessionTimeoutBannerViewComponent(ITokenStateManager tokenStateManager) : ViewComponent
    {
        public async Task<IViewComponentResult> InvokeAsync()
        {
            var model = new SessionTimeoutViewModel();

            var state = await tokenStateManager.GetCurrentTokenStateAsync();
            if (!state.IsAuthenticated || !state.ExternalIdpToken.ExpiryTime.HasValue)
            {
                return View(model);
            }

            // Time until we would force logout is time until 5-minute expiry threshold
            var expiryUtc = state.ExternalIdpToken.ExpiryTime.Value;
            var timeUntilForceLogout = expiryUtc - DateTime.UtcNow - TimeSpan.FromMinutes(5);

            // If we are within 2 minutes of force logout, show overlay
            if (timeUntilForceLogout > TimeSpan.Zero && timeUntilForceLogout <= TimeSpan.FromMinutes(2))
            {
                model.Show = true;
                model.AutoRedirectSeconds = (int)Math.Ceiling(timeUntilForceLogout.TotalSeconds);
                model.DisplayTime = timeUntilForceLogout.TotalMinutes >= 1
                    ? $"{(int)Math.Ceiling(timeUntilForceLogout.TotalMinutes)} minute{(timeUntilForceLogout.TotalMinutes >= 2 ? "s" : string.Empty)}"
                    : $"{model.AutoRedirectSeconds} seconds";
            }
            else if (timeUntilForceLogout > TimeSpan.FromMinutes(2))
            {
                // Not time to show overlay yet. Schedule a page refresh to the same URL
                // exactly when the 2-minute window starts, so the overlay appears without user action.
                var untilOverlay = timeUntilForceLogout - TimeSpan.FromMinutes(2);
                model.PreOverlayRefreshSeconds = (int)Math.Ceiling(untilOverlay.TotalSeconds);
            }

            return View(model);
        }
    }

    public class SessionTimeoutViewModel
    {
        public bool Show { get; set; }
        public int AutoRedirectSeconds { get; set; }
        public string DisplayTime { get; set; } = string.Empty;
        public int PreOverlayRefreshSeconds { get; set; }
    }
}


