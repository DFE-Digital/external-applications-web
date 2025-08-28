namespace DfE.ExternalApplications.Application.Interfaces
{
    public interface IComplexFieldRendererFactory
    {
        IComplexFieldRenderer GetRenderer(string fieldType);
    }
} 