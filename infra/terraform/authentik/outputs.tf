output "amsterfam_client_id" {
  description = "OIDC client ID for the Amsterfam backend"
  value       = authentik_provider_oauth2.amsterfam.client_id
}

output "oidc_issuer_url" {
  description = "OIDC issuer URL (configure as Jwt__Authority in the backend)"
  value       = "http://localhost:9000/application/o/${authentik_application.amsterfam.slug}/"
}
