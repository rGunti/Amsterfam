import { expect, test } from '@playwright/test';

// The browser session is fixed to `e2e-user-1` (see environment.e2e.ts).
const API = 'http://localhost:5293';
const BROWSER_USER = 'e2e-user-1';
const USER_HEADER = 'X-Test-User-ExternalId';

function asUser(externalId: string) {
  return { [USER_HEADER]: externalId, Authorization: 'Bearer e2e-fake-token' };
}

// 1x1 PNG.
const PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
  'base64',
);

test('organiser adds and removes a banner on the event overview', async ({ page, request }) => {
  const create = await request.post(`${API}/api/v1/events/`, {
    headers: asUser(BROWSER_USER),
    data: {
      name: `Banner Test ${Date.now()}`,
      description: null,
      startDate: '2031-07-01',
      endDate: '2031-07-08',
      location: 'Amsterdam',
    },
  });
  expect(create.ok()).toBeTruthy();
  const eventId = (await create.json()).id as string;
  const open = await request.post(`${API}/api/v1/events/${eventId}/status`, {
    headers: asUser(BROWSER_USER),
    data: { target: 'Open' },
  });
  expect(open.ok()).toBeTruthy();

  await page.goto(`/events/${eventId}`);
  await expect(page.locator('app-event-banner')).toHaveCount(0);

  await page.getByRole('button', { name: 'Edit' }).click();
  await page.locator('input[type=file]').setInputFiles({
    name: 'banner.png',
    mimeType: 'image/png',
    buffer: PNG,
  });
  await expect(page.getByRole('button', { name: 'Change banner' })).toBeVisible();
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(page.locator('app-event-banner img')).toHaveAttribute('src', /^blob:/);

  // Survives a reload, i.e. it really was stored.
  await page.reload();
  await expect(page.locator('app-event-banner img')).toHaveAttribute('src', /^blob:/);

  // The join-link preview advertises it to non-members.
  const link = await request.post(`${API}/api/v1/events/${eventId}/join-links/`, {
    headers: asUser(BROWSER_USER),
    data: { kind: 'Attendee', expiresAt: null, maxUses: null },
  });
  const token = (await link.json()).token as string;
  const preview = await request.get(`${API}/api/v1/join-links/${token}`, {
    headers: asUser('e2e-banner-stranger'),
  });
  expect((await preview.json()).bannerFileId).toBeTruthy();

  await page.getByRole('button', { name: 'Edit' }).click();
  await page.getByRole('button', { name: 'Remove banner' }).click();
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(page.locator('app-event-banner')).toHaveCount(0);
});

test('a visitor opening a join link sees the event banner', async ({ page, request }) => {
  const organiser = 'e2e-banner-organiser';
  const create = await request.post(`${API}/api/v1/events/`, {
    headers: asUser(organiser),
    data: {
      name: `Banner Join ${Date.now()}`,
      description: null,
      startDate: '2031-07-01',
      endDate: '2031-07-08',
      location: 'Amsterdam',
    },
  });
  const eventId = (await create.json()).id as string;
  await request.post(`${API}/api/v1/events/${eventId}/status`, {
    headers: asUser(organiser),
    data: { target: 'Open' },
  });
  const upload = await request.put(`${API}/api/v1/events/${eventId}/banner`, {
    headers: { ...asUser(organiser), 'Content-Type': 'application/octet-stream' },
    data: PNG,
  });
  expect(upload.ok()).toBeTruthy();

  const link = await request.post(`${API}/api/v1/events/${eventId}/join-links/`, {
    headers: asUser(organiser),
    data: { kind: 'Attendee', expiresAt: null, maxUses: null },
  });
  const token = (await link.json()).token as string;

  await page.goto(`/join/${token}`);
  await expect(page.locator('app-event-banner img')).toHaveAttribute('src', /^blob:/);
  await expect(page.locator('app-event-banner')).toContainText("You're invited to Banner Join");
});
