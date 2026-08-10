resource "google_artifact_registry_repository" "app" {
  project       = var.project_id
  location      = var.region
  repository_id = var.app_repository_id
  description   = "Private production images for the MyCollection API and Web services."
  format        = "DOCKER"

  cleanup_policy_dry_run = false

  cleanup_policies {
    id     = "delete-older-than-30-days"
    action = "DELETE"

    condition {
      tag_state  = "ANY"
      older_than = "2592000s"
    }
  }

  cleanup_policies {
    id     = "keep-20-most-recent"
    action = "KEEP"

    most_recent_versions {
      keep_count = 20
    }
  }

  lifecycle {
    prevent_destroy = true
  }
}
