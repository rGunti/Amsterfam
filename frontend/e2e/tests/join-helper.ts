import { APIRequestContext, expect } from '@playwright/test';

const API = 'http://localhost:5293';
const USER_HEADER = 'X-Test-User-ExternalId';

/** Creates an attendee join link as `organiser` and redeems it as `user`. */
export async function joinViaLink(
  request: APIRequestContext,
  eventId: number | string,
  organiser: string,
  user: string,
): Promise<void> {
  const headers = (id: string) => ({ [USER_HEADER]: id, Authorization: 'Bearer e2e-fake-token' });
  const link = await request.post(`${API}/api/v1/events/${eventId}/join-links/`, {
    headers: headers(organiser),
    data: { kind: 'Attendee', expiresAt: null, maxUses: null },
  });
  expect(link.ok()).toBeTruthy();
  const token = (await link.json()).token as string;

  const join = await request.post(`${API}/api/v1/join-links/${token}/join`, {
    headers: headers(user),
  });
  expect(join.ok()).toBeTruthy();
}
