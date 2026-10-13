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
      name: `News ${Date.now()}`,
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

test('an attendee sees the unread badge, reads the post, and finds it in the timeline', async ({
  page,
  request,
}) => {
  const organiser = `e2e-news-organiser-${Date.now()}`;
  const eventId = await createOpenEvent(request, organiser);
  await joinViaLink(request, eventId, organiser, BROWSER_USER);
  await confirm(request, eventId, organiser, BROWSER_USER);

  const body = `Please bring **towels**. ${'More details follow here. '.repeat(20)}`;
  const post = await request.post(`${API}/api/v1/events/${eventId}/news`, {
    headers: asUser(organiser),
    data: { title: 'Packing list', body },
  });
  expect(post.ok()).toBeTruthy();

  await page.goto(`/events/${eventId}`);
  const newsLink = page.getByRole('link', { name: /^News/ });
  await expect(newsLink.locator('.nav-badge')).toHaveText('1');

  await newsLink.click();
  await expect(page).toHaveURL(new RegExp(`/events/${eventId}/news$`));
  await expect(page.getByRole('heading', { name: 'Packing list' })).toBeVisible();
  await expect(page.locator('.post-body strong')).toHaveText('towels');
  await expect(page.getByText('New', { exact: true })).toBeVisible();
  await expect(newsLink.locator('.nav-badge')).toHaveCount(0);

  await page.getByRole('link', { name: 'Timeline' }).click();
  const preview = page.locator('.news-preview');
  await expect(preview).toContainText('Packing list');
  await expect(preview).toContainText('Please bring towels.');
  await preview.getByText('Read more').click();
  await expect(page).toHaveURL(new RegExp(`/events/${eventId}/news/\\d+$`));
  await expect(page.getByRole('heading', { name: 'Packing list' })).toBeVisible();
});

test('an organiser writes, edits and deletes a post without losing a draft', async ({
  page,
  request,
}) => {
  const eventId = await createOpenEvent(request, BROWSER_USER);

  await page.goto(`/events/${eventId}/news`);
  await expect(page.getByText('No news yet.')).toBeVisible();
  await page.getByRole('link', { name: 'New post' }).click();
  await expect(page).toHaveURL(new RegExp(`/events/${eventId}/news/new$`));

  await page.getByLabel('Title (optional)').fill('Arrival');
  await page.getByLabel('Post', { exact: true }).fill('We meet at **Centraal** at 3pm.');

  // Leaving asks first; keeping on editing keeps the text.
  await page.getByRole('link', { name: 'Timeline' }).click();
  await expect(page.getByText('Discard changes?')).toBeVisible();
  await page.getByRole('button', { name: 'Keep editing' }).click();
  await expect(page).toHaveURL(new RegExp(`/events/${eventId}/news/new$`));
  await expect(page.getByLabel('Post', { exact: true })).toHaveValue(
    'We meet at **Centraal** at 3pm.',
  );

  await page.getByRole('button', { name: 'Publish' }).click();
  await expect(page).toHaveURL(new RegExp(`/events/${eventId}/news/\\d+$`));
  await expect(page.locator('.post-body strong')).toHaveText('Centraal');

  await page.getByRole('button', { name: 'Post actions' }).click();
  await page.getByRole('menuitem', { name: 'Edit' }).click();
  await expect(page.getByLabel('Post', { exact: true })).toHaveValue(
    'We meet at **Centraal** at 3pm.',
  );
  await page.getByLabel('Post', { exact: true }).fill('We meet at **Centraal** at 4pm.');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.locator('.post-body')).toContainText('4pm');
  await expect(page.getByText('edited', { exact: true })).toBeVisible();

  await page.getByRole('button', { name: 'Post actions' }).click();
  await page.getByRole('menuitem', { name: 'Delete' }).click();
  await page.getByRole('button', { name: 'Delete' }).click();
  await expect(page).toHaveURL(new RegExp(`/events/${eventId}/news$`));
  await expect(page.getByText('No news yet.')).toBeVisible();
});

test('an attendee acknowledges a pinned post from the event overview', async ({
  page,
  request,
}) => {
  const organiser = `e2e-news-ack-organiser-${Date.now()}`;
  const eventId = await createOpenEvent(request, organiser);
  await joinViaLink(request, eventId, organiser, BROWSER_USER);
  await confirm(request, eventId, organiser, BROWSER_USER);

  const created = await request.post(`${API}/api/v1/events/${eventId}/news`, {
    headers: asUser(organiser),
    data: { title: 'Deposit due', body: 'Pay **€50** by Friday.', requiresAck: true, pin: true },
  });
  expect(created.ok()).toBeTruthy();
  const postId = (await created.json()).id as number;

  await page.goto(`/events/${eventId}`);
  const card = page.locator('app-news-summary-card');
  await expect(card.getByText('Latest news')).toBeVisible();
  await expect(card.getByRole('link', { name: 'Deposit due' })).toBeVisible();
  await expect(card.getByLabel('Pinned')).toBeVisible();

  await card.getByRole('button', { name: 'Got it' }).click();
  await expect(card.getByText("You've acknowledged this")).toBeVisible();
  await expect(card.getByRole('button', { name: 'Got it' })).toHaveCount(0);

  const acks = await request.get(`${API}/api/v1/events/${eventId}/news/${postId}/acks`, {
    headers: asUser(organiser),
  });
  expect(acks.ok()).toBeTruthy();
  const body = await acks.json();
  expect(body.acknowledged).toHaveLength(1);
  expect(body.missing).toHaveLength(0); // the organiser wrote it, so isn't asked

  // Reactions work on the feed page.
  await page.getByRole('link', { name: 'All news' }).click();
  await page.getByRole('button', { name: /^🎉/ }).click();
  await expect(page.getByRole('button', { name: '🎉 1' })).toHaveAttribute('aria-pressed', 'true');
});

