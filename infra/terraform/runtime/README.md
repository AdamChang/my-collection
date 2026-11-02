# Production Runtime Infrastructure

This Terraform root owns runtime resources that persist independently of Cloud Run revisions.

The media bucket uses uniform bucket-level access and enforced public access prevention. Only the API runtime service account receives `roles/storage.objectUser`; browser clients must retrieve media through authorised API endpoints.

GCS backend 和 google provider 只認 Application Default Credentials（ADC），不會沿用 `gcloud auth login` 的登入。本機第一次執行、或 ADC 過期時，`terraform init` 會出現 `could not find default credentials`，請先登入：

```powershell
gcloud auth application-default login
gcloud auth application-default set-quota-project mycollection-504914
```

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

> 這是 2026-11 從舊的三個 secret 合併時的紀錄，那三個 secret 已刪除，以下指令無法再執行。重建時改用 [production-operations.md](../../../docs/deployment/production-operations.md) 的輪替流程，逐欄位寫入新值；保留這段是為了說明「值必須原樣保留」的處理方式。

值不落地成檔案、不進 shell history：三個值由 `gcloud` 直接讀出，只存在於變數和 pipe 裡，組成 JSON 後直接寫入。值必須原樣保留（包含結尾換行），所以不能用一般的文字擷取。ClientId 是公開識別碼。

本機（PowerShell，建議 pwsh 7）用內建的 `ConvertTo-Json`，不需要 `jq`：

```powershell
$P = 'mycollection-504914'
$OutputEncoding = [Text.UTF8Encoding]::new($false)

# 取回 base64 再自行解碼：直接擷取 gcloud 的文字輸出會被 PowerShell 去掉結尾換行，值就不是原樣了
function Get-SecretValue([string]$Name) {
  $b64 = (gcloud secrets versions access latest --secret=$Name --project $P --format='get(payload.data)').Trim()
  $b64 = $b64.Replace('-', '+').Replace('_', '/')
  $b64 = $b64.PadRight($b64.Length + (4 - $b64.Length % 4) % 4, '=')
  [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b64))
}

gcloud secrets create api-config --project $P --replication-policy automatic

[ordered]@{
  Jwt              = @{ Key = Get-SecretValue 'jwt-signing-key' }
  SecretProtection = @{ Key = Get-SecretValue 'secret-protection-key' }
  Igdb             = [ordered]@{ ClientId = 'wksiw0stv623a024l8linckyubuv0f'; ClientSecret = Get-SecretValue 'igdb-client-secret' }
} | ConvertTo-Json -Compress |
  gcloud secrets versions add api-config --project $P --data-file=-

# 只列出鍵，不印出值
$cfg = Get-SecretValue 'api-config' | ConvertFrom-Json
$cfg.PSObject.Properties | ForEach-Object { $s = $_.Name; $_.Value.PSObject.Properties.Name | ForEach-Object { "${s}:$_" } }
```

最後一段必須輸出 `Jwt:Key`、`SecretProtection:Key`、`Igdb:ClientId`、`Igdb:ClientSecret` 四行。pipe 傳給 `gcloud` 時 PowerShell 會在 JSON 後面多加一個換行，不影響解析。

Cloud Shell（bash）則用 `jq`，`--rawfile` 搭配 process substitution 會原樣保留值，不經過磁碟：

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

bash 版最後一行必須輸出 `["Jwt:Key","SecretProtection:Key","Igdb:ClientId","Igdb:ClientSecret"]`。

### 切換紀錄

2026-11-02 完成切換（首個讀取 `api-config` 的 revision 是 `mycollection-api-00013-s5d`），之後移除了舊的三個 secret 與 IAM binding。更早的 revision 以環境變數引用已刪除的 secret，無法再啟動，不能作為回滾目標。

當時的順序與理由，日後改 template 引用的 secret 時仍適用：

- `image` 在 `ignore_changes` 裡，apply 會沿用線上的 image。新 template 依賴新程式時，必須先用 `Deploy production` 部署新 image，再 apply。
- apply 產生的新 revision **不會取得流量**：`traffic` 在 `ignore_changes` 裡，由 canary 腳本負責。請以 `status.traffic` 確認，而不是看 `spec.template`，再明確切換流量：

```powershell
gcloud run services update-traffic mycollection-api --project mycollection-504914 --region asia-east1 --to-revisions <revision>=100
```

輪替流程見 [production-operations.md](../../../docs/deployment/production-operations.md)。

The bucket has `prevent_destroy = true`. Removing it requires an explicit code change and a separately reviewed migration or retirement plan.
