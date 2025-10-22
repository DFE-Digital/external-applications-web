namespace DfE.ExternalApplications.Web.Pages.FormEngine;

public static class DisplayHelpers
{
    /// <summary>
    /// Generates a success message using custom template or fallback default
    /// </summary>
    /// <param name="customMessage">Custom message template from configuration</param>
    /// <param name="operation">Operation type: "add", "update", or "delete"</param>
    /// <param name="itemData">Dictionary containing all field values for the item</param>
    /// <param name="flowTitle">Title of the flow</param>
    /// <returns>Formatted success message</returns>
    public static string GenerateSuccessMessage(string? customMessage, string operation, Dictionary<string, object>? itemData, string? flowTitle)
    {
        // If custom message is provided, use it with placeholder substitution
        if (!string.IsNullOrEmpty(customMessage))
        {
            var message = customMessage;
            
            // Replace {flowTitle} placeholder
            message = message.Replace("{flowTitle}", flowTitle ?? "collection");
            
            // Replace field-based placeholders like {firstName}, {gender}, etc.
            if (itemData != null)
            {
                foreach (var kvp in itemData)
                {
                    var placeholder = $"{{{kvp.Key}}}";
                    var value = kvp.Value?.ToString() ?? "";
                    message = message.Replace(placeholder, value);
                }
            }
            
            return message;
        }

        // Fallback to default messages - try to use itemTitleBinding or first available field
        string displayName = "Item";
        if (itemData != null && itemData.Any())
        {
            // Try common name fields first, then fall back to any non-empty value
            var nameFields = new[] { "firstName", "name", "title", "label" };
            var nameField = nameFields.FirstOrDefault(field => itemData.ContainsKey(field) && !string.IsNullOrEmpty(itemData[field]?.ToString()));
            
            if (nameField != null)
            {
                displayName = itemData[nameField]?.ToString() ?? "Item";
            }
            else
            {
                // Use the first non-empty field value
                var firstValue = itemData.Values.FirstOrDefault(v => !string.IsNullOrEmpty(v?.ToString()));
                if (firstValue != null)
                {
                    displayName = firstValue.ToString() ?? "Item";
                }
            }
        }

        var lowerFlowTitle = flowTitle?.ToLowerInvariant() ?? "collection";

        return operation switch
        {
            "add" => $"{displayName} has been added to {lowerFlowTitle}",
            "update" => $"{displayName} has been updated",
            "delete" => $"{displayName} has been removed from {lowerFlowTitle}",
            _ => $"{displayName} has been processed"
        };
    }
}