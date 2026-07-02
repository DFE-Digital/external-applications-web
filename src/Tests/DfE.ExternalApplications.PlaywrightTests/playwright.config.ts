import './support/load-env';
import { defineConfig, devices } from '@playwright/test';
import { getServiceConfigs } from './support/services';

const serviceConfigs = getServiceConfigs();
const zapProxy = process.env.ZAP_PROXY;

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : [['html', { open: 'never' }], ['list']],
  use: {
    trace: 'on-first-retry',
    ignoreHTTPSErrors: true,
    ...(zapProxy
      ? {
          proxy: { server: zapProxy },
        }
      : {}),
  },
  projects: serviceConfigs.map((service) => ({
    name: service.name,
    testMatch: new RegExp(`${service.name}/.*\\.spec\\.ts`),
    use: {
      ...devices['Desktop Chrome'],
      baseURL: service.url,
    },
  })),
});
