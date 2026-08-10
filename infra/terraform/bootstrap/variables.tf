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

variable "billing_account" {
  description = "Billing account ID without the billingAccounts/ prefix."
  type        = string
}

variable "budget_email" {
  description = "Email address that receives project budget alerts."
  type        = string
}

variable "github_repository" {
  description = "GitHub repository allowed to exchange OIDC tokens."
  type        = string
  default     = "AdamChang/my-collection"
}

variable "github_branch" {
  description = "Only this branch can authenticate for production deployment."
  type        = string
  default     = "refs/heads/deploy"
}
