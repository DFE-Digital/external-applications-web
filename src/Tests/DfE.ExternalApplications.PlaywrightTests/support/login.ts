import type { Page } from '@playwright/test';

export async function login(page: Page): Promise<void> {
  await page.context().clearCookies();
  await page.context().clearPermissions();
  await page.goto('/');
  await page.waitForURL(/\/applications\/dashboard/);
}
