# ADR-017 — Web push for news

**Status:** Accepted
**Date:** 2026-10-13

## Decision

Members can get a browser push notification for new news posts (issue #144, part of #105). It's
the second channel on the notification outbox (ADR-015), and the first personal one.

- **Opt-in per device:** a `PushSubscription` row (`Endpoint` unique, `P256dh`, `Auth`,
  `CreatedAt`, `LastSuccessAt?`) is the opt-in; there's no separate preference. Subscriptions
  belong to a user, not an event: a member gets pushes for every event they're confirmed in.
- **Endpoints**, all signed in and `.NotLoggedToTimeline("user settings")`:
  `GET /api/v1/push/config` returns `{ publicKey }`; `PUT /api/v1/me/push-subscriptions` upserts
  by endpoint; `DELETE /api/v1/me/push-subscriptions` takes `{ endpoint }` and only removes the
  caller's own (204 either way). An endpoint stored for someone else moves to the caller: the
  device changed hands.
- **Off unless configured:** `Push:VapidPublicKey`, `Push:VapidPrivateKey` and `Push:Subject`
  (`mailto:` or `https:`). If any is missing, every endpoint 404s, no rows are queued, and the
  profile toggle is hidden.
- **Queuing:** `NewsPublisher` adds one Push row per confirmed member (Attendee or Organiser)
  with at least one subscription, except the author. The dispatcher's existing re-check skips
  members who left before it's sent.
- **Sending:** `PushChannel` (Lib.Net.Http.WebPush, MIT) sends to each of the recipient's
  devices, with a TTL of one day. A 404 or 410 means the push service forgot the device, so the
  subscription is deleted. The row is retried only if no device got it and at least one failed
  for another reason; a retry goes to every device again, and a duplicate isn't worth it.
  Pushes can't be taken back (`CanDelete` is false).
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

## Only the browsers' push services

The server posts to whatever endpoint a member saves, so like Discord webhooks (ADR-015) only
known push services are accepted: https, default port, no user info, and a host of
`fcm.googleapis.com`, `updates.push.services.mozilla.com`, `*.push.services.mozilla.com`,
`*.notify.windows.com`, `web.push.apple.com` or `*.push.apple.com`. A new browser with its own
push service needs adding to `PushEndpointValidator`.

## Frontend

`PushService` wraps `SwPush`: it subscribes with the server key and saves the subscription,
re-saves an existing one on startup (in case the server dropped it), and refreshes the news
badge when a push arrives while the app is open. Logging out removes the device's subscription,
so a shared device stops getting someone else's news. iOS only allows push for apps added to the
Home Screen (iOS 16.4+), which the profile page says.

## Rejected alternatives

- **A per-user or per-event preference flag:** the subscription already says "this device wants
  pushes"; a second switch could disagree with it.
- **One outbox row per device:** the outbox is per recipient for every personal channel, and a
  member's devices come and go between queuing and sending.
- **Sending the Markdown body or images:** push UIs show plain text and cut it short anyway.
