import type { ServiceConfig, ServiceName } from './types';

export function requireEnv(name: string): string {
  const value = process.env[name]?.trim();

  if (!value) {
    throw new Error(`${name} environment variable is required`);
  }

  return value;
}

function normalizeUrl(url: string): string {
  return url.replace(/\/$/, '');
}

function createServiceConfig(
  name: ServiceName,
  urlEnv: string,
  usernameEnv: string,
  apiKeyEnv: string,
  tenantIdEnv: string,
): ServiceConfig {
  return {
    name,
    url: normalizeUrl(requireEnv(urlEnv)),
    username: requireEnv(usernameEnv),
    apiKey: requireEnv(apiKeyEnv),
    tenantId: requireEnv(tenantIdEnv),
  };
}

export function getServiceConfigs(): ServiceConfig[] {
  return [
    createServiceConfig('transfers', 'TRANSFERS_URL', 'TRANSFERS_USERNAME', 'TRANSFERS_API_KEY', 'TRANSFERS_TENANT_ID'),
    createServiceConfig('lsrp', 'LSRP_URL', 'LSRP_USERNAME', 'LSRP_API_KEY', 'LSRP_TENANT_ID'),
    createServiceConfig('visits', 'VISITS_URL', 'VISITS_USERNAME', 'VISITS_API_KEY', 'VISITS_TENANT_ID'),
  ];
}

export function getServiceConfig(name: ServiceName): ServiceConfig {
  const config = getServiceConfigs().find((service) => service.name === name);

  if (!config) {
    throw new Error(`Unknown service: ${name}`);
  }

  return config;
}
