import fs from 'node:fs';
import path from 'node:path';
import { resolveAspNetEnvironment } from './environment';
import type { ServiceName, Terminology } from './types';

type JsonValue = string | number | boolean | null | JsonObject | JsonValue[];
interface JsonObject {
  [key: string]: JsonValue;
}

interface InternalServiceAccount {
  Email?: string;
}

interface AppSettings {
  DfESignIn?: {
    RedirectUri?: string;
  };
  ExternalApplicationsApiClient?: {
    TenantId?: string;
  };
  InternalServiceAuth?: {
    Services?: InternalServiceAccount[];
  };
  ApplicationTerminology?: {
    Singular?: string;
    Plural?: string;
  };
}

const webConfigurationsRoot = path.resolve(__dirname, '../../../DfE.ExternalApplications.Web/configurations');

function isObject(value: JsonValue | undefined): value is JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function deepMerge(base: JsonObject, override: JsonObject): JsonObject {
  const merged: JsonObject = { ...base };

  for (const [key, value] of Object.entries(override)) {
    const existing = merged[key];

    if (isObject(existing) && isObject(value)) {
      merged[key] = deepMerge(existing, value);
      continue;
    }

    merged[key] = value;
  }

  return merged;
}

function readJsonFile(filePath: string): JsonObject {
  if (!fs.existsSync(filePath)) {
    return {};
  }

  const contents = fs.readFileSync(filePath, 'utf8');
  return JSON.parse(contents) as JsonObject;
}

function loadApplicationSettings(applicationFolder: string, aspNetEnvironment: string): AppSettings {
  const configurationDirectory = path.join(webConfigurationsRoot, applicationFolder);
  const baseSettings = readJsonFile(path.join(configurationDirectory, 'appsettings.json'));
  const environmentSettings = readJsonFile(path.join(configurationDirectory, `appsettings.${aspNetEnvironment}.json`));

  return deepMerge(baseSettings, environmentSettings) as AppSettings;
}

export function loadServiceAppSettings(
  serviceName: ServiceName,
  aspNetEnvironment = resolveAspNetEnvironment(),
): AppSettings {
  return loadApplicationSettings(serviceName, aspNetEnvironment);
}

export function resolveBaseUrl(settings: AppSettings): string {
  const redirectUri = settings.DfESignIn?.RedirectUri?.trim();

  if (!redirectUri) {
    throw new Error('DfESignIn:RedirectUri is not configured in appsettings');
  }

  return new URL(redirectUri).origin;
}

export function resolveTenantId(settings: AppSettings): string {
  const tenantId = settings.ExternalApplicationsApiClient?.TenantId?.trim();

  if (!tenantId) {
    throw new Error('ExternalApplicationsApiClient:TenantId is not configured in appsettings');
  }

  return tenantId;
}

export function resolveTerminology(settings: AppSettings): Terminology {
  const singular = settings.ApplicationTerminology?.Singular?.trim();
  const plural = settings.ApplicationTerminology?.Plural?.trim();

  if (!singular || !plural) {
    throw new Error(
      'ApplicationTerminology:Singular and ApplicationTerminology:Plural are not configured in appsettings',
    );
  }

  return { singular, plural };
}

export function resolvePlaywrightServiceEmail(settings: AppSettings, emailMarker: string): string {
  const services = settings.InternalServiceAuth?.Services;

  if (!services?.length) {
    throw new Error('InternalServiceAuth:Services is not configured in appsettings');
  }

  const service = services.find((candidate) => candidate.Email?.toLowerCase().includes(emailMarker.toLowerCase()));

  if (!service) {
    throw new Error(`No InternalServiceAuth service account containing '${emailMarker}' was found in appsettings`);
  }

  const email = service.Email?.trim();

  if (!email) {
    throw new Error(`InternalServiceAuth service account containing '${emailMarker}' is missing Email`);
  }

  return email;
}
