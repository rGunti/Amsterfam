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
