# Production Bootstrap

This root manages project APIs, budget alerts, and GitHub Actions Workload Identity Federation. It deliberately does not manage application services, production secrets, Atlas, or data migration.

## Prerequisites

- The `mycollection-504914` project and `mycollection-504914-tfstate` bucket already exist.
- Local Google Application Default Credentials can administer the project and its billing budget.
- Set `TF_VAR_billing_account` and `TF_VAR_budget_email` in the current shell. Do not create a tracked `terraform.tfvars` containing personal or billing values.

## Apply

```powershell
terraform init
terraform fmt -check
terraform validate
terraform plan -out bootstrap.tfplan
terraform apply bootstrap.tfplan
```

After apply, copy the two WIF outputs into GitHub Actions repository variables. Neither output is a secret.

## Safety boundaries

- Terraform state is private, versioned, and protected from public access.
- Required APIs use `disable_on_destroy = false` to prevent an accidental destroy from disabling shared project services.
- GitHub OIDC admission is limited to `AdamChang/my-collection`, `refs/heads/master`, and `workflow_dispatch`.
- The deployer can push images and update Cloud Run, but cannot administer Terraform, billing, Atlas, or project IAM.
