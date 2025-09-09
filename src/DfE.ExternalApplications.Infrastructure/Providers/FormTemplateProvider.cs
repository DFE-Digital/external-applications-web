﻿using GovUK.Dfe.CoreLibs.Caching.Helpers;
using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using System.Diagnostics.CodeAnalysis;

namespace DfE.ExternalApplications.Infrastructure.Providers;

public class FormTemplateProvider(
    ITemplateStore store, 
    IFormTemplateParser parser,
    ICacheService<IMemoryCacheType> cacheService) : IFormTemplateProvider
{
    [ExcludeFromCodeCoverage]
    public async Task<FormTemplate> GetTemplateAsync(string templateId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"FormTemplate_{CacheKeyHelper.GenerateHashedCacheKey(templateId)}";
        var methodName = nameof(GetTemplateAsync);

        return await cacheService.GetOrAddAsync(
            cacheKey,
            async () =>
            {
                using var stream = await store.GetTemplateStreamAsync(templateId, cancellationToken);
                return await parser.ParseAsync(stream, cancellationToken);
            },
            methodName);
    }
}