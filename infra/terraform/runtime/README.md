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

## Enabling IGDB

IGDB is optional and ships disabled (`igdb_enabled = false`). The API registers the provider only when both credentials are present, so enabling it is a two-step sequence — the secret must exist **before** the flag flips, otherwise the new revision fails to start on an unresolvable secret reference.

```powershell
gcloud secrets create igdb-client-secret --project mycollection-504914 --replication-policy automatic
$secret = Read-Host -AsSecureString "Twitch client secret"
[Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)) |
  gcloud secrets versions add igdb-client-secret --project mycollection-504914 --data-file=-
```

Then set `igdb_enabled = true` (via `-var` or a tfvars file) and apply. The apply creates the secret accessor binding and a new API revision carrying `Igdb__ClientId` and `Igdb__ClientSecret`. Verify with `GET /ingest/providers`: the response must list `igdb`.

Rotating the Twitch secret only needs a new secret version — the container reads `latest`, so a revision restart picks it up without a Terraform change.

The bucket has `prevent_destroy = true`. Removing it requires an explicit code change and a separately reviewed migration or retirement plan.
