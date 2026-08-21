import { test } from '../../fixtures/test';
import { login } from '../../support/login';

const contributor = {
  name: 'Playwright Test',
  email: 'playwright@test.com',
};

test.describe('Visits smoke', () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
  });

  test('should add a contributor', async ({ dashboardPage, contributorsPage, contributorsInvitePage }) => {
    await dashboardPage.startNewApplication();
    await contributorsPage.addContributor();

    await contributorsInvitePage.fillInvite(contributor.name, contributor.email);
    await contributorsInvitePage.sendInvite();

    await contributorsPage.expectContributor(2, contributor.name, contributor.email);
  });
});
