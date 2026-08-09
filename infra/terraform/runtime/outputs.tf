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

output "backup_bucket_name" {
  description = "Private bucket containing MongoDB backup archives."
  value       = google_storage_bucket.backups.name
}

output "backup_job_name" {
  description = "Cloud Run Job that creates the MongoDB backup archive."
  value       = google_cloud_run_v2_job.mongo_backup.name
}

output "backup_image_repository" {
  description = "Artifact Registry repository for the MongoDB backup image."
  value       = google_artifact_registry_repository.backup.name
}

output "app_image_repository" {
  description = "Artifact Registry repository for production API and Web images."
  value       = google_artifact_registry_repository.app.name
}
