# MyCollection Production 維運文件

- 用途：正式環境的座標、例行操作與已知缺口。日常維運看這份，決策背景看 [ADR-0011](../adr/0011-low-cost-production-on-cloud-run-and-atlas-free.md)，建置過程看 [cloud-run-production-plan.md](./cloud-run-production-plan.md)。
- 最後查證：2026-08-27，所有數值以 `gcloud` / `gh` 對正式環境實查為準，非由設定檔推論。
- **本檔案受 Git 追蹤，不得寫入任何連線字串、密碼、金鑰或 token。** 需要指涉秘密時只寫 Secret Manager 的 secret 名稱。

## 環境座標

| 項目 | 值 |
|---|---|
| GCP Project ID | `mycollection-504914` |
| GCP Project Number | `187833859763` |
| Project 名稱 | MyCollection Production |
| Region | `asia-east1` |
| API URL | https://mycollection-api-cswrakuenq-de.a.run.app |
| Web URL | https://mycollection-web-cswrakuenq-de.a.run.app |
| Atlas cluster | `mycollection-prod`（GCP `asia-east1`、Free tier） |
| Atlas project | `MyCollection Production` |
| Production database | `mycollection` |

> Atlas **cluster** 名為 `mycollection-prod`，其中的 **database** 名為 `mycollection`。
> 備份物件路徑前綴 `mycollection-prod/` 取自 cluster 名，不是 database 名。
> 建置計畫 Phase 4 工作 1 把 `mycollection-prod` 寫成 database，該處記載有誤。

### Cloud Run

| 服務 | 設定 |
|---|---|
| `mycollection-api` | `min=0`、`max=1`、cpu `1`、memory `512Mi`、cpu-throttling 開、startup-cpu-boost 開 |
| `mycollection-web` | `min=0`、`max=1`、cpu `1`、memory `256Mi` |

health endpoint 拆兩支：`/health/live`（不查外部依賴）與 `/health/startup`（等 Mongo indexes 與 seeding 完成）。兩者皆允許匿名讀取。

### 儲存

| Bucket | 用途 | 設定 |
|---|---|---|
| `mycollection-504914-media` | 使用者上傳媒體 | ASIA-EAST1、uniform access、public access **enforced**、無版本控管 |
| `mycollection-504914-backups` | 每日 mongodump | 同上，另有 lifecycle `age=30` 刪除、soft delete 保留 7 天 |
| `mycollection-504914-tfstate` | Terraform state | 同上，**版本控管開啟** |

三個 bucket 皆不可匿名列舉或直接讀取（2026-08-27 實測：列舉 `401`、直讀物件 `403`）。

### Artifact Registry

- `mycollection`（DOCKER）—— API 與 Web image
- `mycollection-backup`（DOCKER）—— 備份 image

### Secret Manager

只列名稱，內容不在本文件範圍：`mongo-connection-string`、`jwt-signing-key`、`secret-protection-key`、`igdb-client-secret`。

### 身分

| Service Account | 用途 |
|---|---|
| `mycollection-api-runtime@` | API 執行身分 |
| `mycollection-web-runtime@` | Web 執行身分 |
| `mycollection-task-invoker@` | Cloud Tasks 以 OIDC 呼叫 handler |
| `mycollection-backup-runner@` | 備份 Job 執行身分 |
| `mycollection-backup-scheduler@` | Cloud Scheduler 觸發備份 Job |
| `github-cloud-run-deployer@` | GitHub Actions 部署身分 |

GitHub 以 Workload Identity Federation 取得憑證，**無 JSON key**：

- Pool：`projects/187833859763/locations/global/workloadIdentityPools/github-actions`
- Provider：`.../providers/mycollection-production`

## 部署

正式環境只能由 GitHub Actions `workflow_dispatch` 發布，兩個 workflow 各自獨立：

- `Deploy production`（`.github/workflows/deploy-production.yml`）—— API
- `Deploy Web production`（`.github/workflows/deploy-web-production.yml`）—— Web

流程由 `.github/scripts/rollout-cloud-run.sh` 驅動：部署 0% 流量的 tagged revision → 對 tag URL 執行 `smoke-api.sh` → 切 40% 觀察 15 分鐘 → 通過才升 100%，失敗自動切回前一個 revision。

canary 判定失敗的條件是新 revision 出現 `severity>=ERROR` 或 `httpRequest.status>=500`。

