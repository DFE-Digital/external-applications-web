using DfE.ExternalApplications.Domain.Models;
using Microsoft.AspNetCore.Html;
using TaskModel = DfE.ExternalApplications.Domain.Models.Task;

namespace DfE.ExternalApplications.Web.Services
{
    public interface IFieldRendererService
    {
        Task<IHtmlContent> RenderFieldAsync(Field field, string prefix, string currentValue, IReadOnlyCollection<string> selectedValues, string errorMessage, TaskModel currentTask, Page currentPage);
    }
}