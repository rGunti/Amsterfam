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
      name: `Timeline ${Date.now()}`,
      description: null,
      startDate: '2030-07-01',
      endDate: '2030-07-08',
      location: 'Amsterdam',
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

test('organisers see the full timeline, including organiser-only entries', async ({
  page,
  request,
}) => {
  const eventId = await createOpenEvent(request, BROWSER_USER);
  const joiner = `e2e-timeline-joiner-${Date.now()}`;
  await joinViaLink(request, eventId, BROWSER_USER, joiner);

  await page.goto(`/events/${eventId}`);
  await page.getByRole('link', { name: 'Timeline' }).click();

  await expect(page).toHaveURL(new RegExp(`/events/${eventId}/timeline$`));
  const entries = page.locator('.entry');
  await expect(entries).toHaveCount(4);
  await expect(entries.nth(0)).toContainText('asked to join via “Attendee link”');
  await expect(entries.nth(0).getByLabel('Only organisers see this')).toBeVisible();
  await expect(entries.nth(1)).toContainText('You created the join link “Attendee link”');
  await expect(entries.nth(2)).toContainText('You moved the event to Open');
  await expect(entries.nth(3)).toContainText('You created the event');
});

test('attendees only see public entries', async ({ page, request }) => {
  const owner = `e2e-timeline-owner-${Date.now()}`;
  const eventId = await createOpenEvent(request, owner);
  await joinViaLink(request, eventId, owner, BROWSER_USER);
  await confirm(request, eventId, owner, BROWSER_USER);

  await page.goto(`/events/${eventId}/timeline`);

  const entries = page.locator('.entry');
  await expect(entries).toHaveCount(3);
  await expect(entries.nth(0)).toContainText('confirmed you');
  await expect(page.getByText('asked to join')).toHaveCount(0);
  await expect(page.getByText('join link')).toHaveCount(0);
});
