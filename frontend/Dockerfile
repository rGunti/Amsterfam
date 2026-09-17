FROM node:26-alpine AS build

# The build context here doesn't include .git (it's a subdirectory of the
# repo), so scripts/set-version.js's own `git rev-parse` fallback can't
# work — the SHA/version are passed in from outside instead.
ARG GIT_SHA=unknown
ENV GIT_SHA=$GIT_SHA
# Computed by CI from version.json via `nbgv get-version` (git height as the
# real, orderable patch component) — see .github/workflows/{backend,frontend}.yml.
ARG APP_VERSION=0.1.0
ENV APP_VERSION=$APP_VERSION

ENV PNPM_HOME="/pnpm"
ENV PATH="$PNPM_HOME:$PATH"
RUN npm install -g pnpm@10.20.0

WORKDIR /app
COPY package.json pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile

COPY . .
# Environment-specific values (API base URL, OIDC issuer) are never baked in
# here — API calls go through the "docker" build config's relative apiAddress
# (routed same-origin by Traefik, see infra/docker-compose.prod.yml), and
# OIDC_ISSUER is resolved at container startup from config.json (see
# docker/render-config.sh).
RUN pnpm run build --configuration=production,docker

FROM nginx:alpine

COPY --from=build /app/dist/amsterfam-frontend/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
# Deliberately not under /etc/nginx/templates/ — that path is auto-processed
# by nginx's own entrypoint script, which would write it to the wrong place
# (/etc/nginx/conf.d/, not the html root) before our own script runs.
COPY docker/config.json.tmpl /etc/nginx/config-template/config.json.tmpl
COPY docker/render-config.sh /docker-entrypoint.d/40-render-config.sh
RUN chmod +x /docker-entrypoint.d/40-render-config.sh

ENV OIDC_ISSUER=http://localhost:9000/application/o/amsterfam/

EXPOSE 80
