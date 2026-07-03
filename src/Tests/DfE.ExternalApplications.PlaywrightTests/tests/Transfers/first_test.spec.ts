import { test, expect } from '../../fixtures/test';
import { login } from '../../support/login';

test.describe('Transfers initial test', () => {
  test('should login and navigate to the dashboard', async ({ page }) => {
    await login(page);

    await expect(page).toHaveURL(/\/applications\/dashboard/);
    await expect(page.getByRole('heading', { level: 1, name: 'Your applications' })).toBeVisible();
  });
});
