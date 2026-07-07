using DfE.ExternalApplications.Web.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace DfE.ExternalApplications.Web.Services
{
    public class RazorViewRenderer(IRazorViewEngine viewEngine, ITempDataProvider tempDataProvider, IServiceProvider serviceProvider) : IRazorViewRenderer
    {
        public async Task<string> RenderViewToHtmlAsync<TModel>(string partialName, TModel model)
        {
            var actionContext = GetActionContext();
            var view = FindView(actionContext, partialName);
            using var output = new StringWriter();
            var viewContext = new ViewContext(
                actionContext,
                view,
                new ViewDataDictionary<TModel>(metadataProvider: new EmptyModelMetadataProvider(), modelState: new ModelStateDictionary()) { Model = model },
                new TempDataDictionary(actionContext.HttpContext, tempDataProvider),
                output,
                new HtmlHelperOptions()
            );
            await view.RenderAsync(viewContext);
            return output.ToString();
        }

        private IView FindView(ActionContext actionContext, string viewName)
        {
            ViewEngineResult getViewResult = viewEngine.GetView(null, viewName, false);
            if (getViewResult.Success)
            {
                return getViewResult.View;
            }

            ViewEngineResult findViewResult = viewEngine.FindView(actionContext, viewName, false);
            if (findViewResult.Success)
            {
                return findViewResult.View;
            }

            var searchedLocations = getViewResult.SearchedLocations.Concat(findViewResult.SearchedLocations);
            var errorMessage = string.Join(Environment.NewLine, new[] { $"Unable to find view '{viewName}'. The following locations were searched:" }.Concat(searchedLocations));
            throw new InvalidOperationException(errorMessage);
        }

        private ActionContext GetActionContext()
        {
            var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
            return new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        }
    }
}
