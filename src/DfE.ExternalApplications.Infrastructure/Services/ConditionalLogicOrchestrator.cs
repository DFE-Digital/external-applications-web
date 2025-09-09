using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DfE.ExternalApplications.Infrastructure.Services;

/// <summary>
/// Service for orchestrating conditional logic execution across the form engine
/// </summary>
public class ConditionalLogicOrchestrator(
    IConditionalLogicEngine conditionalLogicEngine,
    ILogger<ConditionalLogicOrchestrator> logger) : IConditionalLogicOrchestrator
{
    public async Task<FormConditionalState> ApplyConditionalLogicAsync(FormTemplate template,
        Dictionary<string, object> formData,
        ConditionalLogicContext? context = null)
    {
        var state = new FormConditionalState();

        try
        {
            logger.LogInformation("[CONDITIONAL DEBUG] ApplyConditionalLogicAsync called for template {TemplateId}", template.TemplateId);
            
            if (template.ConditionalLogic == null || !template.ConditionalLogic.Any())
            {
                logger.LogInformation("[CONDITIONAL DEBUG] No conditional logic defined, returning default state");
                // No conditional logic defined, return default state
                InitializeDefaultState(template, state);
                return state;
            }

            logger.LogInformation("[CONDITIONAL DEBUG] Evaluating {RuleCount} conditional logic rules", template.ConditionalLogic.Count);

            // Evaluate all conditional logic rules
            var result = conditionalLogicEngine.EvaluateRules(template.ConditionalLogic, formData, context);
            state.EvaluationResult = result;

            logger.LogInformation("[CONDITIONAL DEBUG] Conditional logic evaluation result:");
            logger.LogInformation("[CONDITIONAL DEBUG] - EvaluatedRules count: {Count}", result.EvaluatedRules.Count);
            logger.LogInformation("[CONDITIONAL DEBUG] - Actions count: {Count}", result.Actions.Count);
            
            foreach (var rule in result.EvaluatedRules.Take(5)) // Log first 5 rules
            {
                logger.LogInformation("[CONDITIONAL DEBUG] - Rule: {RuleId}", rule);
            }

            // Initialize with default state
            InitializeDefaultState(template, state);

            // Apply actions from conditional logic
            foreach (var action in result.Actions)
            {
                logger.LogInformation("[CONDITIONAL DEBUG] - Applying action from rule {RuleId} on element {ElementId}", action.RuleId, action.Element.ElementId);
            }
            await ApplyActionsAsync(result.Actions, state, template, formData);

            logger.LogDebug("Applied conditional logic for template '{TemplateId}', {RuleCount} rules evaluated",
                template.TemplateId, result.EvaluatedRules.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying conditional logic for template '{TemplateId}'", template.TemplateId);
        }

        return state;
    }

    public async Task<ConditionalLogicResult> EvaluateFieldChangeAsync(FormTemplate template,
        Dictionary<string, object> formData,
        string changedFieldId,
        ConditionalLogicContext? context = null)
    {
        try
        {
            if (template.ConditionalLogic == null || !template.ConditionalLogic.Any())
            {
                return new ConditionalLogicResult();
            }

            // Get only rules that are triggered by this field
            var triggeredRules = conditionalLogicEngine.GetTriggeredRules(template.ConditionalLogic, changedFieldId);

            // Evaluate only the triggered rules
            var result = conditionalLogicEngine.EvaluateRules(triggeredRules, formData, context);

            logger.LogDebug("Evaluated field change for '{FieldId}', {RuleCount} rules triggered",
                changedFieldId, triggeredRules.Count());

            return await System.Threading.Tasks.Task.FromResult(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error evaluating field change for '{FieldId}'", changedFieldId);
            return new ConditionalLogicResult
            {
                Errors = { $"Error evaluating field change: {ex.Message}" }
            };
        }
    }

    public async Task<Dictionary<string, bool>> GetElementVisibilityAsync(FormTemplate template,
        Dictionary<string, object> formData,
        ConditionalLogicContext? context = null)
    {
        var visibility = new Dictionary<string, bool>();

        try
        {
            var state = await ApplyConditionalLogicAsync(template, formData, context);
            
            // Combine field and page visibility
            foreach (var kvp in state.FieldVisibility)
            {
                visibility[kvp.Key] = kvp.Value;
            }

            foreach (var kvp in state.PageVisibility)
            {
                visibility[kvp.Key] = kvp.Value;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting element visibility");
        }

        return visibility;
    }

    public async Task<Dictionary<string, bool>> GetFieldRequiredStateAsync(FormTemplate template,
        Dictionary<string, object> formData,
        ConditionalLogicContext? context = null)
    {
        try
        {
            var state = await ApplyConditionalLogicAsync(template, formData, context);
            return state.FieldRequired;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting field required state");
            return new Dictionary<string, bool>();
        }
    }

    public async Task<List<ConditionalLogicValidationResult>> ValidateTemplateRulesAsync(FormTemplate template)
    {
        var results = new List<ConditionalLogicValidationResult>();

        try
        {
            if (template.ConditionalLogic == null || !template.ConditionalLogic.Any())
            {
                return results;
            }

            foreach (var rule in template.ConditionalLogic)
            {
                var validationResult = conditionalLogicEngine.ValidateRule(rule);
                results.Add(validationResult);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error validating template rules for '{TemplateId}'", template.TemplateId);
            results.Add(new ConditionalLogicValidationResult
            {
                IsValid = false,
                Errors = { $"Validation error: {ex.Message}" }
            });
        }

        return await System.Threading.Tasks.Task.FromResult(results);
    }

    public async Task<string?> GetNextPageAsync(FormTemplate template,
        Dictionary<string, object> formData,
        string currentPageId,
        ConditionalLogicContext? context = null)
    {
        try
        {
            logger.LogInformation("[CONDITIONAL DEBUG] GetNextPageAsync called - currentPageId: {CurrentPageId}, contextTaskId: {TaskId}", currentPageId, context?.CurrentTaskId ?? "null");
            
            // Log form data for debugging
            logger.LogInformation("[CONDITIONAL DEBUG] Form data contains {Count} entries:", formData.Count);
            foreach (var kv in formData.Take(10)) // Log first 10 entries
            {
                logger.LogInformation("[CONDITIONAL DEBUG] FormData[{Key}] = {Value}", kv.Key, kv.Value?.ToString() ?? "null");
            }

            var state = await ApplyConditionalLogicAsync(template, formData, context);

            logger.LogInformation("[CONDITIONAL DEBUG] Conditional state after applying logic:");
            logger.LogInformation("[CONDITIONAL DEBUG] - SkippedPages: {SkippedPages}", string.Join(", ", state.SkippedPages));
            logger.LogInformation("[CONDITIONAL DEBUG] - PageVisibility count: {Count}", state.PageVisibility.Count);
            foreach (var pv in state.PageVisibility)
            {
                logger.LogInformation("[CONDITIONAL DEBUG] - PageVisibility[{PageId}] = {IsVisible}", pv.Key, pv.Value);
            }
            logger.LogInformation("[CONDITIONAL DEBUG] - FieldVisibility count: {Count}", state.FieldVisibility.Count);
            foreach (var fv in state.FieldVisibility)
            {
                logger.LogInformation("[CONDITIONAL DEBUG] - FieldVisibility[{FieldId}] = {IsVisible}", fv.Key, fv.Value);
            }

            // Find the current page and get the next one in sequence
            var allPages = GetAllPages(template);
            var currentPageIndex = allPages.FindIndex(p => p.PageId == currentPageId);

            logger.LogInformation("[CONDITIONAL DEBUG] All pages in order: {Pages}", string.Join(", ", allPages.Select(p => p.PageId)));
            logger.LogInformation("[CONDITIONAL DEBUG] Current page index: {Index}", currentPageIndex);

            if (currentPageIndex == -1 || currentPageIndex >= allPages.Count - 1)
            {
                logger.LogInformation("[CONDITIONAL DEBUG] Current page not found or is last page, returning null");
                return null; // Last page or page not found
            }

            // Look for the next visible page
            for (int i = currentPageIndex + 1; i < allPages.Count; i++)
            {
                var nextPage = allPages[i];
                logger.LogInformation("[CONDITIONAL DEBUG] Checking page {Index}: {PageId}", i, nextPage.PageId);
                
                // Check if this page should be skipped
                if (state.SkippedPages.Contains(nextPage.PageId))
                {
                    logger.LogInformation("[CONDITIONAL DEBUG] - Page {PageId} is in SkippedPages, continuing", nextPage.PageId);
                    continue;
                }

                // Check if this page is visible
                if (state.PageVisibility.TryGetValue(nextPage.PageId, out var isVisible) && !isVisible)
                {
                    logger.LogInformation("[CONDITIONAL DEBUG] - Page {PageId} has visibility=false, continuing", nextPage.PageId);
                    continue;
                }

                // NEW: Check if all fields on the next page are hidden by conditional logic
                var nextPageFields = GetFieldsForPage(template, nextPage.PageId);
                logger.LogInformation("[CONDITIONAL DEBUG] - Page {PageId} has {FieldCount} fields: {Fields}", 
                    nextPage.PageId, nextPageFields.Count, string.Join(", ", nextPageFields.Select(f => f.FieldId)));
                
                if (nextPageFields.Any() && nextPageFields.All(f => state.FieldVisibility.TryGetValue(f.FieldId, out var fieldIsVisible) && !fieldIsVisible))
                {
                    logger.LogInformation("[CONDITIONAL DEBUG] - Skipping page '{PageId}' because all its fields are hidden by conditional logic", nextPage.PageId);
                    continue;
                }

                logger.LogInformation("[CONDITIONAL DEBUG] - Selected next page: {PageId}", nextPage.PageId);
                return nextPage.PageId;
            }

            logger.LogInformation("[CONDITIONAL DEBUG] No more pages found, returning null");
            return null; // No more visible pages
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting next page for '{CurrentPageId}'", currentPageId);
            return null;
        }
    }

    public async Task<bool> ShouldSkipPageAsync(FormTemplate template,
        Dictionary<string, object> formData,
        string pageId,
        ConditionalLogicContext? context = null)
    {
        try
        {
            var state = await ApplyConditionalLogicAsync(template, formData, context);
            
            // Check if page is in skipped list
            if (state.SkippedPages.Contains(pageId))
            {
                return true;
            }

            // Check if page is hidden
            if (state.PageVisibility.TryGetValue(pageId, out var isVisible) && !isVisible)
            {
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking if page '{PageId}' should be skipped", pageId);
            return false;
        }
    }

    #region Private Helper Methods

    private void InitializeDefaultState(FormTemplate template, FormConditionalState state)
    {
        // Initialize all fields and pages as visible and enabled by default
        var allPages = GetAllPages(template);
        var allFields = GetAllFields(template);

        foreach (var page in allPages)
        {
            state.PageVisibility[page.PageId] = true;
        }

        foreach (var field in allFields)
        {
            state.FieldVisibility[field.FieldId] = true;
            state.FieldEnabled[field.FieldId] = true;
            state.FieldRequired[field.FieldId] = field.Required ?? false;
        }
    }

    private async System.Threading.Tasks.Task ApplyActionsAsync(List<ConditionalLogicAction> actions,
        FormConditionalState state,
        FormTemplate template,
        Dictionary<string, object> formData)
    {
        foreach (var action in actions.OrderBy(a => a.Priority))
        {
            try
            {
                await ApplyActionAsync(action, state, template, formData);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error applying action '{Action}' for element '{ElementId}' from rule '{RuleId}'",
                    action.Element.Action, action.Element.ElementId, action.RuleId);
            }
        }
    }

    private async System.Threading.Tasks.Task ApplyActionAsync(ConditionalLogicAction action,
        FormConditionalState state,
        FormTemplate template,
        Dictionary<string, object> formData)
    {
        var element = action.Element;

        switch (element.Action.ToLowerInvariant())
        {
            case ConditionalLogicConstants.Actions.Show:
                ApplyShowAction(element, state);
                break;

            case ConditionalLogicConstants.Actions.Hide:
                ApplyHideAction(element, state);
                break;

            case ConditionalLogicConstants.Actions.Skip:
                ApplySkipAction(element, state);
                break;

            case ConditionalLogicConstants.Actions.Require:
                ApplyRequireAction(element, state);
                break;

            case ConditionalLogicConstants.Actions.MakeOptional:
                ApplyMakeOptionalAction(element, state);
                break;

            case ConditionalLogicConstants.Actions.Enable:
                ApplyEnableAction(element, state);
                break;

            case ConditionalLogicConstants.Actions.Disable:
                ApplyDisableAction(element, state);
                break;

            case ConditionalLogicConstants.Actions.SetValue:
                ApplySetValueAction(element, state);
                break;

            case ConditionalLogicConstants.Actions.ClearValue:
                ApplyClearValueAction(element, state);
                break;

            case ConditionalLogicConstants.Actions.AddValidation:
                ApplyAddValidationAction(element, state);
                break;

            case ConditionalLogicConstants.Actions.RemoveValidation:
                ApplyRemoveValidationAction(element, state);
                break;

            case ConditionalLogicConstants.Actions.ShowMessage:
                ApplyShowMessageAction(element, state, action.RuleId);
                break;

            default:
                logger.LogWarning("Unknown action '{Action}' for element '{ElementId}'",
                    element.Action, element.ElementId);
                break;
        }

        await System.Threading.Tasks.Task.CompletedTask;
    }

    private void ApplyShowAction(AffectedElement element, FormConditionalState state)
    {
        switch (element.ElementType.ToLowerInvariant())
        {
            case ConditionalLogicConstants.ElementTypes.Field:
                state.FieldVisibility[element.ElementId] = true;
                break;
            case ConditionalLogicConstants.ElementTypes.Page:
                state.PageVisibility[element.ElementId] = true;
                break;
        }
    }

    private void ApplyHideAction(AffectedElement element, FormConditionalState state)
    {
        switch (element.ElementType.ToLowerInvariant())
        {
            case ConditionalLogicConstants.ElementTypes.Field:
                state.FieldVisibility[element.ElementId] = false;
                break;
            case ConditionalLogicConstants.ElementTypes.Page:
                state.PageVisibility[element.ElementId] = false;
                break;
        }
    }

    private void ApplySkipAction(AffectedElement element, FormConditionalState state)
    {
        logger.LogInformation("[CONDITIONAL DEBUG] ApplySkipAction called - ElementType: {ElementType}, ElementId: {ElementId}", 
            element.ElementType, element.ElementId);
            
        if (element.ElementType.ToLowerInvariant() == ConditionalLogicConstants.ElementTypes.Page)
        {
            state.SkippedPages.Add(element.ElementId);
            logger.LogInformation("[CONDITIONAL DEBUG] Added page {PageId} to SkippedPages. Total skipped pages: {Count}", 
                element.ElementId, state.SkippedPages.Count);
        }
        else
        {
            logger.LogInformation("[CONDITIONAL DEBUG] Element {ElementId} is not a page (type: {ElementType}), not adding to SkippedPages", 
                element.ElementId, element.ElementType);
        }
    }

    private void ApplyRequireAction(AffectedElement element, FormConditionalState state)
    {
        if (element.ElementType.ToLowerInvariant() == ConditionalLogicConstants.ElementTypes.Field)
        {
            state.FieldRequired[element.ElementId] = true;
        }
    }

    private void ApplyMakeOptionalAction(AffectedElement element, FormConditionalState state)
    {
        if (element.ElementType.ToLowerInvariant() == ConditionalLogicConstants.ElementTypes.Field)
        {
            state.FieldRequired[element.ElementId] = false;
        }
    }

    private void ApplyEnableAction(AffectedElement element, FormConditionalState state)
    {
        if (element.ElementType.ToLowerInvariant() == ConditionalLogicConstants.ElementTypes.Field)
        {
            state.FieldEnabled[element.ElementId] = true;
        }
    }

    private void ApplyDisableAction(AffectedElement element, FormConditionalState state)
    {
        if (element.ElementType.ToLowerInvariant() == ConditionalLogicConstants.ElementTypes.Field)
        {
            state.FieldEnabled[element.ElementId] = false;
        }
    }

    private void ApplySetValueAction(AffectedElement element, FormConditionalState state)
    {
        if (element.ElementType.ToLowerInvariant() == ConditionalLogicConstants.ElementTypes.Field &&
            element.ActionConfig?.TryGetValue("value", out var value) == true)
        {
            state.FieldValues[element.ElementId] = value;
        }
    }

    private void ApplyClearValueAction(AffectedElement element, FormConditionalState state)
    {
        if (element.ElementType.ToLowerInvariant() == ConditionalLogicConstants.ElementTypes.Field)
        {
            state.FieldValues[element.ElementId] = string.Empty;
        }
    }

    private void ApplyAddValidationAction(AffectedElement element, FormConditionalState state)
    {
        if (element.ElementType.ToLowerInvariant() == ConditionalLogicConstants.ElementTypes.Field &&
            element.ActionConfig != null)
        {
            if (!state.AdditionalValidations.ContainsKey(element.ElementId))
            {
                state.AdditionalValidations[element.ElementId] = new List<ValidationRule>();
            }

            // Create validation rule from action config
            var validationRule = CreateValidationRuleFromConfig(element.ActionConfig);
            if (validationRule != null)
            {
                state.AdditionalValidations[element.ElementId].Add(validationRule);
            }
        }
    }

    private void ApplyRemoveValidationAction(AffectedElement element, FormConditionalState state)
    {
        if (element.ElementType.ToLowerInvariant() == ConditionalLogicConstants.ElementTypes.Field &&
            element.ActionConfig?.TryGetValue("validationType", out var validationType) == true)
        {
            if (state.AdditionalValidations.ContainsKey(element.ElementId))
            {
                state.AdditionalValidations[element.ElementId]
                    .RemoveAll(v => v.Type.Equals(validationType.ToString(), StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    private void ApplyShowMessageAction(AffectedElement element, FormConditionalState state, string ruleId)
    {
        if (element.ActionConfig?.TryGetValue("message", out var message) == true)
        {
            var messageType = element.ActionConfig.TryGetValue("messageType", out var type) 
                ? type.ToString() ?? "info" 
                : "info";

            state.Messages.Add(new ConditionalLogicMessage
            {
                Text = message.ToString() ?? string.Empty,
                Type = messageType,
                TargetElement = element.ElementId,
                RuleId = ruleId
            });
        }
    }

    private ValidationRule? CreateValidationRuleFromConfig(Dictionary<string, object> config)
    {
        if (!config.TryGetValue("validationType", out var validationType))
        {
            return null;
        }

        var rule = new ValidationRule
        {
            Type = validationType.ToString() ?? string.Empty,
            Message = config.TryGetValue("message", out var message) ? message.ToString() ?? string.Empty : string.Empty,
            Rule = config.TryGetValue("rule", out var ruleValue) ? ruleValue : string.Empty
        };

        return rule;
    }

    private List<Page> GetAllPages(FormTemplate template)
    {
        var pages = new List<Page>();

        if (template.TaskGroups != null)
        {
            foreach (var group in template.TaskGroups)
            {
                if (group.Tasks != null)
                {
                    foreach (var task in group.Tasks)
                    {
                        if (task.Pages != null)
                        {
                            pages.AddRange(task.Pages);
                        }
                    }
                }
            }
        }

        return pages;
    }

    private List<Field> GetAllFields(FormTemplate template)
    {
        var fields = new List<Field>();

        if (template.TaskGroups != null)
        {
            foreach (var group in template.TaskGroups)
            {
                if (group.Tasks != null)
                {
                    foreach (var task in group.Tasks)
                    {
                        if (task.Pages != null)
                        {
                            foreach (var page in task.Pages)
                            {
                                if (page.Fields != null)
                                {
                                    fields.AddRange(page.Fields);
                                }
                            }
                        }
                    }
                }
            }
        }

        return fields;
    }

    private List<Field> GetFieldsForPage(FormTemplate template, string pageId)
    {
        var fields = new List<Field>();

        if (template.TaskGroups != null)
        {
            foreach (var group in template.TaskGroups)
            {
                if (group.Tasks != null)
                {
                    foreach (var task in group.Tasks)
                    {
                        if (task.Pages != null)
                        {
                            var page = task.Pages.FirstOrDefault(p => p.PageId == pageId);
                            if (page?.Fields != null)
                            {
                                fields.AddRange(page.Fields);
                            }
                        }
                    }
                }
            }
        }

        return fields;
    }

    #endregion
}
