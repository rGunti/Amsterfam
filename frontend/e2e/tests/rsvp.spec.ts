import { APIRequestContext, expect, test } from '@playwright/test';

// The browser session is fixed to `e2e-user-1` (see environment.e2e.ts). Tests
// that drive the organiser UI seed a pending joiner via the API; tests that drive
// the *attendee* UI (join/leave) instead seed the event under a different
// organiser so that e2e-user-1 is a plain user with the Join/Leave buttons.
const API = 'http://localhost:5293';
const BROWSER_USER = 'e2e-user-1';
const USER_HEADER = 'X-Test-User-ExternalId';

function asUser(externalId: string) {
  return { [USER_HEADER]: externalId, Authorization: 'Bearer e2e-fake-token' };
}

async function createOpenEvent(request: APIRequestContext, organiser: string): Promise<number> {
  const create = await request.post(`${API}/api/v1/events/`, {
    headers: asUser(organiser),
    data: {
      name: `RSVP Test ${Date.now()}`,
      description: null,
      startDate: '2030-08-01',
      endDate: '2030-08-05',
      location: 'Amsterdam',
      costPerNight: 25,
    },
  });
  expect(create.ok()).toBeTruthy();
  const eventId = (await create.json()).id as number;

  const publish = await request.post(`${API}/api/v1/events/${eventId}/publish`, {
    headers: asUser(organiser),
  });
  expect(publish.ok()).toBeTruthy();
  return eventId;
}

async function joinViaApi(request: APIRequestContext, eventId: number, user: string) {
  const join = await request.post(`${API}/api/v1/events/${eventId}/attendees/join`, {
    headers: asUser(user),
  });
  expect(join.ok()).toBeTruthy();
}

async function userId(request: APIRequestContext, user: string): Promise<number> {
  const me = await request.get(`${API}/api/v1/me`, { headers: asUser(user) });
  expect(me.ok()).toBeTruthy();
  return (await me.json()).id as number;
}

test('organiser confirms a pending attendee', async ({ page, request }) => {
  const joiner = `rsvp-confirm-${Date.now()}`;
  const eventId = await createOpenEvent(request, BROWSER_USER);
  await joinViaApi(request, eventId, joiner);

  await page.goto(`/events/${eventId}`);

  const pendingCard = page.locator('mat-card', { hasText: 'Pending attendees' });
  await expect(pendingCard.getByText(`Test User ${joiner}`)).toBeVisible();

  await pendingCard.getByRole('button', { name: 'Confirm' }).click();

  await expect(page.getByText('Attendee confirmed')).toBeVisible();

  const rosterCard = page.locator('mat-card', { hasText: "Who's coming" });
  await expect(rosterCard.getByText(`Test User ${joiner}`)).toBeVisible();
});

test('organiser removes a pending attendee', async ({ page, request }) => {
  const joiner = `rsvp-remove-${Date.now()}`;
  const eventId = await createOpenEvent(request, BROWSER_USER);
  await joinViaApi(request, eventId, joiner);

  await page.goto(`/events/${eventId}`);

  const pendingCard = page.locator('mat-card', { hasText: 'Pending attendees' });
  await expect(pendingCard.getByText(`Test User ${joiner}`)).toBeVisible();

  await pendingCard.getByRole('button', { name: 'Remove' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Remove' }).click();

  await expect(page.getByText('Attendee removed')).toBeVisible();
  // Card hides once there are no pending requests left.
  await expect(pendingCard).toBeHidden();
});

test('user joins an open event then cancels the request', async ({ page, request }) => {
  const organiser = `rsvp-org-${Date.now()}`;
  const eventId = await createOpenEvent(request, organiser);

  await page.goto(`/events/${eventId}`);

  const rsvpCard = page.locator('mat-card', { hasText: 'Your RSVP' });
  await rsvpCard.getByRole('button', { name: 'Join' }).click();

  await expect(page.getByText('Joined — waiting for confirmation')).toBeVisible();
  await expect(rsvpCard.getByText('Pending')).toBeVisible();

  await rsvpCard.getByRole('button', { name: 'Cancel request' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Leave' }).click();

  await expect(page.getByText('You left the event')).toBeVisible();
  await expect(rsvpCard.getByRole('button', { name: 'Join' })).toBeVisible();
});

test('confirmed user leaves the event (cancel keeps membership)', async ({ page, request }) => {
  const organiser = `rsvp-org-${Date.now()}`;
  const eventId = await createOpenEvent(request, organiser);
  await joinViaApi(request, eventId, BROWSER_USER);

  const id = await userId(request, BROWSER_USER);
  const confirm = await request.post(`${API}/api/v1/events/${eventId}/attendees/${id}/confirm`, {
    headers: asUser(organiser),
  });
  expect(confirm.ok()).toBeTruthy();

  await page.goto(`/events/${eventId}`);

  const rsvpCard = page.locator('mat-card', { hasText: 'Your RSVP' });
  await expect(rsvpCard.getByText("You're confirmed.")).toBeVisible();

  // Cancelling the dialog keeps the membership.
  await rsvpCard.getByRole('button', { name: 'Leave event' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click();
  await expect(rsvpCard.getByText("You're confirmed.")).toBeVisible();

  // Confirming the dialog leaves.
  await rsvpCard.getByRole('button', { name: 'Leave event' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Leave' }).click();

  await expect(page.getByText('You left the event')).toBeVisible();
  await expect(rsvpCard.getByRole('button', { name: 'Join' })).toBeVisible();
});
