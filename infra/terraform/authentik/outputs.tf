output "amsterfam_client_id" {
  description = "OIDC client ID for the Amsterfam backend"
  value       = authentik_provider_oauth2.amsterfam.client_id
}

output "oidc_issuer_url" {
  description = "OIDC issuer URL (configure as Jwt__Issuer in the backend)"
  value       = "${var.authentik_url}/application/o/${authentik_application.amsterfam.slug}/"
}
