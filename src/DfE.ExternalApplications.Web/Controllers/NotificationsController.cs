using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.ExternalApplications.Web.Controllers
{
    [ApiController]
    [Route("notifications")]
    [Authorize]
    public class NotificationsController(INotificationsClient notificationsClient,
        IConfiguration configuration) : ControllerBase
    {
        private readonly string _context = configuration["ApplicationName"] ?? "Transfers";

        [HttpGet("unread")]
        public Task<IActionResult> GetUnreadAsync(CancellationToken cancellationToken) =>
            ExecuteAsync(() => notificationsClient.GetUnreadNotificationsAsync(_context, null, cancellationToken));

        [HttpGet("all")]
        public Task<IActionResult> GetAllAsync(CancellationToken cancellationToken) =>
            ExecuteAsync(() => notificationsClient.GetAllNotificationsAsync(_context, null, cancellationToken));

        [ValidateAntiForgeryToken]
        [HttpPost("read/{id}")]
        public Task<IActionResult> MarkAsReadAsync([FromRoute] string id, CancellationToken cancellationToken) =>
            ExecuteAsync(async () =>
            {
                var ok = await notificationsClient.MarkNotificationAsReadAsync(id, cancellationToken);
                return ok;
            });

        [ValidateAntiForgeryToken]
        [HttpPost("read-all")]
        public Task<IActionResult> MarkAllAsReadAsync(CancellationToken cancellationToken) =>
            ExecuteAsync(async () =>
            {
                var ok = await notificationsClient.MarkAllNotificationsAsReadAsync(_context, null, cancellationToken);
                return ok;
            });

        [ValidateAntiForgeryToken]
        [HttpPost("remove/{id}")]
        public Task<IActionResult> RemoveAsync([FromRoute] string id, CancellationToken cancellationToken) =>
            ExecuteAsync(async () =>
            {
                var ok = await notificationsClient.RemoveNotificationAsync(id, cancellationToken);
                return ok;
            });

        [ValidateAntiForgeryToken]
        [HttpPost("clear")]
        public Task<IActionResult> ClearAllAsync(CancellationToken cancellationToken) =>
            ExecuteAsync(async () =>
            {
                var ok = await notificationsClient.ClearNotificationsByContextAsync(_context, cancellationToken);
                return ok;
            });

        [ValidateAntiForgeryToken]
        [HttpPost("create")]
        public Task<IActionResult> CreateAsync([FromBody] AddNotificationRequest request, CancellationToken cancellationToken)
        {
            request.Context = _context;
            return ExecuteAsync(() => notificationsClient.CreateNotificationAsync(request, cancellationToken));
        }

        private async Task<IActionResult> ExecuteAsync<T>(Func<Task<T>> action)
        {
            try
            {
                return Ok(await action());
            }
            catch (ExternalApplicationsException ex) when (ex.StatusCode is 401 or 403)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        private async Task<IActionResult> ExecuteAsync(Func<Task<bool>> action)
        {
            try
            {
                var ok = await action();
                return ok ? Ok() : Problem(statusCode: 500);
            }
            catch (ExternalApplicationsException ex) when (ex.StatusCode is 401 or 403)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }
    }
}
