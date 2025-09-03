using System;

namespace GovUK.Dfe.ExternalApplications.Api.Client.Security
{
    public interface IInternalUserTokenStore
    {
        /// <summary>
        /// Gets the cached internal/OBO token
        /// </summary>
        string? GetToken();
        
        /// <summary>
        /// Sets and caches the internal/OBO token
        /// </summary>
        void SetToken(string token);
        
        /// <summary>
        /// Clears the cached internal/OBO token
        /// </summary>
        void ClearToken();
        
        /// <summary>
        /// Checks if the cached internal/OBO token is valid (not expired within buffer time)
        /// </summary>
        bool IsTokenValid();
        
        /// <summary>
        /// Gets the expiry time of the cached internal/OBO token
        /// </summary>
        DateTime? GetTokenExpiry();
    }
}
