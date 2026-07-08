import { playwrightServiceEmailMarker, requireService, resolveAspNetEnvironment } from './environment';
import { loadServiceAppSettings, resolveBaseUrl, resolvePlaywrightServiceEmail, resolveTenantId } from './app-settings';
import type { ServiceConfig, ServiceName } from './types';

function normalizeUrl(url: string): string {
  return url.replace(/\/$/, '');
}

function requireApiKey(): string {
  const explicitApiKey = process.env.SERVICE_API_KEY?.trim();
  if (explicitApiKey) {
    return explicitApiKey;
  }

  throw new Error('SERVICE_API_KEY is required. Set it in .env locally or as a GitHub environment secret in CI.');
}

export function createServiceConfig(serviceName: ServiceName): ServiceConfig {
  const aspNetEnvironment = resolveAspNetEnvironment();
  const settings = loadServiceAppSettings(serviceName, aspNetEnvironment);
  const serviceEmail = resolvePlaywrightServiceEmail(settings, playwrightServiceEmailMarker);

  return {
    name: serviceName,
    url: normalizeUrl(resolveBaseUrl(settings)),
    username: serviceEmail,
    apiKey: requireApiKey(),
    tenantId: resolveTenantId(settings),
  };
}

export function getServiceConfigFromEnv(): ServiceConfig {
  return createServiceConfig(requireService());
}
