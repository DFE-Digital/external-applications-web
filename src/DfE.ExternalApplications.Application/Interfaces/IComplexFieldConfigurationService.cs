using DfE.ExternalApplications.Domain.Models;

namespace DfE.ExternalApplications.Application.Interfaces
{
    public interface IComplexFieldConfigurationService
    {
        ComplexFieldConfiguration GetConfiguration(string complexFieldId);
        bool HasConfiguration(string complexFieldId);
    }
} 