# Production Runtime Infrastructure

This Terraform root owns runtime resources that persist independently of Cloud Run revisions.

The media bucket uses uniform bucket-level access and enforced public access prevention. Only the API runtime service account receives `roles/storage.objectUser`; browser clients must retrieve media through authorised API endpoints.

```powershell
terraform init
terraform fmt -check
terraform validate
terraform plan -out runtime.tfplan
terraform apply runtime.tfplan
```

The bucket has `prevent_destroy = true`. Removing it requires an explicit code change and a separately reviewed migration or retirement plan.
