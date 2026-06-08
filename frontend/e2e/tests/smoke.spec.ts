import { expect, test } from '@playwright/test';

test('loads the home page as an authenticated user', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByText(/Welcome, /)).toBeVisible();
  await expect(page.getByText('This is Amsterfam')).toBeVisible();
});

test('navigates to the profile page', async ({ page }) => {
  await page.goto('/');

  await page.getByRole('link', { name: 'View profile' }).click();

  await expect(page).toHaveURL(/\/profile$/);
});
