import { test, expect } from '../../fixtures/test';
import { login } from '../../support/login';

test.describe('LSRP initial test', () => {
  test('should login and navigate to the dashboard', async ({ page, serviceConfig }) => {
    await login(page, serviceConfig);

    await expect(page).toHaveURL(/\/applications\/dashboard/);
    await expect(page.getByRole('heading', { level: 1, name: 'Your plans' })).toBeVisible();
  });
});
