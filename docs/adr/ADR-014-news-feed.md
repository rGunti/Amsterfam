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

- `NewsPosted`, `NewsEdited`, `NewsDeleted`, `NewsPinned` and `NewsUnpinned` entries store
  `{ postId, title }`, never the body. Pinning an already pinned post logs nothing.
- Acknowledgements and reactions are personal and never logged.
- The timeline response adds a `news` preview (title, plain-text excerpt, image id) to each
  `NewsPosted` entry, read from the **current** post. Edits show up there, and a deleted
  post falls back to the plain sentence. The log itself stays body-free.

## Pins, acknowledgements and reactions (#139)

- **Pins:** organisers pin and unpin from a post's menu, or when creating it. The first feed
  page starts with every pinned post (most recently pinned first). The keyset paging only
  walks unpinned posts, so the cursor logic stays as it was.
- **Acknowledgements:** a post with `RequiresAck` gets a "Got it" button. Acknowledging is
  idempotent and can't be undone. Organisers see `ackCount`/`ackTotal` and a list of who
  hasn't acknowledged; both count current confirmed members only, so Pending members are
  never "missing". Editing keeps acknowledgements unless the organiser asks everyone to
  acknowledge again.
- **Reactions:** a fixed server-side list (👍 ❤️ 😂 🎉 😮 🙏), compared exactly; anything else
  is a 400. This avoids grapheme validation and abuse.
- **Overview:** `GET .../news/summary` feeds the "Latest news" card on the event page:
  pinned posts, posts still waiting for the viewer's acknowledgement, and the newest post.

## Images (#140)

- One image per post, stored as an `EventFile` like the banner and served only through the
  authenticated API. Outside channels (#142 onwards) stay text-only and link back (ADR-011).
- The browser scales images down to 1600 px JPEG before uploading; the server checks the
  bytes (JPEG/PNG/WebP, max 5 MB) and serves the sniffed type. `ImageUpload` holds the
  shared upload and serving code.
- `ImageFileId` is a set-null FK, so replacing an image or deleting a post deletes the file
  explicitly.
- Image changes are edits (`NewsEdited` with `changed: ["image"]`). A new post's first image
  is uploaded with `initial=true` straight after creating it, so it doesn't look edited.

## Comments (#147)

- **Who:** confirmed members read and write comments on published posts, while the event
  is writable and the post has `CommentsEnabled` (organisers can turn it off, e.g. for a
  pure "info" post; existing comments stay readable). Only the author edits a comment
  (`EditedAt`, shown as "edited"); the author or any organiser deletes it (hard delete).
  Members who leave keep their comments, and deleting the post deletes them.
- **Shape:** one flat list per post, oldest first, keyset-paged on `(CreatedAt, Id)`. Plain
  text up to 2000 characters, rendered with links made clickable; no Markdown or mentions.
- **Unread:** the nav badge still counts posts only. Posts carry `commentCount` and
  `newCommentCount` (other people's comments since `NewsSeenAt`), so no new read tracking.
  An open comment list fetches newer comments on the event poll's interval.
- **Not in the timeline, not outside the app:** comments are conversation already shown
  on the post, and logging them would flood the timeline. They're never sent to Discord,
  Telegram, push, email or RSS.
- **Moderation is logged:** when an organiser deletes someone else's comment, an
  organisers-only `NewsCommentRemoved` entry records `{ postId, title }` with the comment's
  author as subject, never the text. Deleting your own comment logs nothing.

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
