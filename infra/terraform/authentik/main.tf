# ── Flows ────────────────────────────────────────────────────────────────────

# Lookup the built-in default flows so we don't recreate them.
data "authentik_flow" "default_authorization" {
  slug = "default-provider-authorization-implicit-consent"
}

data "authentik_flow" "default_source_enrollment" {
  slug = "default-source-enrollment"
}

data "authentik_flow" "default_source_authentication" {
  slug = "default-source-authentication"
}

data "authentik_flow" "default_provider_invalidation" {
  slug = "default-provider-invalidation-flow"
}

data "authentik_flow" "default_authentication" {
  slug = "default-authentication-flow"
}

# ── Certificate (self-signed, used for token signing) ─────────────────────────

resource "tls_private_key" "amsterfam" {
  algorithm = "RSA"
  rsa_bits  = 4096
}

resource "tls_self_signed_cert" "amsterfam" {
  private_key_pem = tls_private_key.amsterfam.private_key_pem

  subject {
    common_name  = "amsterfam"
    organization = "Amsterfam"
  }

  validity_period_hours = 87600 # 10 years
  is_ca_certificate     = false

  allowed_uses = [
    "digital_signature",
    "key_encipherment",
  ]
}

resource "authentik_certificate_key_pair" "amsterfam" {
  name             = "amsterfam-signing"
  certificate_data = tls_self_signed_cert.amsterfam.cert_pem
  key_data         = tls_private_key.amsterfam.private_key_pem
}

# ── Discord OAuth Source ──────────────────────────────────────────────────────

# Discord's OAuth source type only maps username/email/name out of the box
# (authentik/sources/oauth/types/discord.py). This mapping fills in the
# avatar CDN URL from the raw profile response into a user attribute, which
# AUTHENTIK_AVATARS (see infra/docker-compose.yml) is configured to read.
resource "authentik_property_mapping_source_oauth" "discord_avatar" {
  name = "Discord Avatar"
  expression = <<-EOT
    avatar_url = None
    if info.get("avatar"):
        avatar_url = f"https://cdn.discordapp.com/avatars/{info['id']}/{info['avatar']}.png"
    return {
        "attributes": {
            "avatar": avatar_url,
        },
    }
  EOT
}

resource "authentik_source_oauth" "discord" {
  name                = "Discord"
  slug                = "discord"
  enabled             = true
  provider_type       = "discord"
  consumer_key        = var.discord_client_id
  consumer_secret     = var.discord_client_secret
  authentication_flow = data.authentik_flow.default_source_authentication.id
  enrollment_flow     = data.authentik_flow.default_source_enrollment.id
  property_mappings   = [authentik_property_mapping_source_oauth.discord_avatar.id]
}

# Creating the source above doesn't make it show up as a login button — that
# requires adding it to the login page's identification stage, which the
# goauthentik/authentik provider (~> 2026.2) doesn't expose a clean way to
# manage without a manual `terraform import` of Authentik's built-in stage.
# PATCH it directly instead (same approach as the grant_types workaround
# below), merging into whatever sources are already configured rather than
# overwriting, in case others get added later outside Terraform. Requires
# jq on the machine running `terraform apply`.
resource "terraform_data" "discord_login_button" {
  triggers_replace = [authentik_source_oauth.discord.id]

  provisioner "local-exec" {
    command = <<-EOT
      set -euo pipefail

      # authentik_source_oauth.discord.id is the slug ("discord"), but the
      # stage's `sources` field wants the source's actual UUID pk.
      resp0=$(curl -sS --retry 3 --retry-connrefused --retry-delay 2 -w '\n%%{http_code}' \
        -H "Authorization: Bearer ${var.authentik_token}" \
        "${var.authentik_url}/api/v3/sources/oauth/?slug=${authentik_source_oauth.discord.id}")
      http_status0=$(echo "$resp0" | tail -n1)
      body0=$(echo "$resp0" | sed '$d')
      if [ "$http_status0" -ge 400 ]; then
        echo "GET discord source failed ($http_status0): $body0" >&2
        exit 1
      fi
      source_pk=$(echo "$body0" | jq -r '.results[0].pk')
      if [ -z "$source_pk" ] || [ "$source_pk" = "null" ]; then
        echo "Discord source not found by slug" >&2
        exit 1
      fi

      resp1=$(curl -sS --retry 3 --retry-connrefused --retry-delay 2 -w '\n%%{http_code}' \
        -H "Authorization: Bearer ${var.authentik_token}" \
        "${var.authentik_url}/api/v3/flows/bindings/?target=${data.authentik_flow.default_authentication.id}")
      http_status1=$(echo "$resp1" | tail -n1)
      body1=$(echo "$resp1" | sed '$d')
      if [ "$http_status1" -ge 400 ]; then
        echo "GET flow bindings failed ($http_status1): $body1" >&2
        exit 1
      fi
      stage_pk=$(echo "$body1" | jq -r '.results[] | select(.stage_obj.component == "ak-stage-identification-form") | .stage_obj.pk')
      if [ -z "$stage_pk" ]; then
        echo "No identification stage bound to the default authentication flow" >&2
        exit 1
      fi

      resp2=$(curl -sS --retry 3 --retry-connrefused --retry-delay 2 -w '\n%%{http_code}' \
        -H "Authorization: Bearer ${var.authentik_token}" \
        "${var.authentik_url}/api/v3/stages/identification/$stage_pk/")
      http_status2=$(echo "$resp2" | tail -n1)
      body2=$(echo "$resp2" | sed '$d')
      if [ "$http_status2" -ge 400 ]; then
        echo "GET identification stage failed ($http_status2): $body2" >&2
        exit 1
      fi
      current_sources=$(echo "$body2" | jq -c '.sources // []')

      new_sources=$(echo "$current_sources" \
        | jq -c --arg src "$source_pk" '. + [$src] | unique')

      resp3=$(curl -sS --retry 3 --retry-connrefused --retry-delay 2 -X PATCH -w '\n%%{http_code}' \
        -H "Authorization: Bearer ${var.authentik_token}" \
        -H "Content-Type: application/json" \
        -d "{\"sources\": $new_sources}" \
        "${var.authentik_url}/api/v3/stages/identification/$stage_pk/")
      http_status3=$(echo "$resp3" | tail -n1)
      body3=$(echo "$resp3" | sed '$d')
      if [ "$http_status3" -ge 400 ]; then
        echo "PATCH identification stage failed ($http_status3): $body3" >&2
        exit 1
      fi
    EOT
  }
}

