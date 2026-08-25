# MyCollection Cloud Run Production 部署計畫

- 狀態：Phase 0–6 已完成並經正式環境實測；**Phase 7 Production Acceptance 尚未開始**
- 日期：2026-08-08（最後回寫：2026-08-25）
- 決策依據：[ADR-0011](../adr/0011-low-cost-production-on-cloud-run-and-atlas-free.md)
- 各 Phase 的實際執行結果、與計畫的偏離、未通過的 Gate：見文末〈執行紀錄〉

> 以下 Phase 0–7 的內容為 2026-08-08 訂定的原始計畫，**刻意不隨執行結果改寫**，以保留「當初打算怎麼做」的紀錄。
> 實際做出來的樣子與差異一律記在〈執行紀錄〉，各 Phase 開頭的引言則標示該 Phase 的現況。

## 完成定義

- Angular Web 與 ASP.NET Core API 分別在 `asia-east1` 的 Cloud Run 運行。
- Production Atlas、GCS、Cloud Tasks 與備份均不依賴開發機或 Cloud Run 本機磁碟。
- Production 只能由 GitHub Actions `workflow_dispatch` 發布，且 canary gate 通過後才取得 100% 流量。
- 可從前一個 Cloud Run revision 回復服務，也能從最近一次 production dump 復原資料。
- Repository、GitHub Actions logs、Terraform variables/state output 均不洩漏 secrets。

## 執行狀態總覽

> 更新於 2026-08-25，依據 GitHub Actions run 紀錄與 `gcloud` 對正式環境的查詢。

| Phase | 執行 | Gate | 依據 |
|---|---|---|---|
| 0 Preflight 與 Bootstrap | ✅ 完成 | ✅ 通過 | 2026-08-09 建立；bootstrap 於 08-24 另 apply 兩次（`logging.viewer` 綁定、WIF 分支改 `master`） |
| 1 API／Web 基線 | ✅ 完成 | ✅ 通過 | 現行 revision `mycollection-api-run-32749490889-1`／`mycollection-web-run-32749490889-1` |
| 2 GCS Media Storage | ✅ 完成 | ⚠️ 部分 | 「revision 更換後圖片仍可讀取」未在正式環境實測 → 併入 Phase 7-4 |
| 3 Cloud Tasks | ✅ 完成 | ⚠️ 部分 | 重送冪等、五次 attempts 後手動重跑未在正式環境實測 → 併入 Phase 7-3 |
| 4 Atlas／Secrets／資料搬移 | ✅ 完成 | ⚠️ 部分 | smoke tests 已由 canary 涵蓋；Provider 憑證重新輸入尚未進行 → 併入 Phase 7-2 |
| 5 Backup 與 Restore Drill | ⚠️ 部分 | ❌ 未通過 | 每日備份確實在跑；log 回顯連線字串已於 2026-08-25 修正（待下次部署生效）；**restore drill 仍從未執行** |
| 6 Terraform 與 Workflow | ✅ 完成 | ✅ 通過 | run `32749490889`（2026-08-24）首次完整成功 |
| 7 Production Acceptance | ❌ 未開始 | — | 自 2026-08-09 起零進度 |

## Phase 0：Preflight 與 Bootstrap

> **現況（2026-08-25）**：✅ 完成。Project `mycollection-504914`／`asia-east1`，WIF 無 JSON key。
> 2026-08-24 另 apply 兩次 bootstrap：canary 觀察期需要的 `roles/logging.viewer`，以及把 `github_branch` 由 `refs/heads/deploy` 改為 `refs/heads/master`。

### 工作

1. 建立獨立 GCP Project，連結既有 billing account，預設 region 設為 `asia-east1`。
2. 建立 Terraform state bucket：private、public access prevention、versioning、最小 IAM。
3. 建立 GitHub Workload Identity Pool／Provider 與 deploy service account，不建立 JSON key。
4. 設定 US$5 的 50%／90%／100% budget notifications，以及 US$10 高優先通知。
5. 以唯讀 `dbStats` 驗證 development data＋indexes 小於 350 MB；否則停止並改用 Atlas Flex。
6. 盤點使用者上傳媒體與可由外部 Provider 重建的衍生圖片。

### Gate

- Project、billing、state backend 與 WIF 可由最小權限帳號存取。
- Development DB 容量通過 350 MB Gate。
- 沒有 secret 出現在 Terraform plan、workflow log 或 tracked files。

