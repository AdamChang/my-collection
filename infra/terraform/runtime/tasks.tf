data "google_project" "current" {
  project_id = var.project_id
}

resource "google_service_account" "task_invoker" {
  project      = var.project_id
  account_id   = "mycollection-task-invoker"
  display_name = "MyCollection Cloud Tasks invoker"
  description  = "OIDC identity accepted only by the internal ingestion task endpoint."
}

resource "google_cloud_tasks_queue" "ingestion" {
  project  = var.project_id
  location = var.region
  name     = "mycollection-ingestion"

  rate_limits {
    max_concurrent_dispatches = 1
    max_dispatches_per_second = 1
  }

  retry_config {
    max_attempts       = 5
    min_backoff        = "10s"
    max_backoff        = "300s"
    max_doublings      = 5
    max_retry_duration = "0s"
  }
}

resource "google_cloud_tasks_queue_iam_member" "api_enqueuer" {
  project  = var.project_id
  location = google_cloud_tasks_queue.ingestion.location
  name     = google_cloud_tasks_queue.ingestion.name
  role     = "roles/cloudtasks.enqueuer"
  member   = "serviceAccount:${google_service_account.api_runtime.email}"
}

resource "google_service_account_iam_member" "api_acts_as_task_invoker" {
  service_account_id = google_service_account.task_invoker.name
  role               = "roles/iam.serviceAccountUser"
  member             = "serviceAccount:${google_service_account.api_runtime.email}"
}

resource "google_service_account_iam_member" "cloud_tasks_mints_oidc" {
  service_account_id = google_service_account.task_invoker.name
  role               = "roles/iam.serviceAccountTokenCreator"
  member             = "serviceAccount:service-${data.google_project.current.number}@gcp-sa-cloudtasks.iam.gserviceaccount.com"
}
