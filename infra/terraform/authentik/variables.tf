variable "authentik_url" {
  description = "Base URL of the Authentik instance"
  type        = string
  default     = "http://localhost:9000"
}

variable "authentik_token" {
  description = "Authentik API token (bootstrap token or admin token)"
  type        = string
  sensitive   = true
}

variable "discord_client_id" {
  description = "Discord OAuth2 application client ID"
  type        = string
}

variable "discord_client_secret" {
  description = "Discord OAuth2 application client secret"
  type        = string
  sensitive   = true
}

variable "amsterfam_client_id" {
  description = "OIDC client ID for the Amsterfam application"
  type        = string
  default     = "amsterfam"
}

variable "additional_redirect_uris" {
  description = "Extra allowed OIDC redirect URIs beyond the local dev defaults (e.g. the deployed frontend's callback URL)"
  type        = list(string)
  default     = []
}