test('an organiser schedules a post, then sends it out early', async ({ page, request }) => {
  const eventId = await createOpenEvent(request, BROWSER_USER);
  const attendee = `e2e-news-scheduled-attendee-${Date.now()}`;
  await joinViaLink(request, eventId, BROWSER_USER, attendee);
  await confirm(request, eventId, BROWSER_USER, attendee);

  await page.goto(`/events/${eventId}/news/new`);
  await page.getByLabel('Title (optional)').fill('Boat trip');
  await page.getByLabel('Post', { exact: true }).fill('Meet at the **pier**.');
  await page.getByRole('radio', { name: 'Later' }).click();
  // Tomorrow, 10:00 local time.
  const at = new Date();
  at.setDate(at.getDate() + 1);
  const pad = (n: number) => String(n).padStart(2, '0');
  await page
    .getByLabel('Goes out at')
    .fill(`${at.getFullYear()}-${pad(at.getMonth() + 1)}-${pad(at.getDate())}T10:00`);
  await page.getByRole('button', { name: 'Schedule' }).click();

  await expect(page).toHaveURL(new RegExp(`/events/${eventId}/news$`));
  const scheduled = page.locator('section.scheduled');
  await expect(scheduled.getByRole('heading', { name: 'Boat trip' })).toBeVisible();
  await expect(scheduled.locator('.schedule-chip')).toContainText('Scheduled for');
  await expect(page.getByText('No news yet.')).toBeVisible();

  // Members don't see it yet.
  const feed = await request.get(`${API}/api/v1/events/${eventId}/news`, {
    headers: asUser(attendee),
  });
  expect((await feed.json()).posts).toHaveLength(0);

  await scheduled.getByRole('button', { name: 'Post actions' }).click();
  await page.getByRole('menuitem', { name: 'Edit' }).click();
  await page.getByRole('radio', { name: 'Now' }).click();
  await page.getByRole('button', { name: 'Publish now' }).click();

  await expect(page).toHaveURL(new RegExp(`/events/${eventId}/news/\\d+$`));
  await expect(page.locator('.post-body strong')).toHaveText('pier');
  await expect(page.locator('.schedule-chip')).toHaveCount(0);
  const after = await request.get(`${API}/api/v1/events/${eventId}/news`, {
    headers: asUser(attendee),
  });
  expect((await after.json()).posts).toHaveLength(1);
});

// 1x1 PNG.
const PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
  'base64',
);

test('an organiser posts with an image, sees it in the timeline, then removes it', async ({
  page,
  request,
}) => {
  const eventId = await createOpenEvent(request, BROWSER_USER);

  await page.goto(`/events/${eventId}/news/new`);
  await page.getByLabel('Title (optional)').fill('Beach day');
  await page.getByLabel('Post').fill('Bring sunscreen.');
  await page.locator('input[type=file]').setInputFiles({
    name: 'beach.png',
    mimeType: 'image/png',
    buffer: PNG,
  });
  await expect(page.getByRole('button', { name: 'Change image' })).toBeVisible();
  await page.getByRole('button', { name: 'Publish' }).click();

  await expect(page).toHaveURL(new RegExp(`/events/${eventId}/news/\\d+$`));
  await expect(page.locator('.post-image img')).toHaveAttribute('src', /^blob:/);
  // The first image isn't an edit.
  await expect(page.getByText('edited', { exact: true })).toHaveCount(0);

  await page.getByRole('link', { name: 'Timeline' }).click();
  const preview = page.locator('.news-preview');
  await expect(preview.locator('.news-preview-image img')).toHaveAttribute('src', /^blob:/);
  await preview.locator('.news-preview-image').click();
  await expect(page).toHaveURL(new RegExp(`/events/${eventId}/news/\\d+$`));

  await page.getByRole('button', { name: 'Post actions' }).click();
  await page.getByRole('menuitem', { name: 'Edit' }).click();
  await page.getByRole('button', { name: 'Remove image' }).click();
  await expect(page.getByRole('button', { name: 'Add image' })).toBeVisible();
  await page.getByRole('button', { name: 'Save' }).click();

  await expect(page).toHaveURL(new RegExp(`/events/${eventId}/news/\\d+$`));
  await expect(page.getByRole('heading', { name: 'Beach day' })).toBeVisible();
  await expect(page.locator('.post-image')).toHaveCount(0);
});

