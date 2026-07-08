import { test as base } from '@playwright/test';
import { registerAuthentication } from '../support/authenticationInterceptor';
import { getServiceConfigFromEnv } from '../support/test-config';
import type { Terminology } from '../support/types';
import { DashboardPage } from '../pages/DashboardPage';
import { ContributorsPage } from '../pages/ContributorsPage';
import { ContributorsInvitePage } from '../pages/ContributorsInvitePage';

interface Fixtures {
  terminology: Terminology;
  dashboardPage: DashboardPage;
  contributorsPage: ContributorsPage;
  contributorsInvitePage: ContributorsInvitePage;
}

export const test = base.extend<Fixtures>({
  context: async ({ context }, use) => {
    const serviceConfig = getServiceConfigFromEnv();
    await registerAuthentication(context, serviceConfig);
    await use(context);
  },
  terminology: async ({}, use) => {
    await use(getServiceConfigFromEnv().terminology);
  },
  dashboardPage: async ({ page, terminology }, use) => {
    await use(new DashboardPage(page, terminology));
  },
  contributorsPage: async ({ page, terminology }, use) => {
    await use(new ContributorsPage(page, terminology));
  },
  contributorsInvitePage: async ({ page, terminology }, use) => {
    await use(new ContributorsInvitePage(page, terminology));
  },
});

export { expect } from '@playwright/test';
