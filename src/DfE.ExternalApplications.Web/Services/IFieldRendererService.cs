using DfE.ExternalApplications.Domain.Models;
using Microsoft.AspNetCore.Html;

namespace DfE.ExternalApplications.Web.Services
{
    public interface IFieldRendererService
    {
        Task<IHtmlContent> RenderFieldAsync(Field field, string prefix, string currentValue, string errorMessage, string taskName);
    }
}