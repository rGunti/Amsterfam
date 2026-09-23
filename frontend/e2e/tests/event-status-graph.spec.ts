import { APIRequestContext, Page, expect, test } from '@playwright/test';

import { joinViaLink } from './join-helper';

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

function graph(page: Page) {
  return page.getByRole('list', { name: 'Event progress' });
}

test('organisers see how far the event has come, skipped stages included', async ({
  page,
  request,
}) => {
  const eventId = await createEvent(request, BROWSER_USER, `Graph ${Date.now()}`);
  // Draft → Open skips Looking for date.
  await setStatus(request, BROWSER_USER, eventId, 'Open');
  await page.goto(`/events/${eventId}`);

  const stages = graph(page).getByRole('listitem');
  await expect(stages).toHaveCount(6);
  await expect(stages.nth(0)).toHaveAccessibleName('Draft: done');
  await expect(stages.nth(1)).toHaveAccessibleName('Looking for date: done');
  await expect(stages.nth(2)).toHaveAccessibleName('Open: current stage');
  await expect(stages.nth(2)).toHaveAttribute('aria-current', 'step');
  await expect(stages.nth(5)).toHaveAccessibleName('Archived: upcoming');

  // Moving on updates the graph in place.
  await page.getByRole('button', { name: 'Start now' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Start', exact: true }).click();
  await expect(stages.nth(3)).toHaveAccessibleName('In progress: current stage');

  await setStatus(request, BROWSER_USER, eventId, 'Cancelled');
  await request.delete(`${API}/api/v1/events/${eventId}`, { headers: asUser(BROWSER_USER) });
});

test('a cancelled event greys out the graph and ends in Cancelled', async ({ page, request }) => {
  const eventId = await createEvent(request, BROWSER_USER, `Graph Cancel ${Date.now()}`);
  await setStatus(request, BROWSER_USER, eventId, 'Open');
  await setStatus(request, BROWSER_USER, eventId, 'Cancelled');
  await page.goto(`/events/${eventId}`);

  const stages = graph(page).getByRole('listitem');
  await expect(stages).toHaveCount(6);
  await expect(stages.nth(2)).toHaveAccessibleName('Open: not reached');
  await expect(stages.nth(5)).toHaveAccessibleName('Cancelled: current stage');
  await expect(graph(page)).not.toContainText('Archived');

  await request.delete(`${API}/api/v1/events/${eventId}`, { headers: asUser(BROWSER_USER) });
});

test('attendees do not see the graph', async ({ page, request }) => {
  const organiser = `graph-org-${Date.now()}`;
  const eventId = await createEvent(request, organiser, `Graph Attendee ${Date.now()}`);
  await setStatus(request, organiser, eventId, 'Open');
  await joinViaLink(request, eventId, organiser, BROWSER_USER);

  await page.goto(`/events/${eventId}`);
  await expect(page.locator('mat-card-title mat-chip')).toHaveText('Open');
  await expect(graph(page)).toHaveCount(0);

  await setStatus(request, organiser, eventId, 'Cancelled');
  await request.delete(`${API}/api/v1/events/${eventId}`, { headers: asUser(organiser) });
});