# ── OAuth2/OIDC Provider for the Amsterfam backend ────────────────────────────

resource "authentik_provider_oauth2" "amsterfam" {
  name               = "Amsterfam"
  client_id          = var.amsterfam_client_id
  client_type        = "public"
  authorization_flow = data.authentik_flow.default_authorization.id
  invalidation_flow  = data.authentik_flow.default_provider_invalidation.id

  signing_key = authentik_certificate_key_pair.amsterfam.id

  allowed_redirect_uris = concat(
    [
      {
        matching_mode = "strict"
        url           = "http://localhost:4200/auth/callback"
      },
      {
        matching_mode = "strict"
        url           = "http://localhost:8080/auth/callback"
      },
    ],
    [
      for uri in var.additional_redirect_uris : {
        matching_mode = "strict"
        url           = uri
      }
    ],
  )

  access_token_validity  = "minutes=60"
  refresh_token_validity = "days=30"

  sub_mode = "hashed_user_id"

  property_mappings = concat(
    data.authentik_property_mapping_provider_scope.scopes.ids,
    [authentik_property_mapping_provider_scope.picture.id],
  )
}

data "authentik_property_mapping_provider_scope" "scopes" {
  managed_list = [
    "goauthentik.io/providers/oauth2/scope-openid",
    "goauthentik.io/providers/oauth2/scope-email",
    "goauthentik.io/providers/oauth2/scope-profile",
  ]
}

# The built-in scope-profile mapping doesn't emit a "picture" claim. This
# adds one under the same "profile" scope, backed by user.avatar (which
# AUTHENTIK_AVATARS resolves — see the Discord avatar source mapping above).
resource "authentik_property_mapping_provider_scope" "picture" {
  name       = "Amsterfam: OpenID 'profile' picture"
  scope_name = "profile"
  expression = "return {\"picture\": request.user.avatar}"
}

# ── Application ───────────────────────────────────────────────────────────────

resource "authentik_application" "amsterfam" {
  name              = "Amsterfam"
  slug              = "amsterfam"
  protocol_provider = authentik_provider_oauth2.amsterfam.id
  meta_description  = "Annual Amsterdam friend group trip organiser"
  meta_launch_url   = "http://localhost:4200"
  open_in_new_tab   = false
}

# ── Grant types workaround ────────────────────────────────────────────────────
# The goauthentik/authentik provider (~> 2026.2) doesn't yet expose `grant_types`
# on authentik_provider_oauth2, but Authentik >= 2026.4 defaults it to an empty
# list, which makes the authorization code flow fail with "invalid_request".
# Patch it directly via the API until the provider catches up.

resource "terraform_data" "amsterfam_grant_types" {
  triggers_replace = [authentik_provider_oauth2.amsterfam.id]

  provisioner "local-exec" {
    command = <<-EOT
      curl -sf -X PATCH \
        -H "Authorization: Bearer ${var.authentik_token}" \
        -H "Content-Type: application/json" \
        -d '{"grant_types": ["authorization_code", "refresh_token"]}' \
        "${var.authentik_url}/api/v3/providers/oauth2/${authentik_provider_oauth2.amsterfam.id}/" > /dev/null
    EOT
  }
}

# ── Superuser group ───────────────────────────────────────────────────────────

resource "authentik_group" "superusers" {
  name         = "Amsterfam Superusers"
  is_superuser = false
}
