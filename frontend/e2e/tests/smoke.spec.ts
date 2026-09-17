import { expect, test } from '@playwright/test';

test('loads the home page as an authenticated user', async ({ page }) => {
  await page.goto('/');

  await expect(page.locator('a.toolbar-avatar')).toBeVisible();
});

test('navigates to the profile page', async ({ page }) => {
  await page.goto('/');

  await page.locator('a.toolbar-avatar').click();

  await expect(page).toHaveURL(/\/profile$/);
});
