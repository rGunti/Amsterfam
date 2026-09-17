#!/bin/sh
# Renders config.json from the container's runtime environment before nginx
# starts, so the same image can be deployed to any environment without a
# rebuild. See src/app/core/config/runtime-config.ts for the consumer.
set -eu

envsubst '${OIDC_ISSUER}' \
  < /etc/nginx/config-template/config.json.tmpl \
  > /usr/share/nginx/html/config.json