## Phase 1：API 與 Web 的 Cloud Run 基線

> **現況（2026-08-25）**：✅ 完成。`min=0`／`max=1`、health 拆兩支、runtime API config 皆已在正式環境運行。

### API

1. 將 production settings 全部改由環境變數／Secret Manager 注入。
2. 新增精確 CORS allowlist，不允許 wildcard origin。
3. 啟用可信任 proxy 的 forwarded headers。
4. 拆分 `/health/live` 與 `/health/startup`：startup 等 Mongo indexes 與 seeding 完成；liveness 不查外部依賴。
5. 保持 API container 監聽 Cloud Run 指定 port，設定 `min=0`、`max=1`。
6. 為 application JWT、公開 Share Link 與 Cloud Tasks OIDC 建立彼此獨立的授權邊界。

### Web

1. 移除 nginx 對 Compose DNS `api:8080` 的 production 依賴。
2. 由 container startup environment 產生 runtime API config。
3. 保留 SPA fallback，讓 Angular routes 直接重新整理仍回傳 `index.html`。
4. 明確設定 Cloud Run container port 與靜態資源 cache headers。

### Gate

- Production builds 成功。
- Web 能由 runtime config 呼叫指定 API revision URL。
- 非 allowlisted origin 的 CORS preflight 被拒絕。
- Startup 與 liveness probes 的失敗語意有自動化測試。

## Phase 2：GCS Media Storage

> **現況（2026-08-25）**：✅ 實作完成，⚠️ Gate 未全數實測。私有 bucket、owned／share-scoped 兩條授權路徑已上線；
> 「Cloud Run revision 更換後圖片仍可讀取」與「匿名授權沒有擴權」留待 Phase 7-4 在正式環境驗。

### 工作

1. 讓 `StorageOptions` 實際選擇 Local 或 GCS provider；development 保留 Local。
2. Production bucket 啟用 uniform bucket-level access 與 public access prevention。
3. API service account 只取得必要 object 權限。
4. API 串流支援 cache headers、content type、取消請求與不存在物件的正確回應。
5. Authenticated media endpoint 驗 item ownership；public media endpoint 驗 Share Link 與 item 關係。
6. 建立一次性 migration，僅上傳不能從外部來源重建的使用者媒體，並可安全重跑。

### Gate

- Bucket 不能匿名列舉或直接讀取。
- 未授權 item ID 無法取得圖片；有效 Share Link 只能取得其範圍內圖片。
- Cloud Run revision 更換後圖片仍可讀取。

## Phase 3：Cloud Tasks Reliable Work

> **現況（2026-08-25）**：✅ 實作完成，⚠️ Gate 未全數實測。原子 claim + lease + 五次 attempts 已上線；
> 重送冪等、terminal failure 後由設定頁手動重跑，留待 Phase 7-3 在正式環境驗。

### 工作

1. 將同步／補完 dispatch 抽成可由 development in-process 與 production Cloud Tasks 實作的 boundary。
2. 先持久化 Sync Job／operation ID，再 enqueue task。
3. Handler 以 operation ID 保證 idempotency，重複 delivery 不得重複建立或覆寫品項。
4. 驗證 Cloud Tasks OIDC 的 audience 與 service account；拒絕一般 JWT 與匿名請求。
5. 設定最多五次 attempts、exponential backoff 與 terminal failure 狀態。
6. 圖片下載維持可重建語意，不阻塞主要 Sync Job 的可靠性。

### Gate

- 重送相同 task 不改變最終結果。
- API revision 中止後，未完成 task 能由新 revision 重試。
- 第五次失敗後 Sync Job 顯示 Failed，且設定頁能手動重跑。

## Phase 4：Atlas、Secrets 與初次資料搬移

> **現況（2026-08-25）**：✅ 完成，⚠️ Gate 部分未驗。資料已搬入 `mycollection-prod`，舊 JWT 全失效。
> Gate 的 smoke tests（health／登入／CRUD／GCS／Share Link）已由 canary 在每次部署自動執行並通過；
> 但「Provider credentials 由使用者重新輸入」至今未做 —— 這是 Phase 7-2 的前置，也是 PSN 真實 NPSSO 的首次驗證機會。

### 工作

