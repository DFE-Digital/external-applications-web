using Microsoft.AspNetCore.Html;
using System.Threading.Tasks;
using DfE.ExternalApplications.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DfE.ExternalApplications.Web.Services
{
    public interface IFieldRendererService
    {
        Task<IHtmlContent> RenderFieldAsync(Field field, string prefix, string currentValue, string errorMessage);
    }
}