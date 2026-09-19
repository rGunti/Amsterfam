import { APIRequestContext, expect, test } from '@playwright/test';

// The browser session is fixed to `e2e-user-1` (see environment.e2e.ts).
const API = 'http://localhost:5293';
const BROWSER_USER = 'e2e-user-1';
const USER_HEADER = 'X-Test-User-ExternalId';

function asUser(externalId: string) {
  return { [USER_HEADER]: externalId, Authorization: 'Bearer e2e-fake-token' };
}

async function createEvent(request: APIRequestContext, name: string): Promise<string> {
  const create = await request.post(`${API}/api/v1/events`, {
    headers: asUser(BROWSER_USER),
    data: {
      name,
      description: null,
      startDate: null,
      endDate: null,
      location: 'Amsterdam',
      costPerNight: 25,
    },
  });
  expect(create.ok()).toBeTruthy();
  return (await create.json()).id as string;
}

test('root path shows the reduced nav with no event switcher', async ({ page }) => {
  await page.goto('/');

  await expect(page.locator('.event-switcher')).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Events' })).toBeVisible();
});

test('opening an event shows the toolbar switcher and event-scoped nav', async ({
  page,
  request,
}) => {
  const eventId = await createEvent(request, `Nav Test ${Date.now()}`);

  await page.goto(`/events/${eventId}`);

  await expect(page.locator('.event-switcher')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Overview' })).toBeVisible();
  // New events are drafts and the browser user owns them, so the poll page is offered.
  await expect(page.getByRole('link', { name: 'Find a date' })).toBeVisible();
});

test('switching events via the toolbar dropdown navigates to the selected event', async ({
  page,
  request,
}) => {
  const nameA = `Nav Switch A ${Date.now()}`;
  const nameB = `Nav Switch B ${Date.now()}`;
  const eventA = await createEvent(request, nameA);
  const eventB = await createEvent(request, nameB);

  await page.goto(`/events/${eventA}`);
  await page.locator('.event-switcher').click();
  await page.getByRole('menuitem', { name: nameB }).click();

  await expect(page).toHaveURL(new RegExp(`/events/${eventB}$`));
  await expect(page.locator('.event-switcher')).toContainText(nameB);
});

test('current event is pinned and disabled at the top of the dropdown', async ({
  page,
  request,
}) => {
  const nameA = `Nav Pin A ${Date.now()}`;
  const nameB = `Nav Pin B ${Date.now()}`;
  const eventA = await createEvent(request, nameA);
  await createEvent(request, nameB);

  await page.goto(`/events/${eventA}`);
  await page.locator('.event-switcher').click();

  const firstItem = page.locator('.mat-mdc-menu-panel .mat-mdc-menu-item').first();
  await expect(firstItem).toContainText(nameA);
  await expect(firstItem).toBeDisabled();
});

test('navigating to a non-existent event redirects home with a snackbar', async ({ page }) => {
  await page.goto('/events/00000000-0000-0000-0000-000000000000');

  await expect(page).toHaveURL('/');
  await expect(page.getByText('Event not found')).toBeVisible();
});

test('"All events" returns to the reduced nav', async ({ page, request }) => {
  const eventId = await createEvent(request, `Nav Collapse ${Date.now()}`);

  await page.goto(`/events/${eventId}`);
  await expect(page.locator('.event-switcher')).toBeVisible();

  await page.locator('.event-switcher').click();
  await page.getByRole('menuitem', { name: 'All events' }).click();

  await expect(page).toHaveURL('/');
  await expect(page.locator('.event-switcher')).toHaveCount(0);
});

test('logout button on the profile page requires confirmation', async ({ page }) => {
  await page.goto('/profile');

  await page.getByRole('button', { name: 'Log out' }).click();
  await expect(page.getByRole('dialog')).toContainText('Are you sure you want to log out?');

  await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click();
  await expect(page.getByRole('dialog')).toBeHidden();
  // Cancelling keeps the session — still on the profile page, not logged out.
  await expect(page).toHaveURL(/\/profile$/);
});
