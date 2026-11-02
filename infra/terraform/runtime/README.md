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

## API 機密（`api-config`）

`Jwt:Key`、`SecretProtection:Key`、`Igdb:ClientId`、`Igdb:ClientSecret` 合併在 Secret Manager 的 `api-config` 一個 JSON（[ADR-0013](../../../docs/adr/0013-api-secrets-merged-into-single-config-secret.md)），以 secret volume 掛載到 `/secrets/api/config.json`，由 `SECRETS_CONFIG_FILE` 指向。`mongo-connection-string` 維持獨立，因為備份 Job 只需要它。

是否啟用 IGDB 只看 JSON 裡有沒有 `Igdb` 區塊，Terraform 沒有開關。`api-config` 必須在**任何 apply 之前**存在且至少有一個 version，否則新 revision 會因為解析不到 secret 而啟動失敗。

### 建立 api-config

值不落地成檔案、不進 shell history：三個值由 `gcloud` 直接讀出，在 pipe 裡以 `jq` 組成 JSON 再寫入。`--rawfile` 搭配 process substitution 會原樣保留值（包含結尾換行），不經過磁碟。本機沒有 `jq`，請在 Cloud Shell 執行。ClientId 是公開識別碼。

```bash
P=mycollection-504914
gcloud secrets create api-config --project "$P" --replication-policy automatic
jq -cn \
  --rawfile jwt <(gcloud secrets versions access latest --secret=jwt-signing-key --project "$P") \
  --rawfile protection <(gcloud secrets versions access latest --secret=secret-protection-key --project "$P") \
  --rawfile igdb <(gcloud secrets versions access latest --secret=igdb-client-secret --project "$P") \
  --arg igdbId wksiw0stv623a024l8linckyubuv0f \
  '{Jwt:{Key:$jwt},SecretProtection:{Key:$protection},Igdb:{ClientId:$igdbId,ClientSecret:$igdb}}' |
  gcloud secrets versions add api-config --project "$P" --data-file=-

# 只列出鍵，不印出值
gcloud secrets versions access latest --secret=api-config --project "$P" | jq -c '[paths(scalars) | join(":")]'
```

最後一行必須輸出 `["Jwt:Key","SecretProtection:Key","Igdb:ClientId","Igdb:ClientSecret"]`。

### 切換順序

`image` 在 `ignore_changes` 裡，所以 apply 會沿用線上的 image。舊 image 不認得 `SECRETS_CONFIG_FILE`，而新 template 不再提供 `Jwt__Key` 等環境變數，順序錯了 revision 就起不來：

1. 建立 `api-config`（上一節）。
2. 以 `Deploy production` workflow 部署含 `SecretsFileConfiguration` 的新 image。此時 template 還是舊的，新程式讀不到 `SECRETS_CONFIG_FILE` 就照舊用環境變數。
3. `terraform plan` 確認 `api_reads_igdb_secret` 是 moved 而不是 destroy／create，再 `terraform apply`。
4. 新 revision **不會取得流量**：`traffic` 在 `ignore_changes` 裡，由 canary 腳本負責。請以 `status.traffic` 確認，而不是看 `spec.template`；確認後再明確切換流量，或讓下一次部署繼承這份 template：

```powershell
gcloud run services update-traffic mycollection-api --project mycollection-504914 --region asia-east1 --to-revisions <revision>=100
```

以 `GET /ingest/providers` 驗證：回應必須列出 `igdb`，而且登入正常。

舊的三個 secret 與 IAM binding 在這一版保留給 canary 回滾使用，下一個 PR 才移除。輪替流程見 [production-operations.md](../../../docs/deployment/production-operations.md)。

The bucket has `prevent_destroy = true`. Removing it requires an explicit code change and a separately reviewed migration or retirement plan.
