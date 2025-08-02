//using DfE.ExternalApplications.Application.Interfaces;
//using DfE.ExternalApplications.Domain.Models;
//using Microsoft.AspNetCore.Mvc.ModelBinding;
//using Microsoft.Extensions.Logging;
//using System.Diagnostics.CodeAnalysis;

//namespace DfE.ExternalApplications.Infrastructure.Services;

//[ExcludeFromCodeCoverage]
//public class ModelStateErrorHandler(ILogger<ModelStateErrorHandler> logger) : IModelStateErrorHandler
//{
//    public void AddApiErrorsToModelState(ModelStateDictionary modelState, ApiErrorResponse apiError, 
//        Dictionary<string, string>? fieldMappings = null)
//    {
//        if (!apiError.HasValidationErrors)
//        {
//            logger.LogDebug("No validation errors found in API error response");
//            return;
//        }

//        foreach (var errorField in apiError.Errors!)
//        {
//            var fieldName = errorField.Key;
//            var fieldErrors = errorField.Value;

//            // Map API field names to model property names
//            var modelFieldName = GetMappedFieldName(fieldName, fieldMappings);

//            foreach (var errorMessage in fieldErrors)
//            {
//                if (string.IsNullOrEmpty(modelFieldName))
//                {
//                    // Add as general error with field context
//                    modelState.AddModelError(string.Empty, $"{fieldName}: {errorMessage}");
//                    logger.LogDebug("Added general error for unmapped field {FieldName}: {ErrorMessage}", 
//                        fieldName, errorMessage);
//                }
//                else
//                {
//                    // Add as field-specific error
//                    modelState.AddModelError(modelFieldName, errorMessage);
//                    logger.LogDebug("Added field error for {ModelField} (API field: {ApiField}): {ErrorMessage}", 
//                        modelFieldName, fieldName, errorMessage);
//                }
//            }
//        }
//    }

//    public void AddGeneralError(ModelStateDictionary modelState, string errorMessage)
//    {
//        modelState.AddModelError(string.Empty, errorMessage);
//        logger.LogDebug("Added general error: {ErrorMessage}", errorMessage);
//    }

//    private static string? GetMappedFieldName(string apiFieldName, Dictionary<string, string>? fieldMappings)
//    {
//        if (fieldMappings?.TryGetValue(apiFieldName, out var mappedName) == true)
//        {
//            return mappedName;
//        }

//        // Default mappings for common API field names
//        return apiFieldName switch
//        {
//            "VersionNumber" => "NewVersion",
//            "JsonSchema" => "NewSchema",
//            _ => null // Unmapped fields will be added as general errors
//        };
//    }
//} 