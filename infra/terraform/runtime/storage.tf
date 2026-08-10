resource "google_service_account" "api_runtime" {
  project      = var.project_id
  account_id   = "mycollection-api-runtime"
  display_name = "MyCollection API runtime"
  description  = "Least-privilege identity used by the production API service."
}

resource "google_storage_bucket" "media" {
  project                     = var.project_id
  name                        = var.media_bucket_name
  location                    = var.region
  storage_class               = "STANDARD"
  uniform_bucket_level_access = true
  public_access_prevention    = "enforced"
  force_destroy               = false

  lifecycle {
    prevent_destroy = true
  }
}

data "google_iam_policy" "media" {
  binding {
    role = "roles/storage.admin"
    members = [
      "user:${var.storage_operator_email}",
    ]
  }

  binding {
    role = "roles/storage.objectUser"
    members = [
      "serviceAccount:${google_service_account.api_runtime.email}",
    ]
  }
}

resource "google_storage_bucket_iam_policy" "media" {
  bucket      = google_storage_bucket.media.name
  policy_data = data.google_iam_policy.media.policy_data
}
