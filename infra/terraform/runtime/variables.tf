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
  default     = "asia-east1-docker.pkg.dev/mycollection-504914/mycollection-backup/mongo-backup@sha256:5f04d31c41460ab168da26dad3d5f88cb6b68bb2661e791c3832d8237370e98d"
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
