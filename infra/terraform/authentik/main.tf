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

resource "authentik_source_oauth" "discord" {
  name                = "Discord"
  slug                = "discord"
  enabled             = true
  provider_type       = "discord"
  consumer_key        = var.discord_client_id
  consumer_secret     = var.discord_client_secret
  authentication_flow = data.authentik_flow.default_source_authentication.id
  enrollment_flow     = data.authentik_flow.default_source_enrollment.id
}

# ── OAuth2/OIDC Provider for the Amsterfam backend ────────────────────────────

resource "authentik_provider_oauth2" "amsterfam" {
  name               = "Amsterfam"
  client_id          = var.amsterfam_client_id
  client_type        = "confidential"
  authorization_flow = data.authentik_flow.default_authorization.id
  invalidation_flow  = data.authentik_flow.default_provider_invalidation.id

  signing_key = authentik_certificate_key_pair.amsterfam.id

  allowed_redirect_uris = [
    {
      matching_mode = "strict"
      url           = "http://localhost:4200/auth/callback"
    },
    {
      matching_mode = "strict"
      url           = "http://localhost:8080/auth/callback"
    },
  ]

  access_token_validity  = "minutes=60"
  refresh_token_validity = "days=30"

  sub_mode = "hashed_user_id"

  property_mappings = data.authentik_property_mapping_provider_scope.scopes.ids
}

data "authentik_property_mapping_provider_scope" "scopes" {
  managed_list = [
    "goauthentik.io/providers/oauth2/scope-openid",
    "goauthentik.io/providers/oauth2/scope-email",
    "goauthentik.io/providers/oauth2/scope-profile",
  ]
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

# ── Superuser group ───────────────────────────────────────────────────────────

resource "authentik_group" "superusers" {
  name         = "Amsterfam Superusers"
  is_superuser = false
}

resource "authentik_group" "organisers" {
  name         = "Amsterfam Organisers"
  is_superuser = false
}
