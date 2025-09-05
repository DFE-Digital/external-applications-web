using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.Text;

namespace DfE.ExternalApplications.Web.Extensions
{
    /// <summary>
    /// Extension methods for rendering confirmation buttons
    /// </summary>
    public static class ConfirmationExtensions
    {
        /// <summary>
        /// Renders a button with optional confirmation functionality
        /// </summary>
        public static IHtmlContent RenderConfirmationButton(
            this IHtmlHelper htmlHelper,
            string buttonText,
            string handler = "Page",
            string buttonClass = "govuk-button",
            bool requiresConfirmation = false,
            string displayFields = "",
            string buttonType = "submit",
            string? buttonId = null,
            object? additionalAttributes = null,
            string? title = null)
        {
            var html = new StringBuilder();

            // Start button element
            html.Append($"<button type=\"{buttonType}\" name=\"handler\" value=\"{handler}\" class=\"{buttonClass}\"");

            // Add ID if provided
            if (!string.IsNullOrEmpty(buttonId))
            {
                html.Append($" id=\"{buttonId}\"");
            }

            // Additional attributes placeholder
            html.Append(">");
            html.Append(buttonText);
            html.AppendLine("</button>");

            if (requiresConfirmation)
            {
                html.AppendLine($"<input type=\"hidden\" name=\"confirmation-check-{handler}\" value=\"true\" />");
                if (!string.IsNullOrEmpty(displayFields))
                {
                    html.AppendLine($"<input type=\"hidden\" name=\"confirmation-display-fields-{handler}\" value=\"{displayFields}\" />");
                }
                if (!string.IsNullOrWhiteSpace(title))
                {
                    html.AppendLine($"<input type=\"hidden\" name=\"confirmation-title-{handler}\" value=\"{System.Net.WebUtility.HtmlEncode(title)}\" />");
                }
                // message removed
            }

            return new HtmlString(html.ToString());
        }

        public static IHtmlContent RenderPrimaryConfirmationButton(
            this IHtmlHelper htmlHelper,
            string buttonText,
            string handler = "Page",
            string displayFields = "",
            string? buttonId = null,
            string? title = null)
        {
            return htmlHelper.RenderConfirmationButton(
                buttonText: buttonText,
                handler: handler,
                buttonClass: "govuk-button",
                requiresConfirmation: true,
                displayFields: displayFields,
                buttonId: buttonId,
                title: title);
        }

        public static IHtmlContent RenderSecondaryConfirmationButton(
            this IHtmlHelper htmlHelper,
            string buttonText,
            string handler = "Page",
            string displayFields = "",
            string? buttonId = null,
            string? title = null)
        {
            return htmlHelper.RenderConfirmationButton(
                buttonText: buttonText,
                handler: handler,
                buttonClass: "govuk-button govuk-button--secondary",
                requiresConfirmation: true,
                displayFields: displayFields,
                buttonId: buttonId,
                title: title);
        }

        public static IHtmlContent RenderWarningConfirmationButton(
            this IHtmlHelper htmlHelper,
            string buttonText,
            string handler = "Page",
            string displayFields = "",
            string? buttonId = null,
            string? title = null)
        {
            return htmlHelper.RenderConfirmationButton(
                buttonText: buttonText,
                handler: handler,
                buttonClass: "govuk-button govuk-button--warning",
                requiresConfirmation: true,
                displayFields: displayFields,
                buttonId: buttonId,
                title: title);
        }

        public static IHtmlContent RenderLinkConfirmationButton(
            this IHtmlHelper htmlHelper,
            string buttonText,
            string handler = "Page",
            string displayFields = "",
            string? buttonId = null,
            string? title = null)
        {
            return htmlHelper.RenderConfirmationButton(
                buttonText: buttonText,
                handler: handler,
                buttonClass: "govuk-link",
                requiresConfirmation: true,
                displayFields: displayFields,
                buttonType: "submit",
                buttonId: buttonId,
                title: title);
        }
    }
}

