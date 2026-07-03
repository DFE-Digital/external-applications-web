import type { ServiceName } from './types';

export const applications: readonly ServiceName[] = ['Transfers', 'Lsrp', 'RGVisits'];

const aspNetEnvironmentNames: Record<string, string> = {
  development: 'Development',
  test: 'Test',
  production: 'Production',
};

const aspNetEnvironmentValues = new Set(Object.values(aspNetEnvironmentNames));

export const playwrightServiceEmailMarker = 'eat-cypress';

export function requireService(): ServiceName {
  const service = process.env.SERVICE?.trim();

  if (!service || !applications.includes(service as ServiceName)) {
    throw new Error(`SERVICE environment variable is required (one of: ${applications.join(', ')})`);
  }

  return service as ServiceName;
}

export function toGitHubEnvironmentPrefix(application: ServiceName): string {
  return application.toLowerCase();
}

export function resolveAspNetEnvironment(): string {
  const environment = process.env.ENVIRONMENT?.trim();

  if (!environment) {
    return 'Development';
  }

  if (aspNetEnvironmentValues.has(environment)) {
    return environment;
  }

  const service = requireService();
  const prefix = `${toGitHubEnvironmentPrefix(service)}-`;
  const normalizedEnvironment = environment.toLowerCase();

  if (!normalizedEnvironment.startsWith(prefix)) {
    throw new Error(
      `ENVIRONMENT '${environment}' does not match SERVICE '${service}' (expected prefix '${prefix}')`,
    );
  }

  const suffix = normalizedEnvironment.slice(prefix.length);
  const aspNetEnvironment = aspNetEnvironmentNames[suffix];

  if (!aspNetEnvironment) {
    throw new Error(
      `Unsupported environment suffix '${suffix}' in GitHub environment '${environment}'`,
    );
  }

  return aspNetEnvironment;
}
