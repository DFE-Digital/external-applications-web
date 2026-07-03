import { test as base } from '@playwright/test';
import { registerAuthentication } from '../support/authenticationInterceptor';
import { getServiceConfigFromEnv } from '../support/test-config';

export const test = base.extend({
  context: async ({ context }, use) => {
    const serviceConfig = getServiceConfigFromEnv();
    await registerAuthentication(context, serviceConfig);
    await use(context);
  },
});

export { expect } from '@playwright/test';
