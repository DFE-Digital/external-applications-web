namespace DfE.ExternalApplications.Domain.Models
{
    /// <summary>
    /// Model for displaying confirmation information to the user
    /// </summary>
    public class ConfirmationDisplayModel
    {
        /// <summary>
        /// The title to display on the confirmation page
        /// </summary>
        public string Title { get; set; } = "Confirm your action";

        /// <summary>
        /// The message to display to the user
        /// </summary>
        public string Message { get; set; } = "Please review the following information:";

        /// <summary>
        /// The formatted data to display to the user
        /// Key = Display name, Value = Formatted value
        /// </summary>
        public Dictionary<string, string> DisplayData { get; set; } = new();

        /// <summary>
        /// The URL to return to if the user cancels
        /// </summary>
        public string ReturnUrl { get; set; } = string.Empty;

        /// <summary>
        /// The confirmation token
        /// </summary>
        public string ConfirmationToken { get; set; } = string.Empty;

        /// <summary>
        /// The original action URL to post back to (including handler)
        /// </summary>
        public string OriginalActionUrl { get; set; } = string.Empty;

        /// <summary>
        /// The original form data to be reposted to the original action
        /// </summary>
        public Dictionary<string, object> OriginalFormData { get; set; } = new();
    }
}
