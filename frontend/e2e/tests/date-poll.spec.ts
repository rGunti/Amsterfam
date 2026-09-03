import { APIRequestContext, Locator, expect, test } from '@playwright/test';

// Must match Amsterfam.Api.Auth.TestAuthHandler.UserIdHeader and
// environment.e2e.ts's fakeAuthExternalId — the E2E frontend always
// authenticates as this single fixed user, so it's both organiser and
// (once confirmed) the only attendee of any event it creates.
const API = 'http://localhost:5293';
const TEST_USER_HEADER = 'X-Test-User-ExternalId';
const TEST_USER = 'e2e-user-1';

async function createDraftEvent(request: APIRequestContext, suffix: string) {
  const response = await request.post(`${API}/api/v1/events/`, {
    headers: { [TEST_USER_HEADER]: TEST_USER },
    data: {
      name: `Date Poll E2E ${suffix}`,
      description: null,
      startDate: '2031-07-01',
      endDate: '2031-07-08',
      location: 'Amsterdam',
      costPerNight: 35,
    },
  });
  expect(response.ok()).toBeTruthy();
  return response.json() as Promise<{ id: number }>;
}

async function setPollRange(
  request: APIRequestContext,
  eventId: number,
  pollRangeStart: string,
  pollRangeEnd: string,
) {
  const response = await request.put(`${API}/api/v1/events/${eventId}/date-poll/range`, {
    headers: { [TEST_USER_HEADER]: TEST_USER },
    data: { pollRangeStart, pollRangeEnd },
  });
  expect(response.ok()).toBeTruthy();
}

/** Mirrors the Monday-aligned week generation used by both frontend and backend. */
function expectedSelectableWeekCount(rangeStart: string, rangeEnd: string): number {
  const start = new Date(`${rangeStart}T00:00:00Z`);
  const end = new Date(`${rangeEnd}T00:00:00Z`);
  const offset = (start.getUTCDay() + 6) % 7; // Monday = 0
  const weekStart = new Date(start);
  weekStart.setUTCDate(start.getUTCDate() - offset);

  let count = 0;
  while (weekStart < end) {
    count++;
    weekStart.setUTCDate(weekStart.getUTCDate() + 7);
  }
  return count;
}

function weekRows(page: { locator(selector: string): Locator }): Locator {
  // Scoped to the calendar, not the organiser summary — both use
  // `week-*`-prefixed testids (the summary's `week-counts-*` would
  // otherwise also match a bare `[data-testid^="week-"]` selector).
  return page.locator('app-date-poll-calendar [data-testid^="week-"]');
}

