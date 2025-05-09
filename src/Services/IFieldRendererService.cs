using Microsoft.AspNetCore.Html;
using System.Threading.Tasks;
using DfE.ExternalApplications.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DfE.ExternalApplications.Web.Services
{
    public interface IFieldRendererService
    {
        Task<IHtmlContent> RenderFieldAsync(Field field, string prefix);
    }
}