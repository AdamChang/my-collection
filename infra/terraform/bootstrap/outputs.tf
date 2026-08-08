output "github_deployer_service_account" {
  description = "Service account used by GitHub Actions through WIF."
  value       = google_service_account.github_deployer.email
}

output "github_workload_identity_provider" {
  description = "Fully qualified provider resource name for google-github-actions/auth."
  value       = google_iam_workload_identity_pool_provider.github.name
}

output "project_number" {
  description = "Numeric project identifier required by Workload Identity Federation."
  value       = data.google_project.current.number
}