1. 建立 GCP `asia-east1` Atlas Free cluster、`mycollection-prod` 與 production 專用 DB user。
2. Network Access 明確建立 `0.0.0.0/0` entry，並在 Terraform／ADR 中保留風險註記。
3. 產生全新的 JWT signing key、SecretProtection key 與 Mongo password，寫入 Secret Manager。
4. 暫停 development writes，執行一致性 dump 並 restore 至 production。
5. 比對 collections、document counts 與 indexes。
6. 清除無法用 production key 解密的 Provider credentials；由使用者重新輸入。
7. 保留 development DB 至少 30 天，不建立雙向同步。

### Gate

- Production 登入、品項 CRUD、同步／補完入口與公開 Share Link smoke tests 通過。
- 所有舊 JWT 無效；新登入可取得有效 token。
- Provider secrets 未從 development 複製成可用的 production secret。

## Phase 5：Backup 與 Restore Drill

> **現況（2026-08-25）**：⚠️ 部分完成，**Gate 未通過**。每日排程備份確實在跑（明細見〈執行紀錄〉）。
> Gate 的兩項關鍵中，②「備份與 restore logs 不包含 Mongo URI 或 credentials」原本實測不成立 —— 連線失敗時 `mongodump`
> 會把它從 `--config` 讀進來的完整連線字串（含密碼）寫進 stderr 並落入 Cloud Logging，2026-08-16 發生過一次實例。
> **已於 2026-08-25 在 `infra/backup/entrypoint.sh` 修正**（遮蔽 URI userinfo，保留 host 與 exit code），下次部署備份 image 後生效；
> restore 路徑的對應做法寫進 runbook 的〈Credential exposure in tool output〉。
> ①「每季還原到暫時 database」仍從未執行 —— 這是 Phase 5 Gate 目前唯一未達成的項目。
>
> 附帶收穫：2026-08-16 那次失敗**確實寄達了告警信**（使用者信箱佐證），
> 這是 Phase 7-6「backup failure 通知可送達」目前唯一的端對端實證，不需另外製造失敗來測。

### 工作

1. 建立含 MongoDB Database Tools 的專用 backup image 與 Cloud Run Job。
2. Cloud Scheduler 每日觸發 job；dump 以壓縮 archive 寫入獨立 private GCS bucket。
3. Bucket lifecycle 保留 30 天，service account 只具必要 object 權限。
4. 備份失敗寫入 Cloud Logging 並觸發通知。
5. 撰寫 restore runbook；每季還原到暫時 database，驗證 counts 與 indexes 後再清理。

### Gate

- 可由最近一次 archive 在 4 小時內完成受控還原。
- 還原演練不覆寫 production database。
- 備份與 restore logs 不包含 Mongo URI 或 credentials。

## Phase 6：Terraform 與 Production Workflow

> **現況（2026-08-25）**：✅ 完成並實測。2026-08-24 的 run `32749490889` 為 canary workflow 首次完整成功。
> 注意 `api` / `web` 的 image 與 traffic 由 `ignore_changes` 排除，真實來源是 `gcloud run deploy`（`.github/scripts/rollout-cloud-run.sh`），
> `variables.tf` 中 `api_image` / `web_image` 的 default 是**刻意留下的失效佔位 digest**；`backup_image` 相反，terraform 是唯一來源。

### Terraform ownership

- GCP APIs、IAM、Artifact Registry、GCS、Secret Manager metadata、Cloud Run、Cloud Tasks、Cloud Scheduler、backup job、budget notifications。
- Atlas project、Free cluster、network access list 與不會讓明文秘密落入 state 的 resources。
- 若 Atlas DB user password 無法避免進入 state，改由一次性安全 bootstrap 建立。

### `workflow_dispatch` pipeline

1. 執行 backend／frontend tests 與 production builds。
2. 建立 Web/API images，以 commit SHA 標記並推至 Artifact Registry。
3. 部署 API 0% traffic tagged revision，對 tag URL 執行 health、登入、Mongo CRUD、GCS、Share Link smoke tests。
4. API 切入 40% traffic，以 synthetic requests 觀察 15 分鐘；失敗自動切回前一 revision，成功升至 100%。
5. 以相同流程部署 Web；Web runtime config 指向穩定 API service URL。
6. 記錄 deployed commit、revision names 與驗證結果，供手動 rollback workflow 使用。
7. Artifact Registry cleanup policy 保留近期與目前仍被 revision 使用的 images。

### Canary failure criteria

