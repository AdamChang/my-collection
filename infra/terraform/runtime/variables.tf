variable "project_id" {
  description = "Production Google Cloud project ID."
  type        = string
  default     = "mycollection-504914"
}

variable "region" {
  description = "Primary region for production resources."
  type        = string
  default     = "asia-east1"
}

variable "media_bucket_name" {
  description = "Private bucket used for persistent user media."
  type        = string
  default     = "mycollection-504914-media"
}

variable "storage_operator_email" {
  description = "Human operator allowed to administer the production media bucket."
  type        = string
  default     = "adamcha0516@gmail.com"
}

variable "backup_bucket_name" {
  description = "Private bucket containing MongoDB backup archives."
  type        = string
  default     = "mycollection-504914-backups"
}

variable "backup_repository_id" {
  description = "Artifact Registry repository that hosts the backup job image."
  type        = string
  default     = "mycollection-backup"
}

variable "backup_image" {
  description = "Immutable image reference for the MongoDB backup Cloud Run Job."
  type        = string
  default     = "asia-east1-docker.pkg.dev/mycollection-504914/mycollection-backup/mongo-backup@sha256:258a4badd7020b4a6baa3faf59034c7eaaf2a328cefa812ea164561e38571d18"
}

variable "backup_schedule" {
  description = "Cloud Scheduler cron expression for the production backup."
  type        = string
  default     = "0 2 * * *"
}

variable "backup_alert_email" {
  description = "Address notified when the backup job logs an error."
  type        = string
  default     = "adamcha0516@gmail.com"
}

variable "app_repository_id" {
  description = "Artifact Registry repository for production API and Web images."
  type        = string
  default     = "mycollection"
}

variable "api_image" {
  description = "Immutable production API image reference."
  type        = string
  default     = "asia-east1-docker.pkg.dev/mycollection-504914/mycollection/api@sha256:961ead12fdcbe7ff156509ce0c19b7ec7d5a6c6709f7b7a2ea5dbc60abd5e12e"

  validation {
    condition     = can(regex("@sha256:[0-9a-f]{64}$", var.api_image))
    error_message = "api_image must use an immutable sha256 digest."
  }
}

variable "web_image" {
  description = "Immutable production Web image reference."
  type        = string
  default     = "asia-east1-docker.pkg.dev/mycollection-504914/mycollection/web@sha256:cff6934695f16473331e804df7c13fb8d378a062b30f8e5fb9baec10bd452e5c"

  validation {
    condition     = can(regex("@sha256:[0-9a-f]{64}$", var.web_image))
    error_message = "web_image must use an immutable sha256 digest."
  }
}

variable "github_deployer_service_account_email" {
  description = "GitHub Actions identity allowed to deploy revisions using the runtime service accounts."
  type        = string
  default     = "github-cloud-run-deployer@mycollection-504914.iam.gserviceaccount.com"
}

variable "api_public_url" {
  description = "Canonical production API URL assigned by Cloud Run."
  type        = string
  default     = "https://mycollection-api-cswrakuenq-de.a.run.app"

  validation {
    condition     = can(regex("^https://([a-z0-9-]+\\.)+run\\.app$", var.api_public_url))
    error_message = "api_public_url must be an HTTPS run.app URL."
  }
}

variable "web_public_url" {
  description = "Canonical production Web URL assigned by Cloud Run."
  type        = string
  default     = "https://mycollection-web-cswrakuenq-de.a.run.app"

  validation {
    condition     = can(regex("^https://([a-z0-9-]+\\.)+run\\.app$", var.web_public_url))
    error_message = "web_public_url must be an HTTPS run.app URL."
  }
}

variable "service_alert_email" {
  description = "Address notified when the API or Web service returns 5xx."
  type        = string
  default     = "adamcha0516@gmail.com"
}

# IGDB 是選配的中繼資料來源，API 沒有憑證就整組不註冊 provider。
# 正式環境已於 2026-08-27 啟用，所以預設為 true——留在 false 會讓任何不帶
# -var 的 apply 靜默拆掉線上的 IGDB 設定。前提是 igdb-client-secret 這個
# Secret Manager secret 存在且至少有一個版本，否則 revision 會啟動失敗；
# 重建環境時先建 secret，或以 -var="igdb_enabled=false" 先跳過這一段。
variable "igdb_enabled" {
  description = "Whether the API service receives IGDB credentials. Requires the igdb-client-secret secret to exist with a version."
  type        = bool
  default     = true

  validation {
    condition     = !var.igdb_enabled || length(var.igdb_client_id) > 0
    error_message = "igdb_client_id must be set when igdb_enabled is true."
  }
}

# Twitch 的 Client ID 是公開識別碼（OAuth 請求會原樣送出），不是機密；
# 機密的 Client Secret 走 Secret Manager，絕不進 Terraform state 或版控。
variable "igdb_client_id" {
  description = "Public Twitch application client ID used for IGDB requests."
  type        = string
  default     = "wksiw0stv623a024l8linckyubuv0f"
}
