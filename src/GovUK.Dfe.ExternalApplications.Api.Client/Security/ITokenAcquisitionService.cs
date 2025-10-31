using System.Threading.Tasks;

namespace GovUK.Dfe.ExternalApplications.Api.Client.Security
{
    public interface ITokenAcquisitionService
    {
        Task<string> GetTokenAsync();
    }
}
