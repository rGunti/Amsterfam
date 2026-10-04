import { APIRequestContext, expect, test } from '@playwright/test';

import { joinViaLink } from './join-helper';

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
      name: `Profile Fields Test ${Date.now()}`,
      description: null,
      startDate: '2030-08-01',
      endDate: '2030-08-05',
      location: 'Amsterdam',
    },
  });
  expect(create.ok()).toBeTruthy();
  const eventId = (await create.json()).id as number;

  const open = await request.post(`${API}/api/v1/events/${eventId}/status`, {
    headers: asUser(organiser),
    data: { target: 'Open' },
  });
  expect(open.ok()).toBeTruthy();
  return eventId;
}

async function joinAndConfirm(
  request: APIRequestContext,
  eventId: number,
  organiser: string,
  user: string,
) {
  await joinViaLink(request, eventId, organiser, user);

  const me = await request.get(`${API}/api/v1/me`, { headers: asUser(user) });
  const userId = (await me.json()).id as number;

  const confirm = await request.post(
    `${API}/api/v1/events/${eventId}/attendees/${userId}/confirm`,
    { headers: asUser(organiser) },
  );
  expect(confirm.ok()).toBeTruthy();
}

test.describe('profile fields', () => {
  test('user fills in their About me card', async ({ page }) => {
    const bio = `Here for the canals ${Date.now()}`;
    await page.goto('/profile');

    const card = page.locator('mat-card', { hasText: 'About me' });
    await card.getByRole('button', { name: 'Edit about me' }).click();

    await card.getByRole('button', { name: 'they/them' }).click();
    await expect(card.getByLabel('Pronouns')).toHaveValue('they/them');

    await card.getByLabel('Location').fill('Zürich');

    await card.getByLabel('Day').click();
    await page.getByRole('option', { name: '14', exact: true }).click();
    await card.getByLabel('Month').click();
    await page.getByRole('option', { name: 'July' }).click();

    await card.getByRole('checkbox', { name: 'Vegetarian' }).check();
    await card.getByRole('checkbox', { name: 'Nut allergy', exact: true }).check();
    await card.getByLabel('Anything else?').fill('No coriander please');
    await card.getByLabel('About me').fill(bio);

    await card.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText('Profile updated')).toBeVisible();

    await page.reload();
    const saved = page.locator('mat-card', { hasText: 'About me' });
    await expect(saved.getByText('they/them')).toBeVisible();
    await expect(saved.getByText('Zürich')).toBeVisible();
    await expect(saved.getByText('14 July')).toBeVisible();
    await expect(saved.getByText('Vegetarian')).toBeVisible();
    await expect(saved.getByText('Nut allergy', { exact: true })).toBeVisible();
    await expect(saved.getByText('No coriander please')).toBeVisible();
    await expect(saved.getByText(bio)).toBeVisible();
  });

  test('attendee roster links to a fellow member’s profile', async ({ page, request }) => {
    const organiser = `pf-org-${Date.now()}`;
    const other = `pf-other-${Date.now()}`;
    const eventId = await createOpenEvent(request, organiser);
    await joinAndConfirm(request, eventId, organiser, other);
    await joinAndConfirm(request, eventId, organiser, BROWSER_USER);

    const update = await request.put(`${API}/api/v1/me`, {
      headers: asUser(other),
      data: {
        displayName: null,
        avatarUrl: null,
        pronouns: 'she/her',
        location: 'Amsterdam',
        bio: null,
        birthday: { month: 2, day: 29, year: null },
        dietaryOptionIds: [2],
        dietaryNotes: null,
      },
    });
    expect(update.ok()).toBeTruthy();

    await page.goto(`/events/${eventId}`);

    const rosterCard = page.locator('mat-card', { hasText: "Who's coming" });
    await rosterCard.getByRole('button', { name: `Test User ${other}` }).click();
    await page.getByRole('menuitem', { name: 'View profile' }).click();

    await expect(page).toHaveURL(/\/users\/\d+$/);
    await expect(page.getByText(`@Test User ${other}`)).toBeVisible();
    const about = page.locator('mat-card', { hasText: 'About' }).last();
    await expect(about.getByText('29 February')).toBeVisible();
    await expect(about.getByText('Vegan')).toBeVisible();
    await expect(page.getByText(`${other}@test.example`)).toBeHidden();
  });
});
