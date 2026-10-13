# ADR-017 — Web push for news

**Status:** Accepted
**Date:** 2026-10-13

## Decision

Members can get a browser push notification for new news posts (issue #144, part of #105). It's
the second channel on the notification outbox (ADR-015), and the first personal one.

- **Opt-in per device:** a `PushSubscription` row (`Endpoint` unique, `Name`, `P256dh`, `Auth`,
  `CreatedAt`, `LastSuccessAt?`) is the opt-in; there's no separate preference. Subscriptions
  belong to a user, not an event: a member gets pushes for every event they're confirmed in.
- **Endpoints**, all signed in; the writes are `.NotLoggedToTimeline("user settings")`:
  - `GET /api/v1/push/config` returns `{ publicKey }`.
  - `GET /api/v1/me/push-subscriptions` lists the caller's devices (id, name, endpoint,
    created, last notified). The endpoint only goes back to its owner, so the app can tell
    which entry is itself.
  - `PUT` upserts by endpoint. The name (the app suggests one like "Chrome on Android") is only
    taken for a new device or one that changed hands. An endpoint stored for someone else moves
    to the caller: the device changed hands.
  - `DELETE` with `{ endpoint }` is how a device turns itself off; it only removes the caller's
    own (204 either way).
  - `PATCH /{id}` renames and `DELETE /{id}` revokes any of the caller's devices (404 for
    anyone else's), e.g. a lost phone.
- **At most 10 devices per member.** Saving an 11th drops the one used least recently
  (`LastSuccessAt`, else `CreatedAt`). A device that died without the push service saying so
  never blocks a new one, and one account can't make a post fan out to thousands of endpoints.
- **Off unless configured:** `Push:VapidPublicKey`, `Push:VapidPrivateKey` and `Push:Subject`
  (`mailto:` or `https:`). If any is missing, every endpoint 404s, no rows are queued, and the
  profile toggle is hidden.
- **Queuing:** `NewsPublisher` adds one Push row per confirmed member (Attendee or Organiser)
  with at least one subscription, except the author. The dispatcher's existing re-check skips
  members who left before it's sent.
- **Sending:** `PushChannel` (Lib.Net.Http.WebPush, MIT) sends to each of the recipient's
  devices, with a TTL of one day. A 404 or 410 means the push service forgot the device, so the
  subscription is deleted; so is one the library can't send to at all (e.g. unusable keys).
  The row is retried only if no device got it and at least one failed with an error status, a
  timeout or no connection; a retry goes to every device again, and a duplicate isn't worth it.
  A member with no devices left by the time the row is sent is Skipped. Pushes can't be taken
  back (`CanDelete` is false).
- **Payload** in the Angular service worker's format: the title (or the event name), a plain-text
  excerpt of about 140 characters (`NewsText.Excerpt`), the app icon, and
  `onActionClick: navigateLastFocusedOrOpen` to `{App:PublicUrl}/events/{id}/news/{postId}`.
  No images (ADR-011). The payload is encrypted for the device, so the push service can't read it.

## Keys

The VAPID key pair proves to the push services that pushes come from the server the browser
subscribed to. The public key is handed out freely; the private key is a secret like the
database password and lives in the Ansible vault. Subscriptions are bound to the public key, so
**new keys silently break every subscription**: generate them once
(`npx web-push generate-vapid-keys`) and keep them. The frontend drops a subscription made with
a different key on startup, so members only have to turn push on again.

Keys sent with a subscription are checked before it's stored: `p256dh` must decode to a
65-byte uncompressed P-256 point and `auth` to 16 bytes. Anything else would only fail later,
when a post is encrypted for the device.

## Only the browsers' push services

The server posts to whatever endpoint a member saves, so like Discord webhooks (ADR-015) only
known push services are accepted: https, default port, no user info, and a host of
`fcm.googleapis.com`, `updates.push.services.mozilla.com`, `*.push.services.mozilla.com`,
`*.notify.windows.com`, `web.push.apple.com` or `*.push.apple.com`. A new browser with its own
push service needs adding to `PushEndpointValidator`.

## Frontend

`PushService` wraps `SwPush`. It subscribes with the server key and saves the subscription; if
saving fails, it unsubscribes again so the switch never shows on for a device the server doesn't
know. On startup it loads the member's devices, and a subscription the server no longer lists
(revoked from another device, dropped past the limit, or now someone else's) is removed in the
browser too. That check backs off if the member changed something while it ran. A push that
arrives while the app is open refreshes the news badge. Logging out removes the device's subscription (waiting
at most 5 seconds for the server), so a shared device stops getting someone else's news. iOS only allows push for apps added to the
Home Screen (iOS 16.4+), which the profile page says.

## Rejected alternatives

- **A per-user or per-event preference flag:** the subscription already says "this device wants
  pushes"; a second switch could disagree with it.
- **One outbox row per device:** the outbox is per recipient for every personal channel, and a
  member's devices come and go between queuing and sending.
- **Sending the Markdown body or images:** push UIs show plain text and cut it short anyway.
