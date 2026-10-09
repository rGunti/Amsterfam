# ADR-014 — News feed: organiser posts with a single publish path

**Status:** Accepted
**Date:** 2026-10-09

## Decision

Organisers post news to an event (issue #105, first part #138). A `NewsPost` holds an
optional title and a Markdown body. Confirmed members (attendees and organisers) read the
feed; pending members get 403 and non-members 404, as for the timeline. Any organiser can
edit or delete any post. Writes are 409 on read-only events.

Reads are tracked with one timestamp per member, `EventAttendance.NewsSeenAt`, not per post.
Unread posts are published posts by someone else after that time. Opening the feed moves the
marker to now. `GET /events/{id}` returns the count as `unreadNewsCount` for the nav badge, and
the frontend re-fetches the open event every 60 s while the tab is visible (allowed by
ADR-006), taking over only that count.

Every post goes live through `NewsPublisher.Publish`, which sets `PublishedAt` and records the
`NewsPosted` timeline entry. Later work hooks into it: scheduled posts (#141) and
notifications (#142 onwards).

## Markdown

- The body is stored as raw Markdown and rendered in the browser with `marked`
  (`<app-markdown>`), on top of Angular's sanitiser.
- Allowed: GFM formatting, lists, quotes, code, headings (shown from `h3` down) and
  http(s)/mailto links, including bare URLs.
- Raw HTML is shown as text. Markdown images become links: loading an external image would
  hit another host for every reader, and images belong inside the app (ADR-011).
- Link clicks go through the same "leaving the app" dialog as `<app-linkified-text>`.
- The server turns Markdown into plain text with Markdig (`NewsText.PlainText` /
  `NewsText.Excerpt`) for previews and, later, channels that can't render Markdown.

## Timeline

- `NewsPosted`, `NewsEdited` and `NewsDeleted` entries store `{ postId, title }`, never the
  body.
- The timeline response adds a `news` preview (title, plain-text excerpt, image id) to each
  `NewsPosted` entry, read from the **current** post. Edits show up there, and a deleted
  post falls back to the plain sentence. The log itself stays body-free.

## Up-front columns

`NewsPost` already has `PublishAt`, `PinnedAt`, `RequiresAck`, `Notify`, `CommentsEnabled`
and `ImageFileId` so #139–#147 don't each need a migration. Until those land, posts publish
immediately and the other columns keep their defaults.

## Rejected alternatives

- **Per-post read receipts:** more rows and a privacy question ("who hasn't read it?") for
  little gain. "Did they see it?" is answered explicitly by acknowledgements (#139).
- **Copying the post into the log entry:** keeps a body around after deletion and goes stale
  on edits.
- **Server-rendered HTML:** the client already sanitises, and outside channels each need
  their own format anyway.
- **Writing posts in a dialog:** a stray click outside would lose the draft. Posts are
  written on their own page with an unsaved-changes guard, and a new post's draft is kept in
  the browser.
