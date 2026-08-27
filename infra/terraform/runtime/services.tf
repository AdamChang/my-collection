locals {
  api_service_name = "mycollection-api"
  web_service_name = "mycollection-web"
}

data "google_secret_manager_secret" "jwt_signing_key" {
  project   = var.project_id
  secret_id = "jwt-signing-key"
}

data "google_secret_manager_secret" "secret_protection_key" {
  project   = var.project_id
  secret_id = "secret-protection-key"
}

data "google_secret_manager_secret" "igdb_client_secret" {
  count = var.igdb_enabled ? 1 : 0

  project   = var.project_id
  secret_id = "igdb-client-secret"
}

resource "google_service_account" "web_runtime" {
  project      = var.project_id
  account_id   = "mycollection-web-runtime"
  display_name = "MyCollection Web runtime"
  description  = "Least-privilege identity used by the production Web service."
}

resource "google_secret_manager_secret_iam_member" "api_reads_mongo_uri" {
  project   = var.project_id
  secret_id = data.google_secret_manager_secret.mongo_connection_string.secret_id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.api_runtime.email}"
}

resource "google_secret_manager_secret_iam_member" "api_reads_jwt_key" {
  project   = var.project_id
  secret_id = data.google_secret_manager_secret.jwt_signing_key.secret_id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.api_runtime.email}"
}

resource "google_secret_manager_secret_iam_member" "api_reads_protection_key" {
  project   = var.project_id
  secret_id = data.google_secret_manager_secret.secret_protection_key.secret_id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.api_runtime.email}"
}

resource "google_secret_manager_secret_iam_member" "api_reads_igdb_secret" {
  count = var.igdb_enabled ? 1 : 0

  project   = var.project_id
  secret_id = data.google_secret_manager_secret.igdb_client_secret[0].secret_id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.api_runtime.email}"
}

resource "google_cloud_run_v2_service" "api" {
  project             = var.project_id
  location            = var.region
  name                = local.api_service_name
  ingress             = "INGRESS_TRAFFIC_ALL"
  deletion_protection = true

  template {
    service_account = google_service_account.api_runtime.email
    timeout         = "300s"

    scaling {
      min_instance_count = 0
      max_instance_count = 1
    }

    containers {
      image = var.api_image

      ports {
        container_port = 8080
      }

      resources {
        limits = {
          cpu    = "1"
          memory = "512Mi"
        }
        cpu_idle          = true
        startup_cpu_boost = true
      }

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }

      env {
        name  = "Mongo__Database"
        value = "mycollection"
      }

      env {
        name  = "Jwt__Issuer"
        value = "mycollection"
      }

      env {
        name  = "Jwt__Audience"
        value = "mycollection-web"
      }

      env {
        name  = "Storage__Provider"
        value = "Gcs"
      }

      env {
        name  = "Storage__Bucket"
        value = google_storage_bucket.media.name
      }

      env {
        name  = "Cors__AllowedOrigins__0"
        value = var.web_public_url
      }

      env {
        name  = "Tasks__Provider"
        value = "CloudTasks"
      }

      env {
        name  = "Tasks__ProjectId"
        value = var.project_id
      }

      env {
        name  = "Tasks__Location"
        value = var.region
      }

      env {
        name  = "Tasks__Queue"
        value = google_cloud_tasks_queue.ingestion.name
      }

      env {
        name  = "Tasks__HandlerUrl"
        value = "${var.api_public_url}/internal/tasks/ingestion"
      }

      env {
        name  = "Tasks__Audience"
        value = var.api_public_url
      }

      env {
        name  = "Tasks__ServiceAccountEmail"
        value = google_service_account.task_invoker.email
      }

      env {
        name = "Mongo__ConnectionString"
        value_source {
          secret_key_ref {
            secret  = data.google_secret_manager_secret.mongo_connection_string.secret_id
            version = "latest"
          }
        }
      }

      env {
        name = "Jwt__Key"
        value_source {
          secret_key_ref {
            secret  = data.google_secret_manager_secret.jwt_signing_key.secret_id
            version = "latest"
          }
        }
      }

      env {
        name = "SecretProtection__Key"
        value_source {
          secret_key_ref {
            secret  = data.google_secret_manager_secret.secret_protection_key.secret_id
            version = "latest"
          }
        }
      }

      # 兩個變數必須同進同出：API 只在兩者皆非空時才註冊 IGDB provider，
      # 少一個就是「設定了卻不會生效」的無聲狀態。
      dynamic "env" {
        for_each = var.igdb_enabled ? [1] : []

        content {
          name  = "Igdb__ClientId"
          value = var.igdb_client_id
        }
      }

      dynamic "env" {
        for_each = var.igdb_enabled ? [1] : []

        content {
          name = "Igdb__ClientSecret"
          value_source {
            secret_key_ref {
              secret  = data.google_secret_manager_secret.igdb_client_secret[0].secret_id
              version = "latest"
            }
          }
        }
      }

      startup_probe {
        initial_delay_seconds = 0
        timeout_seconds       = 3
        period_seconds        = 10
        failure_threshold     = 12

        http_get {
          path = "/health/live"
          port = 8080
        }
      }

      liveness_probe {
        initial_delay_seconds = 10
        timeout_seconds       = 3
        period_seconds        = 30
        failure_threshold     = 3

        http_get {
          path = "/health/live"
          port = 8080
        }
      }
    }
  }

  traffic {
    type    = "TRAFFIC_TARGET_ALLOCATION_TYPE_LATEST"
    percent = 100
  }

  lifecycle {
    prevent_destroy = true
    ignore_changes = [
      template[0].containers[0].image,
      traffic,
    ]
  }

  depends_on = [
    google_secret_manager_secret_iam_member.api_reads_mongo_uri,
    google_secret_manager_secret_iam_member.api_reads_jwt_key,
    google_secret_manager_secret_iam_member.api_reads_protection_key,
    google_secret_manager_secret_iam_member.api_reads_igdb_secret,
  ]
}

