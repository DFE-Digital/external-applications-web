using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System;
using System.Threading.Tasks;
using DfE.ExternalApplications.Domain.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace DfE.ExternalApplications.Web.Services
{
    public class FieldRendererService : IFieldRendererService
    {
        private readonly IServiceProvider _serviceProvider;

        public FieldRendererService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }
        
        public async Task<IHtmlContent> RenderFieldAsync(Field field, string prefix, string currentValue, string errorMessage)
        {
            var htmlHelper = _serviceProvider.GetRequiredService<IHtmlHelper>() as IViewContextAware;

            var actionContextAccessor = _serviceProvider.GetRequiredService<IActionContextAccessor>();
            var viewEngine = _serviceProvider.GetRequiredService<ICompositeViewEngine>();
            var tempDataProvider = _serviceProvider.GetRequiredService<ITempDataProvider>();

            var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            {
                Model = new FieldViewModel(field, prefix, currentValue, errorMessage)
            };

            var tempData = new TempDataDictionary(actionContextAccessor.ActionContext.HttpContext, tempDataProvider);

            var viewContext = new ViewContext(
                  actionContextAccessor.ActionContext,
                  new FakeView(),
                  viewData,
                  tempData,
                  TextWriter.Null,
                  new HtmlHelperOptions());

            ((IViewContextAware)htmlHelper).Contextualize(viewContext);

            var partialName = field.Type switch
            {
                "text" => "Fields/_TextField",
                "select" => "Fields/_SelectField",
                "text-area" => "Fields/_TextAreaField",
                "radios" => "Fields/_RadiosField",
                "character-count" => "Fields/_CharacterCountField",
                "date" => "Fields/_DateInputField",
                _ => throw new NotSupportedException($"Field type '{field.Type}' not supported")
            };

            return await ((IHtmlHelper)htmlHelper).PartialAsync($"~/Views/Shared/{partialName}.cshtml", viewData.Model);

        }
    }

    internal class FakeView : IView
    {
        public string Path => string.Empty;
        public System.Threading.Tasks.Task RenderAsync(ViewContext context) => System.Threading.Tasks.Task.CompletedTask;
    }
}