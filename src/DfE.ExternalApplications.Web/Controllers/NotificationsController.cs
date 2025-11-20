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
    public class NotificationsController(INotificationsClient notificationsClient) : ControllerBase
    {
        [HttpGet("unread")]
        public async Task<ActionResult<IReadOnlyCollection<NotificationDto>>> GetUnreadAsync(CancellationToken cancellationToken)
        {
            var items = await notificationsClient.GetUnreadNotificationsAsync(cancellationToken);
            return Ok(items);
        }

        [HttpGet("all")]
        public async Task<ActionResult<IReadOnlyCollection<NotificationDto>>> GetAllAsync(CancellationToken cancellationToken)
        {
            var items = await notificationsClient.GetAllNotificationsAsync(cancellationToken);
            return Ok(items);
        }

        [ValidateAntiForgeryToken]
        [HttpPost("read/{id}")]
        public async Task<IActionResult> MarkAsReadAsync([FromRoute] string id, CancellationToken cancellationToken)
        {
            var ok = await notificationsClient.MarkNotificationAsReadAsync(id, cancellationToken);
            return ok ? Ok() : Problem(statusCode: 500);
        }

        [ValidateAntiForgeryToken]
        [HttpPost("read-all")]
        public async Task<IActionResult> MarkAllAsReadAsync(CancellationToken cancellationToken)
        {
            var ok = await notificationsClient.MarkAllNotificationsAsReadAsync(cancellationToken);
            return ok ? Ok() : Problem(statusCode: 500);
        }

        [ValidateAntiForgeryToken]
        [HttpPost("remove/{id}")]
        public async Task<IActionResult> RemoveAsync([FromRoute] string id, CancellationToken cancellationToken)
        {
            var ok = await notificationsClient.RemoveNotificationAsync(id, cancellationToken);
            return ok ? Ok() : Problem(statusCode: 500);
        }

        [ValidateAntiForgeryToken]
        [HttpPost("clear")]
        public async Task<IActionResult> ClearAllAsync(CancellationToken cancellationToken)
        {
            var ok = await notificationsClient.ClearAllNotificationsAsync(cancellationToken);
            return ok ? Ok() : Problem(statusCode: 500);
        }

        [ValidateAntiForgeryToken]
        [HttpPost("create")]
        public async Task<ActionResult<NotificationDto>> CreateAsync([FromBody] AddNotificationRequest request, CancellationToken cancellationToken)
        {
            var created = await notificationsClient.CreateNotificationAsync(request, cancellationToken);
            return Ok(created);
        }
    }
}