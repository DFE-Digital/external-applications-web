# Complex Field Pattern

This document explains the new Complex Field pattern implemented in the External Applications Web project.

## Overview

The Complex Field pattern allows you to create reusable, configurable field components that can be easily managed through configuration rather than hardcoded values. This follows SOLID principles and provides better separation of concerns.

## Architecture

### Domain Layer
- `ComplexFieldConfiguration` - Model representing the configuration for a complex field

### Application Layer
- `IComplexFieldConfigurationService` - Interface for retrieving complex field configurations
- `IComplexFieldRenderer` - Interface for complex field renderers
- `IComplexFieldRendererFactory` - Interface for the renderer factory

### Infrastructure Layer
- `ComplexFieldConfigurationService` - Implementation that reads configuration from appsettings
- `ComplexFieldRendererFactory` - Factory for managing different complex field renderers
- `AutocompleteComplexFieldRenderer` - Renderer for autocomplete complex fields
- `CompositeComplexFieldRenderer` - Renderer for composite complex fields

### Web Layer
- `_ComplexField.cshtml` - Reusable view component for complex fields
- Updated `AutocompleteService` - Enhanced to work with complex field configurations
- Updated `FieldRendererService` - Now supports "complexField" type

## Configuration

Complex field configurations are stored in `appsettings.json`:

```json
{
  "FormEngine": {
    "ComplexFields": {
      "TrustComplexField": {
        "FieldType": "autocomplete",
        "ApiEndpoint": "https://api.dev.academies.education.gov.uk/trusts?page=1&count=10&groupname={0}&ukprn={0}&companieshousenumber={0}",
        "ApiKey": "",
        "Label": "Trust",
        "AllowMultiple": false,
        "MinLength": 3,
        "Placeholder": "Start typing to search for trusts...",
        "MaxSelections": 0
      },
      "EstablishmentComplexField": {
        "FieldType": "autocomplete",
        "ApiEndpoint": "https://api.dev.academies.education.gov.uk/establishments?page=1&count=10&name={0}&urn={0}&ukprn={0}",
        "ApiKey": "",
        "Label": "Establishment",
        "AllowMultiple": false,
        "MinLength": 3,
        "Placeholder": "Start typing to search for establishments...",
        "MaxSelections": 0
      }
    }
  }
}
```

### Configuration Properties

- `FieldType` - The type of complex field (autocomplete, composite, etc.)
- `ApiEndpoint` - The API endpoint for the complex field
- `ApiKey` - Optional API key for authentication
- `Label` - The label for the field
- `AllowMultiple` - Whether multiple selections are allowed
- `MinLength` - Minimum characters required before searching
- `Placeholder` - Placeholder text for the input field
- `MaxSelections` - Maximum number of selections (0 = no limit)
- `AdditionalProperties` - Field-specific configuration properties

## JSON Template Usage

To use a complex field in your JSON template, set the field type to "complexField" and provide the complex field ID:

```json
{
  "fieldId": "trustSearch",
  "type": "complexField",
  "label": {
    "value": "Trust",
    "isPageHeading": false
  },
  "placeholder": "Start typing to search for trusts...",
  "tooltip": "Type at least 3 characters to search for trusts by name, UKPRN, or Companies House number",
  "required": true,
  "order": 6,
  "visibility": {
    "default": true
  },
  "validations": [
    {
      "type": "required",
      "message": "Please select a trust"
    }
  ],
  "complexField": {
    "id": "TrustComplexField"
  }
}
```

## Benefits

1. **Configuration-Driven**: API endpoints and settings are managed in appsettings, not hardcoded
2. **Reusable**: The same complex field can be used across multiple templates
3. **Maintainable**: Changes to API endpoints only require configuration updates
4. **Secure**: API keys and sensitive configuration are properly managed
5. **SOLID Compliant**: Follows Single Responsibility Principle and Dependency Inversion
6. **Backward Compatible**: Existing autocomplete fields continue to work

## Migration from Legacy Autocomplete

### Before (Legacy)
```json
{
  "type": "autocomplete",
  "complexField": "{\"properties\":{\"endpoint\":\"https://api.dev.academies.education.gov.uk/trusts?page=1&count=10&groupname={0}&ukprn={0}&companieshousenumber={0}\",\"allowMultiple\":false,\"minLength\":3,\"placeholder\":\"Start typing to search for trusts...\"}}"
}
```

### After (Complex Field)
```json
{
  "type": "complexField",
  "complexField": {
    "id": "TrustComplexField"
  }
}
```

## Adding New Complex Fields

1. **Add Configuration**: Add the complex field configuration to `appsettings.json`
2. **Create Template**: Use the "complexField" type in your JSON template
3. **Register Service**: The `ComplexFieldConfigurationService` is already registered in DI

## Adding New Complex Field Types

1. **Create Renderer**: Implement `IComplexFieldRenderer` for your new field type
2. **Register Renderer**: Add the renderer to DI in `Program.cs`
3. **Update Configuration**: Set the `FieldType` in your configuration
4. **Test**: Verify the new field type works correctly

### Example: Adding a New Autocomplete Field

```csharp
public class MyCustomComplexFieldRenderer : IComplexFieldRenderer
{
    public string FieldType => "myCustomType";
    
    public string Render(ComplexFieldConfiguration configuration, string fieldId, string currentValue, string errorMessage, string label, string tooltip, bool isRequired)
    {
        // Custom rendering logic here
        return "<div>Custom field content</div>";
    }
}
```

## API Handler

The complex field API calls are handled by the `OnGetComplexFieldAsync` method in `RenderForm.cshtml.cs`. This method:

1. Receives the complex field ID and query
2. Calls the `AutocompleteService` with the complex field ID
3. Returns JSON results for the frontend

## Frontend Integration

The `_ComplexField.cshtml` view:

1. Reads the complex field ID from the template
2. Retrieves configuration from the service
3. Renders the appropriate UI based on configuration
4. Handles both single and multiple selection modes
5. Provides accessible autocomplete functionality

## Testing

To test the complex field pattern:

1. Ensure the configuration is in `appsettings.json`
2. Use the example template provided
3. Navigate to the form page
4. Type in the complex field to trigger the autocomplete functionality

## Future Enhancements

The complex field pattern can be extended to support:

- Different field types (not just autocomplete)
- Custom validation rules
- Field-specific styling
- Advanced configuration options
- Caching strategies
- Rate limiting 