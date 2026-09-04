import { APIRequestContext, expect, test } from '@playwright/test';

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
      name: `Payment Methods Test ${Date.now()}`,
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

async function joinAndConfirm(
  request: APIRequestContext,
  eventId: number,
  organiser: string,
  user: string,
) {
  const join = await request.post(`${API}/api/v1/events/${eventId}/attendees/join`, {
    headers: asUser(user),
  });
  expect(join.ok()).toBeTruthy();

  const me = await request.get(`${API}/api/v1/me`, { headers: asUser(user) });
  const userId = (await me.json()).id as number;

  const confirm = await request.post(
    `${API}/api/v1/events/${eventId}/attendees/${userId}/confirm`,
    { headers: asUser(organiser) },
  );
  expect(confirm.ok()).toBeTruthy();
}

test.describe('payment methods', () => {
  test('user adds, edits, and removes a payment method on their profile', async ({ page }) => {
    await page.goto('/profile');

    const card = page.locator('mat-card', { hasText: 'Payment methods' });
    const title = `Wise ${Date.now()}`;

    await card.getByRole('button', { name: 'Add payment method' }).click();
    const addDialog = page.getByRole('dialog');
    await addDialog.getByLabel('Title').fill(title);
    await addDialog.getByLabel('Payment link (optional)').fill('https://wise.com/pay/e2e');
    await addDialog.getByRole('button', { name: 'Save' }).click();

    await expect(page.getByText('Payment method added')).toBeVisible();
    const row = card.locator('.method-row', { hasText: title });
    await expect(row).toBeVisible();

    await row.getByRole('button', { name: 'Edit' }).click();
    const editDialog = page.getByRole('dialog');
    await editDialog.getByLabel('Description (optional)').fill('prefer friends & family');
    await editDialog.getByRole('button', { name: 'Save' }).click();

    await expect(page.getByText('Payment method updated')).toBeVisible();
    await expect(row.getByText('prefer friends & family')).toBeVisible();

    await row.getByRole('button', { name: 'Remove' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Remove' }).click();
    await expect(page.getByText('Payment method removed')).toBeVisible();
    await expect(row).toBeHidden();
  });

  test('attendee roster shows another confirmed user’s payment methods', async ({
    page,
    request,
  }) => {
    const organiser = `pm-org-${Date.now()}`;
    const other = `pm-other-${Date.now()}`;
    const eventId = await createOpenEvent(request, organiser);
    await joinAndConfirm(request, eventId, organiser, other);
    await joinAndConfirm(request, eventId, organiser, BROWSER_USER);

    const create = await request.post(`${API}/api/v1/me/payment-methods/`, {
      headers: asUser(other),
      data: { title: 'PayPal', icon: 'paypal', link: 'https://paypal.me/e2e', description: null },
    });
    expect(create.ok()).toBeTruthy();

    await page.goto(`/events/${eventId}`);

    const rosterCard = page.locator('mat-card', { hasText: "Who's coming" });
    const otherRow = rosterCard.locator('mat-list-item', { hasText: `Test User ${other}` });
    await otherRow.getByRole('button', { name: 'View payment methods' }).click();

    const dialog = page.getByRole('dialog');
    await expect(dialog.getByText('PayPal')).toBeVisible();

    await dialog.getByRole('button', { name: 'Close' }).click();
  });
});
