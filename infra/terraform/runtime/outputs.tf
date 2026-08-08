output "api_runtime_service_account" {
  description = "Service account attached to the production API Cloud Run service."
  value       = google_service_account.api_runtime.email
}

output "media_bucket_name" {
  description = "Private production media bucket."
  value       = google_storage_bucket.media.name
}
