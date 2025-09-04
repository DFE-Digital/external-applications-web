using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Distributed;

namespace DfE.ExternalApplications.Web.Security;

/// <summary>
/// Stores cookie authentication tickets server-side to keep the browser cookie small.
/// AuthenticationProperties (including tokens) live in distributed cache and not in the browser cookie.
/// </summary>
public sealed class DistributedCacheTicketStore : ITicketStore
{
    private readonly IDistributedCache _cache;
    private const string KeyPrefix = "auth_ticket_";

    public DistributedCacheTicketStore(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = KeyPrefix + Guid.NewGuid().ToString("N");
        await RenewAsync(key, ticket);
        return key;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var data = TicketSerializer.Default.Serialize(ticket);
        var expires = ticket.Properties?.ExpiresUtc ?? DateTimeOffset.UtcNow.AddHours(8);
        await _cache.SetAsync(key, data, new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = expires
        });
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var data = await _cache.GetAsync(key);
        if (data == null)
        {
            return null;
        }
        return TicketSerializer.Default.Deserialize(data);
    }

    public Task RemoveAsync(string key)
    {
        return _cache.RemoveAsync(key);
    }
}


