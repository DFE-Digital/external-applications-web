import { test as base } from '@playwright/test';
import { registerAuthentication } from '../support/authenticationInterceptor';
import { getServiceConfig } from '../support/services';
import type { ServiceConfig, ServiceName } from '../support/types';

export const test = base.extend<{ serviceConfig: ServiceConfig }>({
  serviceConfig: async ({}, use, testInfo) => {
    await use(getServiceConfig(testInfo.project.name as ServiceName));
  },

  context: async ({ context, serviceConfig }, use) => {
    await registerAuthentication(context, serviceConfig);
    await use(context);
  },
});

export { expect } from '@playwright/test';
