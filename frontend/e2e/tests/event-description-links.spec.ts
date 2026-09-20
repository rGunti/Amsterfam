import { expect, test } from '@playwright/test';

// The browser session is fixed to `e2e-user-1` (see environment.e2e.ts).
const API = 'http://localhost:5293';
const BROWSER_USER = 'e2e-user-1';
const USER_HEADER = 'X-Test-User-ExternalId';

const LONG_URL = 'https://www.example.com/stay/amsterdam-farm-lodge/rooms?dates=2031-07-01';

test('links in the description are shortened, warn on click and open freely otherwise', async ({
  page,
  context,
  request,
}) => {
  const create = await request.post(`${API}/api/v1/events/`, {
    headers: { [USER_HEADER]: BROWSER_USER, Authorization: 'Bearer e2e-fake-token' },
    data: {
      name: `Links Test ${Date.now()}`,
      description: `Where we stay: ${LONG_URL}, see you there!\n<b>not bold</b> javascript:alert(1)`,
      startDate: '2031-07-01',
      endDate: '2031-07-08',
      location: 'Amsterdam',
    },
  });
  expect(create.ok()).toBeTruthy();
  const eventId = (await create.json()).id as string;

  // Keep the test offline: whatever the link opens gets a stub page.
  await context.route('https://www.example.com/**', (route) =>
    route.fulfill({ contentType: 'text/html', body: '<title>stub</title>' }),
  );

  await page.goto(`/events/${eventId}`);

  const link = page.locator('app-linkified-text a');
  await expect(link).toHaveCount(1);
  // Shortened label, full URL on the anchor, external-link icon at the end.
  await expect(link).toContainText('example.com/stay/amsterdam-farm-lodge/r…');
  await expect(link).not.toContainText('https://');
  await expect(link).toHaveAttribute('href', LONG_URL);
  await expect(link).toHaveAttribute('target', '_blank');
  await expect(link.locator('mat-icon')).toHaveText('open_in_new');
  // The comma after the URL is text, not part of the link, and markup stays inert.
  await expect(page.locator('app-linkified-text')).toContainText(', see you there!');
  await expect(page.locator('app-linkified-text b')).toHaveCount(0);

  // A plain click asks first; cancelling opens nothing.
  let opened = 0;
  context.on('page', () => opened++);
  await link.click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toContainText('Open external link?');
  await expect(dialog).toContainText('www.example.com');
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  await expect(dialog).toBeHidden();
  expect(opened).toBe(0);

  // Confirming opens the link in a new tab.
  await link.click();
  const popupPromise = page.waitForEvent('popup');
  await page
    .getByRole('dialog')
    .getByRole('link', { name: /Open link/ })
    .click();
  const popup = await popupPromise;
  await popup.waitForLoadState();
  expect(popup.url()).toBe(LONG_URL);
  await expect(page.getByRole('dialog')).toBeHidden();
  await popup.close();

  // A middle-click is already a deliberate "open in a new tab": no dialog.
  // A background tab, so wait for the context's new page rather than a popup.
  const middlePopup = context.waitForEvent('page');
  await link.click({ button: 'middle' });
  await (await middlePopup).close();
  await expect(page.getByRole('dialog')).toBeHidden();
});