### 授權分支綁在兩處

改變部署分支策略時，以下兩處必須**同時**修改，只改一處會讓 dispatch 直接 `skipped`：

1. WIF provider 的 `attribute_condition`（`infra/terraform/bootstrap/wif.tf`）
2. 兩個 workflow 的 `if: github.ref`

目前皆綁 `refs/heads/master`。

### Rollback

```bash
gcloud run services update-traffic mycollection-api \
  --region asia-east1 --to-revisions <前一個 revision>=100
```

revision 名稱由部署腳本以 `--revision-suffix` 指定，格式為 `run-<run_id>-<run_attempt>`。

### terraform apply 的注意事項

`api` / `web` 兩個 service 的 `image` 與 `traffic` 由 `ignore_changes` 排除，真實來源是 `gcloud run deploy`。`variables.tf` 中 `api_image` / `web_image` 的 default 是**刻意留下的失效佔位 digest**，不可當成設定值閱讀。

`backup_image` 相反：**terraform 是唯一來源，pin 必須指向實際存在的 image**。曾發生釘向不存在 image 長達兩週的事故（見計畫文件〈偏離 3〉）。`plan` 不會查 registry，apply 前請自行確認：

```bash
gcloud artifacts docker images describe <backup_image 完整參照>
```

**對 service template 的變更請走 workflow，不要用 `terraform apply`。** apply 會建立新 revision 並使其取得 100% 流量，繞過 canary gate（2026-08-27 因 IGDB 憑證上線而實際發生過一次）。

## 資料

### 每日備份

- Cloud Run Job `mycollection-mongo-backup`，SA `mycollection-backup-runner@`，timeout 3600s
- Cloud Scheduler `mycollection-mongo-backup-daily`，`0 2 * * *` Asia/Taipei（即 18:00 UTC）
- 輸出：`gs://mycollection-504914-backups/mycollection-prod/<UTC 時戳>/mongodump.archive.gz`
- 保留 30 天（lifecycle），另有 7 天 soft delete
- 成功寫入 `mongo_backup_completed` 結構化事件，失敗寫 `mongo_backup_failed` 並觸發告警

備份 image 的 entrypoint 會遮蔽 mongo tools 寫到 stderr 的 URI userinfo，保留 host 以便診斷。這是失敗路徑才會觸發的洩漏，備份成功時完全看不到。

手動觸發一次備份：

```bash
gcloud run jobs execute mycollection-mongo-backup --region asia-east1
```

### 還原演練

腳本：`infra/acceptance/restore-drill.ps1`（host 端編排）＋ `restore-drill-container.sh`（在備份 image 內執行）。

```powershell
pwsh -File infra/acceptance/restore-drill.ps1
```

演練會選出最新的非空 archive、產生 `mc-r-<UTC 時戳>-<8 位隨機值>` 的暫時庫、還原、比對 collection 涵蓋與索引建立情形。**production 不會被觸碰。**

三項刻意偏離 [runbook](./mongodb-backup-restore-runbook.md)，理由寫在腳本的 `.DESCRIPTION`：

1. 執行環境為本機 Docker，非 runbook 所寫的 secured environment。URI 由 stdin 進入容器 tmpfs，不落磁碟、不進 argv、不進環境變數。
2. **不使用 `--drop`**。production 連線字串釘住 `mycollection`，導致無法用同一份 config 讀取暫時庫，也就做不了「目標庫不存在」的前置檢查；與其補檢查再保留破壞性旗標，不如讓旗標消失。
3. 丟棄暫時庫需手動在 Atlas UI 執行。備份 image 內沒有 mongosh，而 mongosh 沒有 `--config`，連線字串只能進 argv 或環境變數 —— 兩者 runbook 都明文禁止。

**counts 比對是「並列顯示 + 標示漂移」而非等值判定。** archive 是快照，production 之後仍在寫入，counts 有差是正確行為；嚴格檢查的是 collection 涵蓋完整與索引是否建立。

演練後務必在 Atlas UI 丟棄腳本印出的暫時庫，並移除本機工作目錄。

## 監控與告警

| Alert Policy | 條件 | Channel |
|---|---|---|
| MyCollection MongoDB backup failed | `cloud_run_job` + `job_name=mycollection-mongo-backup` + `severity>=ERROR` | MyCollection backup failures |
| MyCollection Cloud Run 5xx | `cloud_run_revision` 上 api/web 的 `response_code_class=5xx`，5 分鐘對齊、`auto_close=1800s` | MyCollection service errors |