test.describe('date-finding poll', () => {
  // The E2E frontend always authenticates as the same fixed user, so events
  // created here are visible to (and can collide with) other spec files
  // running concurrently (e.g. smoke.spec.ts's "no trips yet" assumption).
  // Deleting what we create keeps the shared user's event list clean once
  // this file's run finishes.
  let createdEventId: number | undefined;

  test.afterEach(async ({ request }) => {
    if (createdEventId === undefined) {
      return;
    }
    await request.delete(`${API}/api/v1/events/${createdEventId}`, {
      headers: { [TEST_USER_HEADER]: TEST_USER },
    });
    createdEventId = undefined;
  });

  test('organiser sets a range, marks weeks, saves, and the group summary updates', async ({
    page,
    request,
  }) => {
    const ev = await createDraftEvent(request, `range-${test.info().testId}`);
    createdEventId = ev.id;
    await page.goto(`/events/${ev.id}`);

    await expect(page.getByText('Find a date', { exact: true })).toBeVisible();

    await page.getByLabel('Earliest possible week').fill('2031-06-01');
    await page.getByLabel('Latest possible week').fill('2031-06-15');
    await page.getByRole('button', { name: 'Save range' }).click();
    await expect(page.getByText('Poll range saved')).toBeVisible();

    const week = weekRows(page).first();
    await expect(week).toBeVisible();
    const weekTestId = await week.getAttribute('data-testid');
    const weekStart = weekTestId!.replace('week-', '');

    await week.click();
    await expect(week).toHaveClass(/status-available/);

    await page.getByRole('button', { name: 'Save my availability' }).click();
    await expect(page.getByText('Availability saved')).toBeVisible();

    // Group availability summary refetches after save, without a page reload.
    await expect(page.getByTestId(`week-counts-${weekStart}`)).toContainText('1 available');

    // Reload and confirm the save actually persisted server-side.
    await page.reload();
    await expect(page.getByTestId(weekTestId!)).toHaveClass(/status-available/);
  });

  test('cycles a week through statuses, then clears a previously-saved week', async ({
    page,
    request,
  }) => {
    const ev = await createDraftEvent(request, `cycle-${test.info().testId}`);
    createdEventId = ev.id;
    await setPollRange(request, ev.id, '2031-06-01', '2031-06-15');

    await page.goto(`/events/${ev.id}`);
    const week = weekRows(page).first();

    await week.click();
    await expect(week).toHaveClass(/status-available/);
    await week.click();
    await expect(week).toHaveClass(/status-partial/);
    await week.click();
    await expect(week).toHaveClass(/status-unavailable/);

    // Persist it first — clearing a week that was never saved is a no-op
    // client-side and wouldn't exercise the delete endpoint.
    await page.getByRole('button', { name: 'Save my availability' }).click();
    await expect(page.getByText('Availability saved')).toBeVisible();

    const weekTestId = await week.getAttribute('data-testid');
    const weekStart = weekTestId!.replace('week-', '');
    await page.getByTestId(`clear-week-${weekStart}`).click();
    await expect(week).toHaveClass(/status-none/);

    // The "Availability saved" toast from the first save can still be
    // visible here, so asserting on it again wouldn't prove this second
    // save actually round-tripped — wait for the real DELETE instead.
    const [deleteResponse] = await Promise.all([
      page.waitForResponse(
        (r) => r.url().endsWith(`/date-poll/me/${weekStart}`) && r.request().method() === 'DELETE',
      ),
      page.getByRole('button', { name: 'Save my availability' }).click(),
    ]);
    expect(deleteResponse.ok()).toBeTruthy();

    const entries = await (
      await request.get(`${API}/api/v1/events/${ev.id}/date-poll/me`, {
        headers: { [TEST_USER_HEADER]: TEST_USER },
      })
    ).json();
    expect(entries).toEqual([]);
  });

  test('only shows weeks that fall within the poll range', async ({ page, request }) => {
    const rangeStart = '2031-06-02';
    const rangeEnd = '2031-06-20';
    const ev = await createDraftEvent(request, `bounds-${test.info().testId}`);
    createdEventId = ev.id;
    await setPollRange(request, ev.id, rangeStart, rangeEnd);

    await page.goto(`/events/${ev.id}`);

    await expect(weekRows(page)).toHaveCount(expectedSelectableWeekCount(rangeStart, rangeEnd));
  });

  test('organiser can unpublish a published event back to draft', async ({ page, request }) => {
    const ev = await createDraftEvent(request, `unpub-${test.info().testId}`);
    createdEventId = ev.id;
    await page.goto(`/events/${ev.id}`);

    await page.getByRole('button', { name: 'Publish' }).click();
    await expect(page.locator('mat-chip[class*="status-"]')).toHaveText('Open');
    await expect(page.getByText('Find a date', { exact: true })).not.toBeVisible();

    await page.getByRole('button', { name: 'Unpublish' }).click();
    await expect(page.getByText('Event moved back to draft')).toBeVisible();
    await expect(page.locator('mat-chip[class*="status-"]')).toHaveText('Draft');
    await expect(page.getByText('Find a date', { exact: true })).toBeVisible();
  });
});
