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
        /// <param name="htmlHelper">The HTML helper</param>
        /// <param name="buttonText">The text to display on the button</param>
        /// <param name="handler">The page handler to call (default: "Page")</param>
        /// <param name="buttonClass">The CSS classes for the button (default: "govuk-button")</param>
        /// <param name="requiresConfirmation">Whether this button requires confirmation</param>
        /// <param name="displayFields">Comma-separated list of fields to display on confirmation page</param>
        /// <param name="buttonType">The button type (default: "submit")</param>
        /// <param name="buttonId">Optional ID for the button</param>
        /// <param name="additionalAttributes">Additional HTML attributes</param>
        /// <returns>HTML content for the button and hidden confirmation inputs</returns>
        public static IHtmlContent RenderConfirmationButton(
            this IHtmlHelper htmlHelper,
            string buttonText,
            string handler = "Page",
            string buttonClass = "govuk-button",
            bool requiresConfirmation = false,
            string displayFields = "",
            string buttonType = "submit",
            string? buttonId = null,
            object? additionalAttributes = null)
        {
            var html = new StringBuilder();
            
            // Start button element
            html.Append($"<button type=\"{buttonType}\" name=\"handler\" value=\"{handler}\" class=\"{buttonClass}\"");
            
            // Add ID if provided
            if (!string.IsNullOrEmpty(buttonId))
            {
                html.Append($" id=\"{buttonId}\"");
            }
            
            // Add additional attributes if provided
            if (additionalAttributes != null)
            {
                // For now, we'll skip additional attributes to avoid compilation issues
                // This can be enhanced later if needed
            }
            
            html.Append(">");
            html.Append(buttonText);
            html.AppendLine("</button>");
            
            // If confirmation is required, add hidden inputs with confirmation data
            if (requiresConfirmation)
            {
                html.AppendLine($"<input type=\"hidden\" name=\"confirmation-check-{handler}\" value=\"true\" />");
                
                if (!string.IsNullOrEmpty(displayFields))
                {
                    html.AppendLine($"<input type=\"hidden\" name=\"confirmation-display-fields-{handler}\" value=\"{displayFields}\" />");
                }
            }
            
            return new HtmlString(html.ToString());
        }

        /// <summary>
        /// Renders a primary button with confirmation
        /// </summary>
        /// <param name="htmlHelper">The HTML helper</param>
        /// <param name="buttonText">The text to display on the button</param>
        /// <param name="handler">The page handler to call</param>
        /// <param name="displayFields">Comma-separated list of fields to display on confirmation page</param>
        /// <param name="buttonId">Optional ID for the button</param>
        /// <returns>HTML content for the confirmation button</returns>
        public static IHtmlContent RenderPrimaryConfirmationButton(
            this IHtmlHelper htmlHelper,
            string buttonText,
            string handler = "Page",
            string displayFields = "",
            string? buttonId = null)
        {
            return htmlHelper.RenderConfirmationButton(
                buttonText: buttonText,
                handler: handler,
                buttonClass: "govuk-button",
                requiresConfirmation: true,
                displayFields: displayFields,
                buttonId: buttonId);
        }

        /// <summary>
        /// Renders a secondary button with confirmation
        /// </summary>
        /// <param name="htmlHelper">The HTML helper</param>
        /// <param name="buttonText">The text to display on the button</param>
        /// <param name="handler">The page handler to call</param>
        /// <param name="displayFields">Comma-separated list of fields to display on confirmation page</param>
        /// <param name="buttonId">Optional ID for the button</param>
        /// <returns>HTML content for the confirmation button</returns>
        public static IHtmlContent RenderSecondaryConfirmationButton(
            this IHtmlHelper htmlHelper,
            string buttonText,
            string handler = "Page",
            string displayFields = "",
            string? buttonId = null)
        {
            return htmlHelper.RenderConfirmationButton(
                buttonText: buttonText,
                handler: handler,
                buttonClass: "govuk-button govuk-button--secondary",
                requiresConfirmation: true,
                displayFields: displayFields,
                buttonId: buttonId);
        }

        /// <summary>
        /// Renders a warning button with confirmation
        /// </summary>
        /// <param name="htmlHelper">The HTML helper</param>
        /// <param name="buttonText">The text to display on the button</param>
        /// <param name="handler">The page handler to call</param>
        /// <param name="displayFields">Comma-separated list of fields to display on confirmation page</param>
        /// <param name="buttonId">Optional ID for the button</param>
        /// <returns>HTML content for the confirmation button</returns>
        public static IHtmlContent RenderWarningConfirmationButton(
            this IHtmlHelper htmlHelper,
            string buttonText,
            string handler = "Page",
            string displayFields = "",
            string? buttonId = null)
        {
            return htmlHelper.RenderConfirmationButton(
                buttonText: buttonText,
                handler: handler,
                buttonClass: "govuk-button govuk-button--warning",
                requiresConfirmation: true,
                displayFields: displayFields,
                buttonId: buttonId);
        }

        /// <summary>
        /// Renders a link-style button with confirmation
        /// </summary>
        /// <param name="htmlHelper">The HTML helper</param>
        /// <param name="buttonText">The text to display on the button</param>
        /// <param name="handler">The page handler to call</param>
        /// <param name="displayFields">Comma-separated list of fields to display on confirmation page</param>
        /// <param name="buttonId">Optional ID for the button</param>
        /// <returns>HTML content for the confirmation button</returns>
        public static IHtmlContent RenderLinkConfirmationButton(
            this IHtmlHelper htmlHelper,
            string buttonText,
            string handler = "Page",
            string displayFields = "",
            string? buttonId = null)
        {
            return htmlHelper.RenderConfirmationButton(
                buttonText: buttonText,
                handler: handler,
                buttonClass: "govuk-link",
                requiresConfirmation: true,
                displayFields: displayFields,
                buttonType: "submit",
                buttonId: buttonId);
        }
    }
}
