# ADR-018 — News by email

**Status:** Accepted
**Date:** 2026-10-14

## Decision

Members who opt in get an email for each new news post (issue #145, part of #105). It's another
personal channel on the notification outbox (ADR-015), next to web push (ADR-017).

- **Opt-in per member:** `User.EmailNotifications` (default off). It covers every event the
  member is confirmed in, like push.
- **Which address:** `NotificationAddress.Effective` decides. A confirmed custom address comes
  first (see below), then the login address. The login address doesn't count if it's the
  `{externalId}@unknown` fallback that `CurrentUserService` makes up when Authentik sends no
  email claim. A member with no address can't turn email on.
- **Endpoints**, all signed in under `/api/v1/me`. The writes are
  `.NotLoggedToTimeline("user settings")`.
  - `GET /notification-settings` returns `emailAvailable`, `emailNotifications`, `loginEmail`
    (null for the fallback), `notificationEmail`, `notificationEmailConfirmed` and
    `effectiveEmail`.
  - `PUT /notification-settings` sets `{ emailNotifications }`. Turning it on needs an effective
    or pending address (400 otherwise). Turning it off always works.
  - `PUT /notification-email` and `DELETE /notification-email` set and drop the custom address.
  - `POST /notification-email/resend` and `POST /notification-email/confirm` handle the
    confirmation link (below).
- **Off unless configured:** without `Smtp:Host`, nothing is queued, the address endpoints 404,
  and the profile card is hidden.
  - Settings: `Smtp:Host`, `Port` (587), `Username`, `Password`, `From`, `StartTls` (true).
  - `From` must parse as an address when a host is set; startup fails otherwise.
  - The password is a secret and lives in the Ansible vault.
- **Queuing:** `NewsPublisher` adds one Email row per confirmed member who opted in and has an
  address, except the author. The address is looked up again when sending, so a change made in
  between still counts. `EmailChannel` skips the row if the member turned email off or lost
  their address since. The dispatcher's usual check skips members who left.
- **Sending:** `EmailChannel` builds the message and hands it to `IEmailSender`.
  - `MailKitEmailSender` uses MailKit (MIT) with one connection per message, which is plenty at
    this size.
  - MailKit errors are rethrown with our own message, because the outbox stores `LastError`
    and server replies can carry details we don't want there.
  - Failures are retried by the outbox. The returned `Message-Id` is the `ExternalId`.
  - Emails can't be taken back (`CanDelete` is false).
- **Message:**
  - Subject: `[{event name}] {title}`.
  - Text part: `NewsText.PlainText` of the body.
  - HTML part: `NewsText.Html`, which is Markdig with raw HTML off and links made absolute. The
    title and event name are HTML-encoded.
  - Both parts end with a link to the post (`{App:PublicUrl}/events/{id}/news/{postId}`) and a
    "manage notifications" link to `/profile`. No images (ADR-011).

## Custom address

The login address may be missing, or may be one the member doesn't read: Discord accounts are
often on an old address. So a member can set a different address, used for notifications only.
`User.Email` keeps following the login.

- Setting it (`PUT /notification-email`) stores it as pending and mails a one-time link to it:
  `{App:PublicUrl}/profile/confirm-email?token=…`.
  - The token is 32 random bytes. Only its SHA-256 is stored (`SecureToken`, as for feed
    tokens in ADR-016), and it works for **one hour**.
  - Setting a new address voids any link sent for the previous one.
  - If the mail can't be sent, the address stays pending and the request answers 503.
- The link opens the app. The app posts the token to `/notification-email/confirm` **while
  signed in**, and the token must belong to the signed-in member.
  - A forwarded or leaked link is useless to anyone else.
  - The Atom feed stays the only anonymous data endpoint (ADR-016).
  - The comparison is constant-time.
- **Until it's confirmed, news keeps going to the login address**, if that's a real one. An
  unconfirmed address never gets anything but the confirmation mail. A member whose login has no
  email can turn email on while their address is pending; news starts once they confirm.
- Resending is limited to once a minute (429). `DELETE` drops the custom address, pending or
  confirmed, and news goes back to the login address.
- The confirmation mail names the account that asked for it but nothing about any event, so a
  mistyped address learns nothing about the trip.

## Rejected alternatives

- **Unsubscribe tokens in each email:** a link to the profile page is enough for a group this
  size, and it avoids another kind of anonymous token.
- **Trusting a custom address without confirming it:** a typo would send trip news to a
  stranger.
- **Sending news to a pending address:** same reason. Before confirmation, the address is only
  a claim.
- **Overwriting `User.Email` with the custom address:** the login sync updates it on every
  sign-in, and other code expects it to be what Authentik says.
- **Sending the confirmation mail through the outbox:** the outbox is for news posts, and the
  member is waiting for this one, so a failure should show up right away.
