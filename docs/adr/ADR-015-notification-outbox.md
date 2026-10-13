# ADR-015 — Notification outbox and text-only outside channels

**Status:** Accepted
**Date:** 2026-10-13

## Decision

News posts are mirrored to channels outside the app through an outbox table (issue #142, part
of #105). Discord, through a per-event webhook, is the first channel; Telegram, web push and
email (#143–#145) add their own senders to the same outbox.

- **Outbox:** `NotificationDispatch` rows: `EventId`, `NewsPostId?`, `Action` (Send | Delete),
  `Channel` (Discord | Telegram | Push | Email), `RecipientUserId?` (null for group channels),
  `Status` (Pending | Sent | Failed | Skipped), `Attempts`, `NextAttemptAt`, `LastError?`,
  `CreatedAt`, `SentAt?` and `ExternalId?` (e.g. the Discord message id).
- **Writing rows:** only `NewsPublisher.PublishAsync`, the single publish path (ADR-014), adds
  them, and only when the post's `Notify` is set: one row per group channel the event has set
  up. Personal channels will add one row per confirmed member who opted in, except the author.
  Rows are added to the context like `EventLog.Record`, so they're saved in the same
  transaction as the post going live. A post is published once, so rows are created once.
- **Sending:** `NewsWorker` calls `NotificationDispatcher.RunAsync(now)` after publishing due
  posts. Due rows are sent one at a time, no parallelism; plenty for about 20 people. Each row
  is claimed by counting the attempt with a conditional `UPDATE`, so a second instance skips it.
- **Retries:** after 1, 5 and 30 minutes; the 4th failed attempt marks the row Failed. Errors
  are stored in `LastError` and never contain secrets such as the webhook URL.
- **Re-checks before sending:** the post is still published, the event is still visible to
  members (not cancelled), the recipient (if any) is still a confirmed member, and the channel is
  still set up. Otherwise the row is Skipped, with the reason in `LastError`.
- **Senders** implement `INotificationChannel` (`SendAsync`, `DeleteAsync`, `IsConfigured`,
  `CanDelete`). Tests replace Discord's HTTP handler with a stub.

## Why an outbox instead of sending in the request

Sending from the publish request would make publishing depend on Discord being up, slow the
request down, and have no way to retry. Scheduled posts are published by the worker anyway, with
no request to send from. The outbox row is written in the publish transaction, so a post can't
go live without its notifications being queued, and a failed send never undoes a publish.

## At least once

A crash between sending and saving the row means it's sent again on the next run. That's rare
and a duplicate announcement is harmless, so there's no deduplication. Saving uses
`CancellationToken.None` so a shutdown mid-run doesn't add to it.

## What leaves the app

Every outside channel is text only and links back to the app:

- **Discord:** the title in bold, the Markdown body (Discord renders it) and a link to
  `{App:PublicUrl}/events/{id}/news/{postId}`. Bodies over 1,500 characters are cut after the
  last whole top-level block that fits, so a link or code block is never split, followed by
  "…"; if not even the first block fits, it falls back to `NewsText.Excerpt`.
- **No images:** attached images aren't sent (ADR-011 rules out public image URLs), and Markdown
  images in the body are sent as plain links, so Discord doesn't embed them.
- **No pings:** every message sets `allowed_mentions: { parse: [] }`, so `@everyone` or a role in
  a post is shown as text and notifies nobody.
- **No comments, reactions or acknowledgements** (ADR-014). Comfort & consent answers are never
  involved.

## Edits and deletes

- Sent notifications are never edited or sent again. Editing a post sets `EditedAt` and
  notifies nobody; `Notify` can only be changed while the post is unpublished.
- Deleting a post deletes its unsent rows. Sent rows on channels that can take a message back
  (Discord) become Pending **Delete** rows with the same `ExternalId`, and are sent with the
  same retries. `NewsPostId` is set null when the post goes, so these rows outlive it.
- A delete goes to the event's *current* webhook. If the webhook was removed in the meantime, the
  row is Skipped; if it was replaced, Discord's 404 counts as done. Copying the webhook's secret
  onto every row just to delete old messages isn't worth it.
- Deleting a whole event (only possible once it's archived or cancelled) deletes all its outbox
  rows with it, and its Discord messages stay in the channel. The webhook is deleted with the
  event, so taking them back would mean copying its secret onto every row; for an old trip's
  announcements that's not worth it.
- If a post is deleted while its message is being sent, the row is already gone when the send
  is saved. The dispatcher notices (a concurrency error on save) and queues a Delete row for the
  message it just sent.
- A message id longer than the column (100 characters) isn't kept: a cut-off id could delete the
  wrong message, so such a message just can't be taken back.

## The webhook URL is a plain secret column

`Event.DiscordWebhookUrl` is stored as is, for the same reason as join-link tokens (ADR-010):
the server has to use it, so hashing is no option, and encrypting it with a key that lives next
to the database protects nothing in a self-hosted setup. Instead it never leaves the server:

- It isn't in `EventResponse` or any other response. `GET /events/{id}/discord-webhook`
  (organisers) returns only `{ configured, hint }`, the hint being its last 4 characters.
- `PUT` and `DELETE` log `DiscordWebhookChanged` with Organisers visibility and data
  `{ configured }`, never the URL. `POST …/test` isn't logged.
- **SSRF:** the server posts to whatever is saved, so only Discord's own webhook endpoint is
  accepted: https, default port, host `discord.com` or `discordapp.com`, path
  `/api/webhooks/{id}/{token}`, no user info, query or fragment. The URL is stored normalised.
- `App:PublicUrl` (the app's origin, already used by the Atom feed's post links) builds the links
  back; never `App:ApiUrl`, which is for links to API routes.

## Rejected alternatives

- **Sending in the request:** see above.
- **A message queue or hangfire-style job library:** one table and the existing worker are
  enough for a handful of messages a week.
- **A bot instead of a webhook:** needs a hosted bot user, permissions and a token per server;
  a webhook is one URL an organiser can paste.
- **Editing Discord messages when a post is edited:** a quiet edit is the point of "an edit
  notifies nobody", and keeping messages in sync adds a second kind of row for little gain.
- **Cascading outbox rows with the post:** would lose the Discord message id needed to delete
  the message.