- 任一關鍵 smoke test 失敗。
- 新 revision 出現非預期 5xx。
- Startup／liveness probe failure。
- Mongo、GCS 或 task handler 出現部署引入的授權／設定錯誤。

### Gate

- 非 `workflow_dispatch` 事件不能部署 production。
- Canary 失敗時前一 revision 恢復 100% traffic。
- Rollback 不變更 MongoDB，且前一 revision 能讀取部署後資料。

## Phase 7：Production Acceptance

> **現況（2026-08-25）**：❌ 尚未開始，自 2026-08-09 起零進度。先前被連續的部署失敗擋住，**該阻塞已於 2026-08-24 解除**。
> 第 5 項（restore drill）同時也是 Phase 5 未通過的 Gate。
> 第 6 項的「backup failure 通知」已由 2026-08-16 的真實失敗實證送達（見 Phase 5），剩 budget 與 Cloud Run errors 兩條通知待驗。

1. 驗證登入、品項 CRUD、篩選、精選與公開 Share Link。
2. 驗證 Steam／PSN／IGDB credentials 重新輸入與實際外部整合。
3. 驗證同步／補完成功、重試、terminal failure 與手動重跑。
4. 驗證使用者圖片跨 revision 存活，匿名圖片授權沒有擴權。
5. 執行一次 production backup，並以獨立 database 完成 restore drill。
6. 確認 budget、backup failure 與 Cloud Run errors 的通知可送達。
7. 將實際 service URLs、Project ID、Atlas project/cluster 名稱與操作 runbook 存入不含秘密的維運文件。

## 明確不在本計畫內

- 自訂網域、Load Balancer、CDN 或多區域部署。
- Atlas Dedicated、Private Service Connect、VPC peering 或固定 outbound IP。
- 長期 staging environment、automatic deployment on push，或 production canary 以外的常駐測試環境。
- 自動 database rollback；任何破壞向後相容性的資料變更必須另立決策。

---

# 執行紀錄

> 本節於 2026-08-25 一次補齊 2026-08-09 → 2026-08-24 的執行過程。
> 計畫本文自 2026-08-09 起零回寫，期間的成功、失敗與偏離都只散落在 git log、GitHub Actions 與 GCP 狀態裡，此節即為收斂。
> 所有數字皆以 `gh` / `gcloud` 實查為準，非由 commit message 推論。

## 時間線

| 日期 | 事件 |
|---|---|
| 2026-08-09 | Phase 0–6 主體交付：bootstrap + WIF、runtime API config、health 拆兩支、私有 GCS、Cloud Tasks、備份 Job、canary workflow。當日三次部署 run 全數失敗，canary 未跑完 |
| 2026-08-10 | canary revision 改用 `run_id`-`run_attempt`（解可重試）、圖片無限重打修正；`deploy` 併回 `master`（PR #9）。Web 部署 run 仍失敗 |
| 2026-08-11 | 備份 image 瘦身（拔除 Cloud SDK 與 python3）並重釘 digest；canary 補 `logging.viewer`。**此交付自此分裂成兩半長達兩週** |
| 2026-08-11 → 08-23 | 無部署活動。每日備份持續執行 |
| 2026-08-24 | 分支分裂收斂、WIF 綁定改 `master`、Testcontainers 升版解 GHSA；**run `32749490889` 首次完整成功**，正式環境切至 `master` HEAD |
| 2026-08-25 | 本節回寫 |

## 部署 run 紀錄

| Run | 日期 (UTC) | 分支 | 結果 |
|---|---|---|---|
| `31321651191` | 2026-08-09 15:38 | `deploy` | ❌ failure |
| `31322152096` | 2026-08-09 15:49 | `deploy` | ❌ failure |
| `31323327321` | 2026-08-09 16:15 | `deploy` | ❌ failure |
| `31430023844` | 2026-08-10 20:38 | `deploy` | ❌ failure（Deploy Web production） |
| `32748831264` | 2026-08-24 16:05 | `master` | ⏭️ skipped（workflow 的 `if: github.ref` 仍綁 `deploy`） |
| `32749059583` | 2026-08-24 16:08 | `master` | ❌ failure |
| `32749490889` | 2026-08-24 16:12–16:47 | `master` | ✅ **success**，`workflow_dispatch`，HEAD `5814133` |

