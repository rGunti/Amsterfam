# ADR-011 — Event files stored in Postgres; banner images

**Status:** Accepted
**Date:** 2026-09-20

## Decision

Files live in an `EventFiles` table (metadata plus a `bytea` `Data` column), each row owned by
an event and cascade-deleted with it (issue #121). `Event.BannerFileId` points at the current
banner; replacing or removing a banner deletes the old file row.

Banner uploads are a raw-body `PUT` (no multipart), capped at 5 MB, and limited to JPEG, PNG and
WebP. The stored and served content type comes from the file's magic bytes, so a client can't
label HTML or SVG as an image. The browser downscales to 1600 px on the longest edge and
re-encodes as JPEG before uploading, which also strips EXIF/GPS metadata.

Images are served from authenticated endpoints (`/events/{id}/banner`, and
`/join-links/{token}/banner` for people who aren't members yet). `<img src>` can't send a bearer
token, so the frontend fetches them as blobs and shows an object URL. The file id is the ETag,
so revalidation is a 304 that never reads the blob.

## Answers to the open questions in #121

- **Max size of a Postgres `bytea`:** 1 GB per value, which is far beyond what we need. The 5 MB
  cap is a product limit, not a database one.
- **How to expose files:** dedicated per-purpose GET endpoints that reuse the existing access
  checks, not a generic `/files/{id}` route. That keeps "who may see this" next to the thing
  that owns it (event membership, or a usable join link).

## Reasons

- ~20 users and a handful of small images: a `bytea` column keeps backups, transactions and
  cascade deletes in one place, with no object store to run.
- Denormalising `BannerFileId` onto `Event` means event queries never touch the blob.

## Rejected alternatives

- Object storage (S3/MinIO): another service to run and back up for a few megabytes.
- Server-side resizing: needs an image library (ImageSharp's licence is not free for all use)
  and the browser already does it.
- Public unguessable image URLs: would let the banner of a private event leak via a shared link.
