locals {
  api_service_name = "mycollection-api"
  web_service_name = "mycollection-web"
}

data "google_secret_manager_secret" "api_config" {
  project   = var.project_id
  secret_id = "api-config"
}

# 以下三個舊 secret 與對應的 IAM 是回滾用，PR 2 移除：
# canary 失敗時流量切回舊 revision，舊 revision 仍以 env 引用這三個 secret。
data "google_secret_manager_secret" "jwt_signing_key" {
  project   = var.project_id
  secret_id = "jwt-signing-key"
}

data "google_secret_manager_secret" "secret_protection_key" {
  project   = var.project_id
  secret_id = "secret-protection-key"
}

data "google_secret_manager_secret" "igdb_client_secret" {
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

resource "google_secret_manager_secret_iam_member" "api_reads_api_config" {
  project   = var.project_id
  secret_id = data.google_secret_manager_secret.api_config.secret_id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.api_runtime.email}"
}

# 回滾用，PR 2 移除。
resource "google_secret_manager_secret_iam_member" "api_reads_jwt_key" {
  project   = var.project_id
  secret_id = data.google_secret_manager_secret.jwt_signing_key.secret_id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.api_runtime.email}"
}

# 回滾用，PR 2 移除。
resource "google_secret_manager_secret_iam_member" "api_reads_protection_key" {
  project   = var.project_id
  secret_id = data.google_secret_manager_secret.secret_protection_key.secret_id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.api_runtime.email}"
}

# 回滾用，PR 2 移除。
resource "google_secret_manager_secret_iam_member" "api_reads_igdb_secret" {
  project   = var.project_id
  secret_id = data.google_secret_manager_secret.igdb_client_secret.secret_id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.api_runtime.email}"
}

# 拿掉 count 後位址從 [0] 變成無索引；沒有 moved 會先建後刪，
# 兩者是同一條 IAM binding，刪除那一步會讓 binding 消失。PR 2 隨資源一起移除。
moved {
  from = google_secret_manager_secret_iam_member.api_reads_igdb_secret[0]
  to   = google_secret_manager_secret_iam_member.api_reads_igdb_secret
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

    # Jwt、SecretProtection、Igdb 合併在 api-config 一個 JSON（ADR-0013）。
    # volume 會蓋掉整個掛載目錄，所以必須是專屬目錄。
    volumes {
      name = "api-secrets"
      secret {
        secret = data.google_secret_manager_secret.api_config.secret_id
        items {
          version = "latest"
          path    = "config.json"
        }
      }
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
        name  = "SECRETS_CONFIG_FILE"
        value = "/secrets/api/config.json"
      }

      volume_mounts {
        name       = "api-secrets"
        mount_path = "/secrets/api"
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
    google_secret_manager_secret_iam_member.api_reads_api_config,
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
