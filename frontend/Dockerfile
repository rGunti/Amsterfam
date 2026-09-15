FROM node:26-alpine AS build

# Baked into the bundle at build time — there's no runtime config mechanism,
# so these must be set per-environment via `docker compose build --build-arg`
# (see infra/docker-compose.yml).
ARG API_ADDRESS=http://localhost:5293
ARG OIDC_ISSUER=http://localhost:9000/application/o/amsterfam/

# The build context here doesn't include .git (it's a subdirectory of the
# repo), so scripts/set-version.js's own `git rev-parse` fallback can't
# work — the SHA is passed in from outside instead.
ARG GIT_SHA=unknown
ENV GIT_SHA=$GIT_SHA

ENV PNPM_HOME="/pnpm"
ENV PATH="$PNPM_HOME:$PATH"
RUN npm install -g pnpm@10.20.0

WORKDIR /app
COPY package.json pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile

COPY . .
# ngsw-config.json's "current-user" dataGroup URL is also the localhost:5293
# placeholder — it must stay in sync with environment.ts here, since a plain
# `pnpm run build` outside Docker won't rewrite either and will keep pointing
# at the dev API address.
RUN sed -i "s|http://localhost:5293|${API_ADDRESS}|" src/environments/environment.ts ngsw-config.json && \
    sed -i "s|http://localhost:9000/application/o/amsterfam/|${OIDC_ISSUER}|" src/app/core/auth/auth.config.ts
RUN pnpm run build

FROM nginx:alpine

COPY --from=build /app/dist/amsterfam-frontend/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