test('an organiser sees a new comment on a post and removes it', async ({ page, request }) => {
  const eventId = await createOpenEvent(request, BROWSER_USER);
  const attendee = `e2e-news-commenter-${Date.now()}`;
  await joinViaLink(request, eventId, BROWSER_USER, attendee);
  await confirm(request, eventId, BROWSER_USER, attendee);

  const post = await request.post(`${API}/api/v1/events/${eventId}/news`, {
    headers: asUser(BROWSER_USER),
    data: { title: 'Deposit due', body: 'Pay by Friday.' },
  });
  expect(post.ok()).toBeTruthy();
  const postId = (await post.json()).id as number;
  const comment = await request.post(`${API}/api/v1/events/${eventId}/news/${postId}/comments`, {
    headers: asUser(attendee),
    data: { body: 'Which Friday exactly?' },
  });
  expect(comment.ok()).toBeTruthy();

  await page.goto(`/events/${eventId}/news`);
  const toggle = page.locator('.comments-toggle');
  await expect(toggle).toContainText('1 comment');
  await expect(toggle).toContainText('1 new');

  await toggle.click();
  await expect(page.locator('.comment')).toContainText('Which Friday exactly?');
  await expect(toggle).not.toContainText('new');

  await page.getByRole('button', { name: 'Comment actions' }).click();
  await page.getByRole('menuitem', { name: 'Delete' }).click();
  await page.getByRole('button', { name: 'Delete' }).click();
  await expect(page.locator('.comment')).toHaveCount(0);
  await expect(toggle).toContainText('Comment');

  await page.getByRole('link', { name: 'Timeline' }).click();
  await expect(page.getByText(/You removed a comment by .+ on “Deposit due”/)).toBeVisible();
  await expect(page.getByText('Which Friday exactly?')).toHaveCount(0);
});

test('an attendee subscribes via RSS, a reader fetches the feed, then the link is revoked', async ({
  page,
  request,
}) => {
  const organiser = `e2e-news-organiser-${Date.now()}`;
  const eventId = await createOpenEvent(request, organiser);
  await joinViaLink(request, eventId, organiser, BROWSER_USER);
  await confirm(request, eventId, organiser, BROWSER_USER);
  const post = await request.post(`${API}/api/v1/events/${eventId}/news`, {
    headers: asUser(organiser),
    data: { title: 'Ferry times', body: 'The ferry leaves at **noon**.' },
  });
  expect(post.ok()).toBeTruthy();

  await page.goto(`/events/${eventId}/news`);
  await page.getByRole('button', { name: 'Subscribe via RSS' }).click();
  await page.getByRole('button', { name: 'Create feed link' }).click();
  const url = (await page.locator('app-news-feed-sheet .url').textContent())!.trim();

  // A feed reader has no credentials; the token in the URL is all it needs.
  const feedPath = new URL(url).pathname;
  const feed = await request.get(`${API}${feedPath}`);
  expect(feed.status()).toBe(200);
  expect(feed.headers()['content-type']).toContain('application/atom+xml');
  expect(await feed.text()).toContain('Ferry times');

  await page.getByRole('button', { name: 'Revoke' }).click();
  await page.locator('mat-dialog-container').getByRole('button', { name: 'Revoke' }).click();
  await expect(page.getByRole('button', { name: 'Create feed link' })).toBeVisible();
  expect((await request.get(`${API}${feedPath}`)).status()).toBe(404);
});

test('an organiser connects Discord without the webhook URL ever showing again', async ({
  page,
  request,
}) => {
  const eventId = await createOpenEvent(request, BROWSER_USER);
  await page.goto(`/events/${eventId}`);
  await expect(page.locator('app-discord-webhook-card')).toHaveCount(0);
  await page.getByRole('link', { name: 'Notifications' }).click();
  await expect(page).toHaveURL(new RegExp(`/events/${eventId}/notifications$`));

  const card = page.locator('app-discord-webhook-card');
  const url = card.getByLabel('Webhook URL');
  await url.fill('https://example.com/api/webhooks/1/abc');
  await card.getByRole('button', { name: 'Connect' }).click();
  await expect(card).toContainText("That isn't a Discord webhook URL.");

  await url.fill('https://discord.com/api/webhooks/123/e2e-secret-wxyz');
  await card.getByRole('button', { name: 'Connect' }).click();
  await expect(card).toContainText('ending in “wxyz”');

  await page.reload();
  await expect(card).toContainText('ending in “wxyz”');
  await expect(page.locator('body')).not.toContainText('e2e-secret');

  await card.getByRole('button', { name: 'Disconnect' }).click();
  await expect(card.getByLabel('Webhook URL')).toBeVisible();
});
