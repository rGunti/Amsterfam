# ADR-003 — Authentik for Auth (over Keycloak / Supabase)

**Status:** Accepted  
**Date:** 2026-06-06

## Decision

Use self-hosted Authentik as the identity provider. Discord OAuth is the primary login; Google, Apple, and email/magic link also supported. Authentik config is version-controlled via Terraform (`goauthentik/authentik` provider v2026.2.0).

## Reasons

- Simpler setup and friendlier UI than Keycloak
- Full IaC reproducibility via official Terraform provider
- Self-hosted; no vendor dependency or data leaving the stack

## Rejected Alternatives

**Keycloak** — more complex to operate; no meaningful feature advantage for this use case.  
**Supabase** — free tier pauses projects after 1 week of inactivity; unsuitable for a community app only active around event time.