resource "google_cloud_run_v2_service" "web" {
  project             = var.project_id
  location            = var.region
  name                = local.web_service_name
  ingress             = "INGRESS_TRAFFIC_ALL"
  deletion_protection = true

  template {
    service_account = google_service_account.web_runtime.email
    timeout         = "60s"

    scaling {
      min_instance_count = 0
      max_instance_count = 1
    }

    containers {
      image = var.web_image

      ports {
        container_port = 8080
      }

      resources {
        limits = {
          cpu    = "1"
          memory = "256Mi"
        }
        cpu_idle          = true
        startup_cpu_boost = true
      }

      env {
        name  = "API_BASE_URL"
        value = var.api_public_url
      }

      startup_probe {
        initial_delay_seconds = 0
        timeout_seconds       = 3
        period_seconds        = 5
        failure_threshold     = 12

        http_get {
          path = "/"
          port = 8080
        }
      }

      liveness_probe {
        initial_delay_seconds = 10
        timeout_seconds       = 3
        period_seconds        = 30
        failure_threshold     = 3

        http_get {
          path = "/"
          port = 8080
        }
      }
    }
  }

  traffic {
    type    = "TRAFFIC_TARGET_ALLOCATION_TYPE_LATEST"
    percent = 100
  }

  lifecycle {
    prevent_destroy = true
    ignore_changes = [
      template[0].containers[0].image,
      traffic,
    ]
  }
}

resource "google_cloud_run_v2_service_iam_member" "public_invokes_api" {
  project  = var.project_id
  location = google_cloud_run_v2_service.api.location
  name     = google_cloud_run_v2_service.api.name
  role     = "roles/run.invoker"
  member   = "allUsers"
}

resource "google_cloud_run_v2_service_iam_member" "public_invokes_web" {
  project  = var.project_id
  location = google_cloud_run_v2_service.web.location
  name     = google_cloud_run_v2_service.web.name
  role     = "roles/run.invoker"
  member   = "allUsers"
}

resource "google_cloud_run_v2_service_iam_member" "task_invoker_calls_api" {
  project  = var.project_id
  location = google_cloud_run_v2_service.api.location
  name     = google_cloud_run_v2_service.api.name
  role     = "roles/run.invoker"
  member   = "serviceAccount:${google_service_account.task_invoker.email}"
}

resource "google_service_account_iam_member" "github_deployer_uses_api_runtime" {
  service_account_id = google_service_account.api_runtime.name
  role               = "roles/iam.serviceAccountUser"
  member             = "serviceAccount:${var.github_deployer_service_account_email}"
}

resource "google_service_account_iam_member" "github_deployer_uses_web_runtime" {
  service_account_id = google_service_account.web_runtime.name
  role               = "roles/iam.serviceAccountUser"
  member             = "serviceAccount:${var.github_deployer_service_account_email}"
}