三個 email channel 皆 enabled：`MyCollection backup failures`、`MyCollection service errors`、`MyCollection budget email`。

Budget（`infra/terraform/bootstrap/budget.tf`）：

- TWD 150：50% / 90% / 100% 三段門檻
- TWD 300：100% 一段

> Cloud Run 5xx 告警的 `auto_close` 是必要的：`min=0` 的服務沒有流量時指標會缺值，缺值不觸發告警，但已開啟的 incident 需要能自行關閉，否則 scale-to-zero 之後會一直掛著。

## 例行維運

**每日**（自動）：備份 Job 依排程執行。失敗會寄信。

**每季**：執行一次還原演練（見上），記錄 archive 識別資訊、耗時與比對結果。

**收到備份失敗告警時**：檢查 execution 與 Cloud Logging，過程中不要複製環境變數、設定檔、Mongo URI 或憑證。修正根因後手動執行一次備份，確認產生新的非空 archive 再結案。若在 Cloud Logging 中發現遮蔽修正之前的失敗紀錄含有完整 URI，依 runbook〈Credential exposure in tool output〉輪替 Atlas 密碼。

## 驗收實證（2026-08-27）

| 項目 | 結果 |
|---|---|
| 登入 / CRUD / 公開分享 | 通過（`infra/acceptance/phase7-acceptance.ps1`） |
| Share Link 授權邊界 | 通過（範圍內可讀、範圍外拒絕，雙向） |
| 圖片跨 revision 存活 | 通過（2026-08-08 上傳的物件由 08-24 之後的 revision 讀取成功） |
| 匿名授權沒有擴權 | 通過（bucket 列舉 `401`、直讀 `403`、API `401`、非白名單 CORS 無 `Access-Control-Allow-Origin`） |
| 還原演練 | 通過（229 documents、6 collections、索引全建、3 秒） |
| IGDB 外部整合 | 通過（`POST api.igdb.com/v4/games` → 200） |

## 已知缺口

| 項目 | 說明 |
|---|---|
| Atlas 完全不由 Terraform 管理 | `infra/terraform` 只有 `hashicorp/google` provider，沒有 Atlas provider。計畫 Phase 6 宣告的「Terraform 管理 Atlas project、Free cluster、network access list」從未實作 —— Atlas 目前 100% 手動 |
| Atlas Network Access 為 `0.0.0.0/0` | ADR-0011 已知並接受的取捨，補償控制為 TLS、專用 DB user、強密碼與 Secret Manager |
| Steam / PSN 憑證未經實際外部呼叫驗證 | 憑證已輸入但未觸發同步。憑證存入不等於可用 —— Phase 4 清除舊憑證的理由正是它們無法用 production key 解密 |
| Cloud Tasks 在正式環境零執行 | 30 天內無任何 task handler 呼叫或佇列日誌。實作已部署但路徑從未被真實流量走過；重試、terminal failure 與手動重跑皆未驗證 |
| 孤兒 revision 與 traffic tag | api / web 各有數個舊命名殘骸與指向舊 revision 的 tag，未清理 |
| API 回應缺 `Vary: Origin` | 目前無共用快取層，不構成實際風險，但屬正確性缺口 |
| 備份桶實際可回溯範圍小於 30 天 | 2026-08-24 有一次手動刪除，移除了 6 份 archive。lifecycle 設定本身正確 |

## 常用查詢

```bash
# 目前流量分配
gcloud run services describe mycollection-api --region asia-east1 --format="value(status.traffic)"

# 近期備份執行
gcloud run jobs executions list --job mycollection-mongo-backup --region asia-east1 --limit 10

# 備份完成事件
gcloud logging read 'resource.type="cloud_run_job" AND jsonPayload.event="mongo_backup_completed"' --limit 10

# 桶內實際保留的 archive
gcloud storage ls -l "gs://mycollection-504914-backups/mycollection-prod/**"

# API 近期 5xx
gcloud logging read 'resource.type="cloud_run_revision" AND resource.labels.service_name="mycollection-api" AND httpRequest.status>=500' --freshness=24h
```

> `gcloud alpha monitoring` 在本機未安裝。查詢 alert policy 與 notification channel 請直接打 Monitoring REST API，並以 PowerShell 的 `ConvertFrom-Json` 解析（本機無 `jq`）。
