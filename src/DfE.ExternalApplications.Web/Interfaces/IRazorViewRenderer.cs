namespace DfE.ExternalApplications.Web.Interfaces
{
    public interface IRazorViewRenderer
    {
        Task<string> RenderViewToHtmlAsync<TModel>(string partialName, TModel model);
    }
}
