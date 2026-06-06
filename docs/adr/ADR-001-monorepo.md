# ADR-001 — Monorepo Structure

**Status:** Accepted  
**Date:** 2026-06-06

## Decision

Single monorepo containing frontend, backend, infrastructure, and Terraform config.

## Reasons

- Project has one developer and one tightly coupled stack
- Shared context (domain models, API contracts) benefits from co-location
- Simpler CI/CD; no cross-repo coordination needed

## Rejected Alternative

**Polyrepo** — adds cross-repo overhead with no benefit at this scale.
