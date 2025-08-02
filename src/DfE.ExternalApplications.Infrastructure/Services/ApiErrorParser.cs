//using DfE.ExternalApplications.Application.Interfaces;
//using DfE.ExternalApplications.Domain.Models;
//using Microsoft.Extensions.Logging;
//using System.Diagnostics.CodeAnalysis;
//using System.Text.Json;
//using System.Text.RegularExpressions;

//namespace DfE.ExternalApplications.Infrastructure.Services;

//[ExcludeFromCodeCoverage]
//public class ApiErrorParser(ILogger<ApiErrorParser> logger) : IApiErrorParser
//{
//    private static readonly JsonSerializerOptions JsonOptions = new()
//    {
//        PropertyNameCaseInsensitive = true,
//        AllowTrailingCommas = true,
//        ReadCommentHandling = JsonCommentHandling.Skip
//    };

//    private static readonly Regex[] JsonExtractionPatterns = 
//    [
//        new(@"\{""type"".*?\}", RegexOptions.Singleline | RegexOptions.Compiled),
//        new(@"\{.*?""errors"".*?\}", RegexOptions.Singleline | RegexOptions.Compiled),
//        new(@"\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}", RegexOptions.Compiled)
//    ];

//    public ApiErrorParsingResult ParseApiError(Exception exception)
//    {
//        logger.LogDebug("Parsing API error from exception: {ExceptionType}", exception.GetType().Name);

//        var errorSources = GetErrorSources(exception);
        
//        foreach (var source in errorSources)
//        {
//            var jsonContent = ExtractJsonFromString(source);
//            if (jsonContent != null)
//            {
//                var parseResult = TryParseApiError(jsonContent);
//                if (parseResult != null)
//                {
//                    logger.LogDebug("Successfully parsed API error with {ErrorCount} validation errors", 
//                        parseResult.Errors?.Count ?? 0);
//                    return ApiErrorParsingResult.Success(parseResult);
//                }
//            }
//        }

//        // Fallback: try to extract specific error patterns
//        var fallbackErrors = ExtractKnownErrorPatterns(exception.ToString());
//        if (fallbackErrors.Count > 0)
//        {
//            var fallbackResponse = new ApiErrorResponse
//            {
//                Errors = fallbackErrors,
//                Title = "Validation errors occurred",
//                Status = 400
//            };
            
//            logger.LogDebug("Used fallback error extraction, found {ErrorCount} errors", fallbackErrors.Count);
//            return ApiErrorParsingResult.Success(fallbackResponse);
//        }

//        logger.LogDebug("Could not parse structured API error, returning raw message");
//        return ApiErrorParsingResult.Failure(exception.Message);
//    }

//    private static IEnumerable<string> GetErrorSources(Exception exception)
//    {
//        yield return exception.Message;
        
//        if (exception.InnerException != null)
//            yield return exception.InnerException.Message;
        
//        yield return exception.ToString();
//    }

//    private string? ExtractJsonFromString(string input)
//    {
//        if (string.IsNullOrWhiteSpace(input))
//            return null;

//        // Try regex patterns first
//        foreach (var pattern in JsonExtractionPatterns)
//        {
//            var match = pattern.Match(input);
//            if (match.Success && IsValidJson(match.Value))
//            {
//                return match.Value;
//            }
//        }

//        // Fallback to brace counting
//        return ExtractJsonByBraceMatching(input);
//    }

//    private static string? ExtractJsonByBraceMatching(string input)
//    {
//        var startIndex = input.IndexOf('{');
//        if (startIndex == -1) return null;

//        var braceCount = 0;
//        for (int i = startIndex; i < input.Length; i++)
//        {
//            if (input[i] == '{') braceCount++;
//            else if (input[i] == '}') braceCount--;

//            if (braceCount == 0)
//            {
//                var candidate = input.Substring(startIndex, i - startIndex + 1);
//                return IsValidJson(candidate) ? candidate : null;
//            }
//        }

//        return null;
//    }

//    private ApiErrorResponse? TryParseApiError(string jsonContent)
//    {
//        try
//        {
//            return JsonSerializer.Deserialize<ApiErrorResponse>(jsonContent, JsonOptions);
//        }
//        catch (JsonException ex)
//        {
//            logger.LogDebug(ex, "Failed to deserialize JSON as ApiErrorResponse");
//            return null;
//        }
//    }

//    private static bool IsValidJson(string content)
//    {
//        try
//        {
//            JsonSerializer.Deserialize<object>(content);
//            return true;
//        }
//        catch
//        {
//            return false;
//        }
//    }

//    private static Dictionary<string, string[]> ExtractKnownErrorPatterns(string exceptionText)
//    {
//        var errors = new Dictionary<string, string[]>();

//        // Pattern for VersionNumber errors
//        if (exceptionText.Contains("VersionNumber", StringComparison.OrdinalIgnoreCase))
//        {
//            var versionMatch = Regex.Match(exceptionText, 
//                @"VersionNumber["":\[\]]*([^""}\]]+)", 
//                RegexOptions.IgnoreCase);
            
//            if (versionMatch.Success)
//            {
//                var errorMessage = versionMatch.Groups[1].Value.Trim();
//                if (!string.IsNullOrEmpty(errorMessage))
//                {
//                    errors["VersionNumber"] = [errorMessage];
//                }
//            }
//            else if (exceptionText.Contains("Invalid Template Version format", StringComparison.OrdinalIgnoreCase))
//            {
//                errors["VersionNumber"] = ["Invalid Template Version format."];
//            }
//        }

//        // Pattern for JsonSchema errors
//        if (exceptionText.Contains("JsonSchema", StringComparison.OrdinalIgnoreCase))
//        {
//            var schemaMatch = Regex.Match(exceptionText, 
//                @"JsonSchema["":\[\]]*([^""}\]]+)", 
//                RegexOptions.IgnoreCase);
            
//            if (schemaMatch.Success)
//            {
//                var errorMessage = schemaMatch.Groups[1].Value.Trim();
//                if (!string.IsNullOrEmpty(errorMessage))
//                {
//                    errors["JsonSchema"] = [errorMessage];
//                }
//            }
//        }

//        return errors;
//    }
//} 