成功那次的 canary 走完全程：api / web 各自 0% tagged revision → smoke tests → 40% → 15 分鐘觀察 → 100%。
四個歷史阻塞經此 run 實測解除：WIF 分支（OIDC 交換成功）、traffic tag 長度上限（`candidate-run-32749490889-1` 建得起來）、
revision 名可重試（`run-32749490889-1`）、`logging.viewer`（觀察期的 `logging read` 未中斷）。

## 備份實跑紀錄

Cloud Run Job `mycollection-mongo-backup`，2026-08-09 → 08-24 共 19 次 execution（17 次每日排程 + 2 次手動）。

- **兩次失敗**：`2qr87`（08-09，初次建立時）、`jd82q`（08-16，Atlas 端 TLS 錯誤導致 server selection 逾時，重試一次仍失敗）。其餘全數 `SUCCEEDED`。
- **瘦身版 image 的實際上線時間是 2026-08-11，不是 08-18**：08-10 的 `4qdh9` 仍跑舊 image `5f04d31…`，08-11 00:42 的手動 execution `pdg9t` 是瘦身版 `0e1a7b9…` 首跑，此後每日排程皆為 `0e1a7b9…`。
- 新的 metadata server token 路徑、awk urlencode、非 root 權限，皆已在正式排程中連續驗證兩週。
- **08-16 那次失敗的告警信確實送達**（使用者信箱佐證：`[ALERT - No severity] MyCollection MongoDB backup failed for Cloud Run Job`）。
  `infra/terraform/runtime/backups.tf` 的 alert policy 條件為 `severity>=ERROR`，entrypoint 的失敗路徑走 stderr 即為 ERROR，鏈路完整。
  2026-08-25 的遮蔽修正不影響這條鏈路 —— 遮蔽後的輸出仍走 stderr。

## 與計畫的偏離

1. **`api` / `web` 的 image 不由 Terraform 管**。Phase 6 的「Terraform ownership」列了 Cloud Run，但實際上這兩個 service 的 image 與 traffic 以 `ignore_changes` 排除，交給 `gcloud run deploy`。這是刻意的設計（canary 需要在單次部署內操作 traffic），代價是 `variables.tf` 裡的 `api_image` / `web_image` default 變成看起來像設定的失效佔位。**備份 Job 相反，terraform 是唯一來源，pin 必須正確。**
2. **授權分支的綁定有兩處，不是一處**。WIF 的 `attribute_condition` 與兩個 workflow 的 `if: github.ref` 各綁一次；2026-08-24 只改前者，導致首次 dispatch 直接 `skipped`。改分支策略時兩處必須同時改。
3. **`backup_image` 曾釘向不存在的 image 長達兩週**。瘦身 Dockerfile 進了 `master`、對應的 digest pin 卻留在 `deploy`，兩半各自看起來完整。期間對 `infra/terraform/runtime` 執行 `apply` 會打壞正常運作中的每日備份。已釘回線上實跑的 digest。
4. **pipeline 久未執行會累積時間炸彈**。`TreatWarningsAsErrors` + NuGetAudit 之下，一則新的傳遞相依公告就能讓 restore 失敗、擋住整條部署，而程式碼一行未改（本次為 `SSH.NET 2025.1.0` 的 GHSA-q939-rpr3-3284，經 Testcontainers 傳遞）。判準：上游已修就升上游，上游未修才覆寫 pin。

## 未通過的 Gate 與待辦

| 項目 | 來源 | 狀態 |
|---|---|---|
| 季度 restore drill 從未執行 | Phase 5 Gate | 🔴 待執行，同為 Phase 7-5 |
| 備份失敗時 log 回顯連線字串（含密碼） | Phase 5 Gate | ✅ 2026-08-25 已修（entrypoint 遮蔽 URI userinfo）；**待重建備份 image 並部署才生效**。既有的歷史 log entry 需另行決定是否輪替憑證 |
| Provider credentials 尚未由使用者重新輸入 | Phase 4 工作 6 | 🟡 Phase 7-2 的前置；PSN 真實 NPSSO 至今未驗證 |
| `backup_image` 缺 apply 前的 digest 存在性檢查 | 偏離 3 的後續 | 🟡 格式 `validation` 擋不掉「digest 不存在」，`plan` 不查 registry |
| 孤兒 revision 未清理 | Phase 6 工作 7 | 🟡 `mycollection-api-sha-3a8bc116…`（已 False）、`mycollection-web-web-9a85fb9-1` 等舊命名殘骸 |
| Phase 7 全部七項 | Phase 7 | 🔴 未開始 |
