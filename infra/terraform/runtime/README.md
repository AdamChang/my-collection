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

## IGDB credentials

IGDB is enabled in production (`igdb_enabled = true`). The API registers the provider only when both credentials are present, and the `igdb-client-secret` secret must exist with at least one version **before** any apply that carries the flag — otherwise the new revision fails to start on an unresolvable secret reference.

Bootstrapping a fresh environment therefore starts with the secret, or with `-var="igdb_enabled=false"` to skip IGDB until the secret is in place:

```powershell
gcloud secrets create igdb-client-secret --project mycollection-504914 --replication-policy automatic
$secret = Read-Host -AsSecureString "Twitch client secret"
[Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)) |
  gcloud secrets versions add igdb-client-secret --project mycollection-504914 --data-file=-
```

The apply creates the secret accessor binding and a new API revision carrying `Igdb__ClientId` and `Igdb__ClientSecret`. That revision receives **no traffic** — `traffic` is in `ignore_changes` because the canary script owns it, so verify against `status.traffic` rather than `spec.template`, and either shift traffic explicitly or let the next deployment inherit the setting:

```powershell
gcloud run services update-traffic mycollection-api --project mycollection-504914 --region asia-east1 --to-revisions <revision>=100
```

Verify with `GET /ingest/providers`: the response must list `igdb`.

Rotating the Twitch secret only needs a new secret version — the container reads `latest`, so a revision restart picks it up without a Terraform change.

The bucket has `prevent_destroy = true`. Removing it requires an explicit code change and a separately reviewed migration or retirement plan.
