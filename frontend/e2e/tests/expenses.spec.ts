import { APIRequestContext, expect, test } from '@playwright/test';

import { joinViaLink } from './join-helper';

// The browser session is fixed to `e2e-user-1` (see environment.e2e.ts).
const API = 'http://localhost:5293';
const BROWSER_USER = 'e2e-user-1';
const USER_HEADER = 'X-Test-User-ExternalId';

function asUser(externalId: string) {
  return { [USER_HEADER]: externalId, Authorization: 'Bearer e2e-fake-token' };
}

async function createOpenEvent(request: APIRequestContext, owner: string): Promise<string> {
  const create = await request.post(`${API}/api/v1/events`, {
    headers: asUser(owner),
    data: {
      name: `Expenses ${Date.now()}`,
      description: null,
      startDate: '2030-07-01',
      endDate: '2030-07-08',
      location: 'Amsterdam',
      currency: 'EUR',
    },
  });
  expect(create.ok()).toBeTruthy();
  const id = (await create.json()).id as string;
  const open = await request.post(`${API}/api/v1/events/${id}/status`, {
    headers: asUser(owner),
    data: { target: 'Open' },
  });
  expect(open.ok()).toBeTruthy();
  return id;
}

async function confirm(
  request: APIRequestContext,
  eventId: string,
  organiser: string,
  user: string,
) {
  const me = await request.get(`${API}/api/v1/me`, { headers: asUser(user) });
  const userId = (await me.json()).id as number;
  const res = await request.post(`${API}/api/v1/events/${eventId}/attendees/${userId}/confirm`, {
    headers: asUser(organiser),
  });
  expect(res.ok()).toBeTruthy();
}

test('add an equal-split expense and settle it up', async ({ page, request }) => {
  const eventId = await createOpenEvent(request, BROWSER_USER);
  const friend = `e2e-expenses-friend-${Date.now()}`;
  await joinViaLink(request, eventId, BROWSER_USER, friend);
  await confirm(request, eventId, BROWSER_USER, friend);

  await page.goto(`/events/${eventId}`);
  await page.getByRole('link', { name: 'Expenses' }).click();
  await expect(page).toHaveURL(new RegExp(`/events/${eventId}/expenses$`));
  await expect(page.getByText("You're all settled up.")).toBeVisible();

  await page.getByRole('button', { name: 'Add expense' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Title').fill('Groceries');
  await dialog.getByLabel('Amount (EUR)').fill('30');
  // Everyone is included by default, so each share is half.
  await expect(dialog.getByText('€15.00')).toHaveCount(2);
  await dialog.getByRole('button', { name: 'Save' }).click();

  await expect(page.getByText('Groceries')).toBeVisible();
  await expect(page.getByText("You're owed €15.00 in total.")).toBeVisible();

  await page.getByRole('button', { name: 'Mark as paid' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Record' }).click();

  await expect(page.getByText("You're all settled up.")).toBeVisible();
  await expect(page.locator('.row').filter({ hasText: 'paid you' })).toContainText('€15.00');
});
