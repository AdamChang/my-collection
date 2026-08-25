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
