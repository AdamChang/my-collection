locals {
  github_deployer_roles = toset([
    "roles/artifactregistry.writer",
    "roles/run.admin",
    "roles/serviceusage.serviceUsageConsumer",
  ])
}

resource "google_service_account" "github_deployer" {
  project      = var.project_id
  account_id   = "github-cloud-run-deployer"
  display_name = "GitHub Cloud Run deployer"
  description  = "Keyless production deployment identity for ${var.github_repository}."
}

resource "google_project_iam_member" "github_deployer" {
  for_each = local.github_deployer_roles

  project = var.project_id
  role    = each.value
  member  = "serviceAccount:${google_service_account.github_deployer.email}"
}

resource "google_iam_workload_identity_pool" "github" {
  project                   = var.project_id
  workload_identity_pool_id = "github-actions"
  display_name              = "GitHub Actions"
  description               = "OIDC identities for production deployment workflows."

  depends_on = [google_project_service.required]
}

resource "google_iam_workload_identity_pool_provider" "github" {
  project                            = var.project_id
  workload_identity_pool_id          = google_iam_workload_identity_pool.github.workload_identity_pool_id
  workload_identity_pool_provider_id = "mycollection-production"
  display_name                       = "MyCollection production"

  attribute_mapping = {
    "google.subject"       = "assertion.sub"
    "attribute.repository" = "assertion.repository"
    "attribute.ref"        = "assertion.ref"
    "attribute.event_name" = "assertion.event_name"
  }

  attribute_condition = join(" && ", [
    "assertion.repository == '${var.github_repository}'",
    "assertion.ref == '${var.github_branch}'",
    "assertion.event_name == 'workflow_dispatch'",
  ])

  oidc {
    issuer_uri = "https://token.actions.githubusercontent.com"
  }
}

resource "google_service_account_iam_member" "github_workload_identity_user" {
  service_account_id = google_service_account.github_deployer.name
  role               = "roles/iam.workloadIdentityUser"
  member             = "principalSet://iam.googleapis.com/${google_iam_workload_identity_pool.github.name}/attribute.repository/${var.github_repository}"
}
