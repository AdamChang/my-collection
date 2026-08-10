resource "google_artifact_registry_repository" "backup" {
  project       = var.project_id
  location      = var.region
  repository_id = var.backup_repository_id
  description   = "Private images for the MongoDB backup Cloud Run Job."
  format        = "DOCKER"
}

resource "google_service_account" "backup_runner" {
  project      = var.project_id
  account_id   = "mycollection-backup-runner"
  display_name = "MyCollection MongoDB backup runner"
  description  = "Reads the Mongo connection secret and creates backup objects only."
}

resource "google_service_account" "backup_scheduler" {
  project      = var.project_id
  account_id   = "mycollection-backup-scheduler"
  display_name = "MyCollection backup scheduler"
  description  = "Invokes only the MongoDB backup Cloud Run Job."
}

resource "google_storage_bucket" "backups" {
  project                     = var.project_id
  name                        = var.backup_bucket_name
  location                    = var.region
  storage_class               = "STANDARD"
  uniform_bucket_level_access = true
  public_access_prevention    = "enforced"
  force_destroy               = false

  lifecycle_rule {
    condition {
      age = 30
    }

    action {
      type = "Delete"
    }
  }

  lifecycle {
    prevent_destroy = true
  }
}

data "google_iam_policy" "backups" {
  binding {
    role = "roles/storage.admin"
    members = [
      "user:${var.storage_operator_email}",
    ]
  }

  binding {
    role = "roles/storage.objectCreator"
    members = [
      "serviceAccount:${google_service_account.backup_runner.email}",
    ]
  }
}

resource "google_storage_bucket_iam_policy" "backups" {
  bucket      = google_storage_bucket.backups.name
  policy_data = data.google_iam_policy.backups.policy_data
}

data "google_secret_manager_secret" "mongo_connection_string" {
  project   = var.project_id
  secret_id = "mongo-connection-string"
}

resource "google_secret_manager_secret_iam_member" "backup_runner_reads_mongo_uri" {
  project   = var.project_id
  secret_id = data.google_secret_manager_secret.mongo_connection_string.secret_id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.backup_runner.email}"
}

resource "google_cloud_run_v2_job" "mongo_backup" {
  project  = var.project_id
  name     = "mycollection-mongo-backup"
  location = var.region

  template {
    task_count = 1

    template {
      max_retries     = 1
      service_account = google_service_account.backup_runner.email
      timeout         = "3600s"

      containers {
        image = var.backup_image

        env {
          name  = "BACKUP_BUCKET"
          value = google_storage_bucket.backups.name
        }

        env {
          name  = "MONGODB_URI_FILE"
          value = "/var/run/secrets/mongo-uri/uri"
        }

        resources {
          limits = {
            cpu    = "1"
            memory = "512Mi"
          }
        }

        volume_mounts {
          name       = "mongo-uri"
          mount_path = "/var/run/secrets/mongo-uri"
        }
      }

      volumes {
        name = "mongo-uri"

        secret {
          secret = data.google_secret_manager_secret.mongo_connection_string.secret_id
          items {
            version = "latest"
            path    = "uri"
          }
        }
      }
    }
  }

  depends_on = [
    google_secret_manager_secret_iam_member.backup_runner_reads_mongo_uri,
    google_storage_bucket_iam_policy.backups,
  ]
}

resource "google_cloud_run_v2_job_iam_member" "scheduler_invokes_backup" {
  project  = google_cloud_run_v2_job.mongo_backup.project
  location = google_cloud_run_v2_job.mongo_backup.location
  name     = google_cloud_run_v2_job.mongo_backup.name
  role     = "roles/run.invoker"
  member   = "serviceAccount:${google_service_account.backup_scheduler.email}"
}

resource "google_service_account_iam_member" "scheduler_mints_backup_token" {
  service_account_id = google_service_account.backup_scheduler.name
  role               = "roles/iam.serviceAccountTokenCreator"
  member             = "serviceAccount:service-${data.google_project.current.number}@gcp-sa-cloudscheduler.iam.gserviceaccount.com"
}

resource "google_cloud_scheduler_job" "mongo_backup" {
  project     = var.project_id
  region      = var.region
  name        = "mycollection-mongo-backup-daily"
  description = "Runs the MongoDB production backup once per day."
  schedule    = var.backup_schedule
  time_zone   = "Asia/Taipei"

  http_target {
    http_method = "POST"
    uri         = "https://run.googleapis.com/v2/projects/${var.project_id}/locations/${var.region}/jobs/${google_cloud_run_v2_job.mongo_backup.name}:run"

    oauth_token {
      service_account_email = google_service_account.backup_scheduler.email
      scope                 = "https://www.googleapis.com/auth/cloud-platform"
    }
  }
}

resource "google_monitoring_notification_channel" "backup_failure_email" {
  display_name = "MyCollection backup failures"
  type         = "email"

  labels = {
    email_address = var.backup_alert_email
  }
}

resource "google_monitoring_alert_policy" "mongo_backup_failure" {
  display_name = "MyCollection MongoDB backup failed"
  combiner     = "OR"

  alert_strategy {
    notification_rate_limit {
      period = "300s"
    }
  }

  conditions {
    display_name = "Cloud Run backup job emitted an error"

    condition_matched_log {
      filter = "resource.type=\"cloud_run_job\" AND resource.labels.job_name=\"${google_cloud_run_v2_job.mongo_backup.name}\" AND severity>=ERROR"
    }
  }

  notification_channels = [google_monitoring_notification_channel.backup_failure_email.name]
}
