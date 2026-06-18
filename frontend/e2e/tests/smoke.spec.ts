import { expect, test } from '@playwright/test';

test('loads the home page as an authenticated user', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByText("You're not part of any trips yet.")).toBeVisible();
});

test('navigates to the profile page', async ({ page }) => {
  await page.goto('/');

  await page.getByRole('link', { name: 'Profile' }).click();

  await expect(page).toHaveURL(/\/profile$/);
});
