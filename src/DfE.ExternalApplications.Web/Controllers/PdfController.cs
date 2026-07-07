using DfE.ExternalApplications.Web.Interfaces;
using DfE.ExternalApplications.Web.Pages.FormEngine;
using Microsoft.AspNetCore.Mvc;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace DfE.ExternalApplications.Web.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class PdfController(RenderFormModel model/*IServiceProvider serviceProvider*/, IRazorViewRenderer renderer) : ControllerBase
    {
        private static string ChromiumPath => Path.Combine(Path.GetTempPath());
        private static string ChromeBuildId => "150.0.7871.47"; // Chrome.DefaultBuildId

        /// <summary>
        /// Generate PDF from a given URL
        /// </summary>
        [HttpGet]
        [Route("generate")]
        public async Task<IActionResult> Generate(string url, string name)
        {
            if (string.IsNullOrEmpty(url))
            {
                return BadRequest("The URL parameter is mandatory.");
            }

            if (string.IsNullOrEmpty(name))
            {
                return BadRequest("The Name parameter is mandatory.");
            }

            // generate PDF with puppeteer
            var bf = await InitializeBrowser();
            var pdfFileName = name + ".pdf";
            byte[] pdfBytes = [];
            using (var browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true, ExecutablePath = bf.GetExecutablePath(ChromeBuildId) }))
            {
                using var page = await browser.NewPageAsync();
                await page.EmulateMediaTypeAsync(PuppeteerSharp.Media.MediaType.Print);
                await page.SetViewportAsync(new ViewPortOptions { Width = 1200, Height = 800 });
                await page.GoToAsync(url);
                await page.EvaluateExpressionHandleAsync("document.fonts.ready");
                var contentSize = await page.EvaluateFunctionAsync<ContentSize>("() => { return { width: document.documentElement.scrollWidth, height: document.documentElement.scrollHeight }; }");
                await page.SetViewportAsync(new ViewPortOptions { Width = contentSize.Width, Height = contentSize.Height });
                pdfBytes = await page.PdfDataAsync(new PdfOptions()
                {
                    PrintBackground = true,
                    Height = contentSize.Height,
                    Width = contentSize.Width,
                });
            }
            var streamResult = new MemoryStream(pdfBytes);
            return File(streamResult, "application/pdf", pdfFileName);
        }

        /// <summary>
        /// Generate PDF from Razor view template
        /// </summary>
        [HttpPost]
        [Route("generatepdf")]
        public async Task<IActionResult> GeneratePdf(Guid applicationId)
        {
            // TODO SP create view model using applicationId and pass it to the view
            //RenderFormModel? model = serviceProvider.GetService<RenderFormModel>();
            await model!.OnGetAsync();

            var html = await renderer.RenderViewToHtmlAsync("_ApplicationPreview", model);

            byte[] pdfBytes = [];
            var bf = await InitializeBrowser();
            using (IBrowser browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true, ExecutablePath = bf.GetExecutablePath(ChromeBuildId) }))
            {
                PdfOptions pdfOptions = new() { PrintBackground = true, Format = PaperFormat.A4 };
                using IPage page = await browser.NewPageAsync();
                await page.SetContentAsync(html);
                pdfBytes = await page.PdfDataAsync(pdfOptions);
            }
            var streamResult = new MemoryStream(pdfBytes);
            return File(streamResult, "application/pdf", "test.pdf");
        }

        private static async Task<BrowserFetcher> InitializeBrowser()
        {
            var browserFetcher = new BrowserFetcher(new BrowserFetcherOptions { Path = ChromiumPath });
            await browserFetcher.DownloadAsync(ChromeBuildId);
            return browserFetcher;
        }
    }

    internal class ContentSize
    {
        public int Height { get; set; }
        public int Width { get; set; }
    }
}
