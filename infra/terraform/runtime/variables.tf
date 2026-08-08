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
