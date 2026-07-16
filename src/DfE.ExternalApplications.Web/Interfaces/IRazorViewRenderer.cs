using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DfE.ExternalApplications.Web.Interfaces
{
    public interface IRazorViewRenderer
    {
        Task<string> RenderPartialToStringAsync<TModel>(string partialName, TModel model, PageContext pageContext);
    }
}
