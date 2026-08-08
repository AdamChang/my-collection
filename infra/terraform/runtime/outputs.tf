output "api_runtime_service_account" {
  description = "Service account attached to the production API Cloud Run service."
  value       = google_service_account.api_runtime.email
}

output "media_bucket_name" {
  description = "Private production media bucket."
  value       = google_storage_bucket.media.name
}

output "ingestion_queue_name" {
  description = "Cloud Tasks queue used for durable sync and enrich operations."
  value       = google_cloud_tasks_queue.ingestion.id
}

output "task_invoker_service_account" {
  description = "OIDC identity used by Cloud Tasks when calling the ingestion handler."
  value       = google_service_account.task_invoker.email
}
