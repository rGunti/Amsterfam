import { APIRequestContext, Page, expect, test } from '@playwright/test';

// The browser session is fixed to `e2e-user-1` (see environment.e2e.ts).
const API = 'http://localhost:5293';
const BROWSER_USER = 'e2e-user-1';
const USER_HEADER = 'X-Test-User-ExternalId';

function asUser(externalId: string) {
  return { [USER_HEADER]: externalId, Authorization: 'Bearer e2e-fake-token' };
}

async function createEvent(request: APIRequestContext, owner: string, name: string) {
  const create = await request.post(`${API}/api/v1/events/`, {
    headers: asUser(owner),
    data: {
      name,
      description: null,
      startDate: '2031-07-01',
      endDate: '2031-07-08',
      location: 'Amsterdam',
      costPerNight: 25,
    },
  });
  expect(create.ok()).toBeTruthy();
  return (await create.json()).id as string;
}

async function setStatus(
  request: APIRequestContext,
  owner: string,
  eventId: string,
  target: string,
) {
  const response = await request.post(`${API}/api/v1/events/${eventId}/status`, {
    headers: asUser(owner),
    data: { target },
  });
  expect(response.ok()).toBeTruthy();
}

async function userId(request: APIRequestContext, user: string): Promise<number> {
  const me = await request.get(`${API}/api/v1/me`, { headers: asUser(user) });
  expect(me.ok()).toBeTruthy();
  return (await me.json()).id as number;
}

function statusChip(page: Page) {
  return page.locator('mat-card-title mat-chip');
}

async function confirmDialog(page: Page, button: string) {
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: button, exact: true }).click();
  await expect(dialog).toBeHidden();
}

test('owner walks an event through its full lifecycle and deletes it', async ({
  page,
  request,
}) => {
  const eventId = await createEvent(request, BROWSER_USER, `Lifecycle ${Date.now()}`);
  await page.goto(`/events/${eventId}`);

  await page.getByRole('button', { name: 'Open event' }).click();
  await expect(statusChip(page)).toHaveText('Open');

  await page.getByRole('button', { name: 'Start now' }).click();
  await expect(page.getByRole('dialog')).toContainText("can't be moved back");
  await confirmDialog(page, 'Start');
  await expect(statusChip(page)).toHaveText('In progress');

  await page.getByRole('button', { name: 'Close event' }).click();
  await confirmDialog(page, 'Close');
  await expect(statusChip(page)).toHaveText('Closed');

  await page.getByRole('button', { name: 'Archive' }).click();
  await confirmDialog(page, 'Archive');
  await expect(statusChip(page)).toHaveText('Archived');

  // Archived events are read-only.
  await expect(page.getByRole('button', { name: 'Edit' })).toHaveCount(0);

  const dangerZone = page.locator('mat-card', { hasText: 'Danger Zone' });
  await dangerZone.getByRole('button', { name: 'Delete event' }).click();
  await confirmDialog(page, 'Delete');
  await expect(page).toHaveURL('/');
});

test('owner can reset a started event back to open from the Danger Zone', async ({
  page,
  request,
}) => {
  const eventId = await createEvent(request, BROWSER_USER, `Reset ${Date.now()}`);
  await setStatus(request, BROWSER_USER, eventId, 'Open');
  await setStatus(request, BROWSER_USER, eventId, 'InProgress');
  await page.goto(`/events/${eventId}`);

  const dangerZone = page.locator('mat-card', { hasText: 'Danger Zone' });
  await dangerZone.getByRole('button', { name: 'Reset to Open' }).click();
  await confirmDialog(page, 'Reset');

  await expect(statusChip(page)).toHaveText('Open');
  await expect(page.getByText('Automatic start/close is paused')).toBeVisible();

  await setStatus(request, BROWSER_USER, eventId, 'Cancelled');
  await request.delete(`${API}/api/v1/events/${eventId}`, { headers: asUser(BROWSER_USER) });
});

test('owner cancels an event and still sees it', async ({ page, request }) => {
  const eventId = await createEvent(request, BROWSER_USER, `Cancel Owner ${Date.now()}`);
  await page.goto(`/events/${eventId}`);

  const dangerZone = page.locator('mat-card', { hasText: 'Danger Zone' });
  await dangerZone.getByRole('button', { name: 'Cancel event' }).click();
  await expect(page.getByRole('dialog').getByRole('button', { name: 'Keep event' })).toBeVisible();
  await confirmDialog(page, 'Cancel event');

  await expect(statusChip(page)).toHaveText('Cancelled');
  await expect(dangerZone.getByRole('button', { name: 'Delete event' })).toBeVisible();

  await request.delete(`${API}/api/v1/events/${eventId}`, { headers: asUser(BROWSER_USER) });
});

test('attendees of a cancelled event get a dead-end page', async ({ page, request }) => {
  const organiser = `lifecycle-org-${Date.now()}`;
  const eventId = await createEvent(request, organiser, `Cancelled ${Date.now()}`);
  await setStatus(request, organiser, eventId, 'LookingForDate');

  const join = await request.post(`${API}/api/v1/events/${eventId}/attendees/join`, {
    headers: asUser(BROWSER_USER),
  });
  expect(join.ok()).toBeTruthy();
  const browserUserId = await userId(request, BROWSER_USER);
  const confirm = await request.post(
    `${API}/api/v1/events/${eventId}/attendees/${browserUserId}/confirm`,
    { headers: asUser(organiser) },
  );
  expect(confirm.ok()).toBeTruthy();
  await setStatus(request, organiser, eventId, 'Cancelled');

  await page.goto(`/events/${eventId}`);

  await expect(page).toHaveURL(new RegExp(`/events/${eventId}/cancelled$`));
  await expect(page.getByText('This event was cancelled')).toBeVisible();
  await expect(page.locator('.event-switcher')).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Overview' })).toHaveCount(0);

  // Sub-pages are covered too.
  await page.goto(`/events/${eventId}/find-a-date`);
  await expect(page).toHaveURL(new RegExp(`/events/${eventId}/cancelled$`));

  await page.getByRole('link', { name: 'Back to events' }).click();
  await expect(page).toHaveURL('/');

  await request.delete(`${API}/api/v1/events/${eventId}`, { headers: asUser(organiser) });
});

test('unknown routes show a not-found page', async ({ page }) => {
  await page.goto('/this-does-not-exist');

  await expect(page.getByText('Page not found')).toBeVisible();
  await page.getByRole('link', { name: 'Back to events' }).click();
  await expect(page).toHaveURL('/');
});
