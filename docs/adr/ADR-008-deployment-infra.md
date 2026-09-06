# ADR-008 — Ansible + Traefik for Deployment (over Kubernetes / managed PaaS)

**Status:** Accepted
**Date:** 2026-09-06

## Decision

Deploy to a single Debian 13 host (`dev.amsterfam.eu`) provisioned and documented via Ansible
(`infra/ansible/`). Traefik fronts all services with automatic Let's Encrypt certs (HTTP-01
challenge). Images are built directly on the host from a git checkout — no container registry.
CI (`.github/workflows/deploy.yml`) auto-deploys on `main` after the existing build/test
workflows pass, connecting as an unprivileged `deploy` user (never root).

## Reasons

- Single host, single environment, ~18-20 users — Kubernetes/managed PaaS would be pure
  overhead with no operational benefit at this scale.
- Ansible gives full reproducibility (rebuild the box, or stand up a second environment) without
  the ongoing cost of a registry, image signing, or a cluster control plane.
- Traefik's Docker provider means routing is just container labels — no separate reverse-proxy
  config to keep in sync with `docker-compose.yml`.
- HTTP-01 over DNS-01: only 3 known subdomains, no wildcard needed, avoids handing Traefik
  DNS-provider API credentials.

## Rejected Alternatives

**Kubernetes (k3s or managed)** — orders of magnitude more operational surface than a
friend-group app needs.
**Push-to-registry + pull-on-host** — an extra piece of infra (registry auth, image pull
secrets) for a two-image app redeployed a few times a week at most; building on the host from
the same Dockerfiles CI already validates is simpler and sufficient.
