# ADR-016 — Private Atom feeds with anonymous, hashed per-member tokens

**Status:** Accepted
**Date:** 2026-10-12

## Decision

Each confirmed member can get a private Atom feed of an event's news for a feed reader
(issue #146, part of #105). The feed URL carries a per-member, per-event token:

```
{App:PublicUrl}/api/v1/feeds/{token}/news.atom
{App:PublicUrl}/api/v1/feeds/{token}/images/{postId}.{ext}
```

- **Token:** 32 random bytes in base64url (`SecureToken.New`), like join links. Only its
  SHA-256 hash is stored (`NewsFeedToken.TokenHash`, unique). The URL is returned once, by
  `POST /events/{id}/news/feed-token`; posting again revokes the old token and returns a new
  one. A filtered unique index keeps at most one active token per member and event.
- **Managing it** needs the same access as reading the news. Creating and revoking aren't on
  the timeline (`.NotLoggedToTimeline("personal feed token")`): it's a personal setting.
- **The feed** is the API's first anonymous data endpoint (`.AllowAnonymous()`). On every
  request it hashes the token, then checks the token isn't revoked, its owner is still a
  confirmed member and the event is visible to them (cancelled events: organisers only).
  Any failure, including a malformed token, is a plain 404, so the endpoint gives nothing away.
- **Removing a member** revokes their feed tokens for the event, so a link doesn't come back to
  life if they're confirmed again later.
- **Content:** published posts only, newest first, at most 50. Each entry has the title (or the
  start of the body), author, dates, a link to the post in the app, `<summary>` from
  `NewsText.Excerpt` and `<content type="html">` from `NewsText.Html`. A post's attached image
  is shown at the top of the content and as an `enclosure` link (see "Images" below). The XML is written by
  hand with `XDocument`; no syndication package.
- **Caching:** `Cache-Control: private, max-age=300` and an ETag hashed from the document
  itself, so any change (edits, deletions, renamed authors) changes it, with `If-None-Match`
  → 304. `LastUsedAt` is updated at most once an hour, by GET and HEAD alike.
- **Proxy:** Traefik routes `/api` straight to the API with no forward-auth in front (the API
  validates JWTs itself), so `/api/v1/feeds` needs no change in `infra/`.

## Why anonymous

Feed readers can't do an OIDC login, and most can't send custom headers. A secret in the URL is
the standard way to give them access, and the only practical one here.

## Why hashed, unlike join links (ADR-010)

Join links are stored plain because organisers need to copy them again. A feed link is personal,
pasted once into a reader, and can always be replaced, so it never needs to be shown twice. A
leaked database or backup then doesn't hand out working feed URLs.

## Where tokens end up

The token is part of the URL, so it lands in reverse-proxy access logs, in the reader's own logs
and, for hosted readers (Feedly, Inoreader, …), on someone else's servers along with the posts.
Using a hosted reader is the member's own choice; the subscribe sheet says so. What limits the
damage:

- Each token only reads one event's news and its post images, nothing else, and never writes.
- Membership is re-checked on every request, so leaving or being removed ends access without
  anyone having to remember the link.
- Members can revoke or regenerate the link at any time.

## Images

A post's attached image (#140) is included. It's served by
`GET /api/v1/feeds/{token}/images/{postId}.{ext}`, which runs the same token checks as the feed and only
serves published posts of the token's own event. The URL carries `?v={fileId}` so readers fetch a
replaced image again, which also lets it be cached for a day
(`private, max-age=86400`). The extension in the URL must match the image's type.

This isn't the "public unguessable image URL" ADR-011 rejects: the image is only reachable with
the member's own feed token, which already reveals the post it belongs to, and it stops working
when the link is revoked or the member leaves. Hosted readers will keep a copy of the image along
with the post text, as the member's own choice.

Markdown images in the body still become plain links, so the feed never makes readers load
images from other hosts.

## Otherwise text, with links back to the app

- No comments, reactions or acknowledgements.
- Markdown is rendered with the same Markdig pipeline as `NewsText.PlainText`, with raw HTML
  disabled (shown as text). Relative links are made absolute against `App:PublicUrl`, and links
  that aren't http(s) or mailto, `<scheme:…>` autolinks included, are dropped, keeping their text.

This narrows two earlier decisions for the Atom channel only: ADR-014's rejection of
server-rendered HTML (the app itself still renders Markdown in the browser; readers expect HTML),
and the "text only" rule for outside channels, since the feed is token-guarded like the app.

## Rejected alternatives

- **One shared feed per event:** one leak exposes it for everyone, and it can't be revoked per
  person or tied to membership.
- **HTTP basic auth or API keys in headers:** many readers don't support them, and they would be
  just as long-lived as a URL token.
- **Plain tokens, re-showable like join links:** nobody needs to see the link twice, and hashing
  costs nothing.
- **A syndication package:** an Atom document is a few elements; `XDocument` is enough.
