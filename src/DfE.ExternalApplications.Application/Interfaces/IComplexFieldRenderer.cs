using DfE.ExternalApplications.Domain.Models;

namespace DfE.ExternalApplications.Application.Interfaces
{
    public interface IComplexFieldRenderer
    {
        string FieldType { get; }
        string Render(ComplexFieldConfiguration configuration, string complexFieldId, string currentValue, string errorMessage, string label, string tooltip, bool isRequired);
    }
} 