# MyCollection Cloud Run Production 部署計畫

- 狀態：Phase 0–6 已完成；**Phase 7 Production Acceptance 七項中五項完成**，7-2 部分完成、7-3 未開始
- 日期：2026-08-08（最後回寫：2026-08-27）
- 決策依據：[ADR-0011](../adr/0011-low-cost-production-on-cloud-run-and-atlas-free.md)
- 日常維運座標與操作步驟：[production-operations.md](./production-operations.md)
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

> 更新於 2026-08-27，依據 GitHub Actions run 紀錄與 `gcloud` 對正式環境的查詢。

| Phase | 執行 | Gate | 依據 |
|---|---|---|---|
| 0 Preflight 與 Bootstrap | ✅ 完成 | ✅ 通過 | 2026-08-09 建立；bootstrap 於 08-24 另 apply 兩次（`logging.viewer` 綁定、WIF 分支改 `master`） |
| 1 API／Web 基線 | ✅ 完成 | ✅ 通過 | 非白名單 origin 的 CORS preflight 無 `Access-Control-Allow-Origin`，08-27 實測補上 Gate 最後一項 |
| 2 GCS Media Storage | ✅ 完成 | ✅ 通過 | 08-27 實測：08-08 上傳的物件由 08-24 之後的 revision 讀取成功；匿名列舉 `401`、直讀 `403`、share 範圍外的圖片被拒 |
| 3 Cloud Tasks | ✅ 完成 | ❌ 未通過 | **正式環境 30 天零執行**：無任何 task handler 呼叫或佇列日誌。重送冪等、五次 attempts 後手動重跑皆未驗證 → Phase 7-3 |
| 4 Atlas／Secrets／資料搬移 | ✅ 完成 | ⚠️ 部分 | smoke tests 已由 canary 涵蓋；IGDB 憑證 08-27 完成重新輸入並實際呼叫成功，Steam／PSN 已輸入但未觸發 → Phase 7-2 |
| 5 Backup 與 Restore Drill | ✅ 完成 | ✅ 通過 | **restore drill 於 2026-08-27 首次執行並通過**（229 documents／6 collections／索引全建／3 秒）；URI 遮蔽修正已於 08-25 生效並連續兩次排程實跑 |
| 6 Terraform 與 Workflow | ✅ 完成 | ✅ 通過 | run `32749490889`（2026-08-24）首次完整成功 |
| 7 Production Acceptance | ⚠️ 部分 | ⚠️ 部分 | 七項中 7-1／7-4／7-5／7-6／7-7 完成；7-2 僅 IGDB 通過；7-3 未開始 |

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

> **現況（2026-08-27）**：✅ 完成，Gate 全數通過。`min=0`／`max=1`、health 拆兩支、runtime API config 皆已在正式環境運行。
> Gate 的「非 allowlisted origin 的 CORS preflight 被拒絕」於 08-27 補上實證：非白名單 origin 得到 `204` 但**回應不含
> `Access-Control-Allow-Origin`**，瀏覽器端即阻擋；白名單 origin 則正常帶回該 header。
> 附帶發現：兩者皆缺 `Vary: Origin`，目前無共用快取層故不構成風險，列為待辦。

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

> **現況（2026-08-27）**：✅ 完成，Gate 全數通過。
> 「revision 更換後圖片仍可讀取」由既有資料實證：media bucket 的 51 個物件全部建立於 2026-08-08～08-10，
> 而現行 revision 建立於 08-24 —— 用現行 revision 讀得到 08-08 的圖，即為跨 revision 存活，不需製造新資料。
> 「匿名授權沒有擴權」實測：匿名列舉 bucket `401`、直讀物件 `403`、API `/media` `401`。
> share scope 邊界改以自帶 fixture 的雙向測試驗證（範圍內可讀、範圍外拒絕）——
> 先前只驗拒絕的單向斷言證明不了任何事：一個對所有路徑都回 404 的壞掉端點也會通過。

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

> **現況（2026-08-27）**：✅ 實作完成，**❌ Gate 未通過**。
> 2026-08-27 查證：正式環境 30 天內**沒有任何** task handler 呼叫，也沒有任何 `cloud_tasks_queue` 佇列日誌。
> 佇列 `mycollection-ingestion` 狀態 `RUNNING`、`maxAttempts: 5` 設定都在，但這條路徑從未被真實流量走過。
> 「實作已部署」與「路徑已驗證」是兩回事，此處先前記為 ✅ 完成是把前者當成了後者。
> IGDB 走的是同步 enrich、不經佇列，因此**光靠 IGDB 永遠驗不到本 Phase** ——
> 解鎖條件是觸發一次 Steam 或 PSN 同步。

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

> **現況（2026-08-27）**：✅ 完成，⚠️ Gate 部分未驗。舊 JWT 全失效，smoke tests 已由 canary 每次部署自動執行並通過。
> Provider credentials 已於 08-27 由使用者重新輸入三組，但只觸發了 IGDB：
> `POST https://api.igdb.com/v4/games` 回 200（164.98ms），圖片下載與 retry policy 均正常，
> 這是正式環境**第一個**經端對端驗證的外部整合。
> **Steam 與 PSN 已輸入但未觸發，因此仍未驗證** —— 憑證存得進去不等於可用：
> 本 Phase 工作 6 清除舊憑證的理由，正是它們無法用 production key 解密。
>
> 另：工作 1 把 `mycollection-prod` 寫成 database 是**記載錯誤**。`mycollection-prod` 是 Atlas **cluster** 名，
> database 名為 `mycollection`；備份物件路徑前綴取自 cluster 名。原始計畫刻意不改寫，錯誤記於此。

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

> **現況（2026-08-27）**：✅ 完成，**Gate 全數通過**。自 2026-08-09 起未通過的最後一項已於本日關閉。
>
> ①「每季還原到暫時 database」**於 2026-08-27 首次執行並通過**：229 documents、6 個 collection、
> 索引全數重建、耗時 3 秒（Gate 上限 4 小時），寫入的是暫時庫，production 未被觸碰。
> 腳本為 `infra/acceptance/restore-drill.ps1` 與 `restore-drill-container.sh`，三項對 runbook 的刻意偏離見〈偏離 7〉。
>
> ②「備份與 restore logs 不包含 Mongo URI 或 credentials」的遮蔽修正**已生效**：
> 備份 Job 現行 image 為 `sha256:258a4bad`（tag `e37b39d`），08-25 與 08-26 兩次排程執行皆跑在此 image 上且成功。
> 08-25 當時記為「待下次部署生效」，實際上 terraform apply 已完成，該記載已過期。
> 但需注意：本次還原演練走的是**成功路徑**，mongo tools 只在連線失敗時才回顯 URI，
> 因此 restore 側的遮蔽**尚未被真實失敗路徑驗證**，目前依據是該函式與 `entrypoint.sh` 逐字相同、而後者已在 production 驗證過。
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

> **現況（2026-08-27）**：⚠️ 七項中五項完成。本 Phase 於 2026-08-27 開始執行，當日完成 7-1、7-4、7-5、7-6、7-7。
>
> | 項目 | 狀態 | 依據 |
> |---|---|---|
> | 7-1 登入／CRUD／篩選／精選／公開分享 | ⚠️ API 層通過 | `infra/acceptance/phase7-acceptance.ps1` 15 項全數 PASS；篩選與精選的**版面語意屬 UI 行為**（ADR-0006／0007／0009），仍待人工確認 |
> | 7-2 三個 provider 憑證與外部呼叫 | ⚠️ 僅 IGDB | IGDB 端對端通過；Steam／PSN 已輸入未觸發 |
> | 7-3 同步重試與手動重跑 | ❌ 未開始 | 依賴 7-2 的 Steam／PSN；Cloud Tasks 正式環境零執行 |
> | 7-4 圖片跨 revision／匿名授權 | ✅ 通過 | 見 Phase 2 現況 |
> | 7-5 restore drill | ✅ 通過 | 見 Phase 5 現況 |
> | 7-6 三條通知 | ⚠️ 實作補齊 | backup failure 已由 08-16 真實失敗實證；**Cloud Run errors 告警先前根本不存在**，08-27 補實作（見〈偏離 5〉）；budget 與 5xx 兩條尚未被真實事件觸發 |
> | 7-7 維運文件 | ✅ 完成 | [production-operations.md](./production-operations.md) |

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
| 2026-08-25 | 本節回寫；URI 遮蔽修正與備份 image 重新 pin 一併落地並 apply |
| 2026-08-27 | **Phase 7 開始執行**。補上從未實作的 Cloud Run 5xx 告警；IGDB 憑證上線並實證外部呼叫；7-1／7-4 驗收腳本全數通過；**restore drill 首次執行並通過**，關閉 Phase 5 最後一項 Gate；維運文件建立 |

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
5. **Atlas 完全不由 Terraform 管理**。Phase 6 的「Terraform ownership」列了「Atlas project、Free cluster、network access list」，但 `infra/terraform` 的 `required_providers` 只有 `hashicorp/google`，**沒有任何 Atlas provider**。Atlas 目前 100% 手動維護。同一節還宣告了 Cloud Run，實際情形見偏離 1 —— 這一節整體上是「打算怎麼分工」而非「實際怎麼分工」。
6. **`terraform apply` 會繞過 canary gate**。2026-08-27 為了讓 IGDB 憑證進入 API service template 而執行 apply，Cloud Run 隨即建立新 revision 並使其取得 100% 流量，完全沒有經過 smoke test 與 15 分鐘觀察期 —— 而「canary gate 通過後才取得 100% 流量」是〈完成定義〉的條目之一。同次 apply 也讓 Web 產生了一個新 revision，但 Web 的流量未跟著移動，兩者不對稱的原因未查明。**結論：對 service template 的變更應走 workflow，不要用 apply。**
7. **restore drill 對 runbook 有三項刻意偏離**，理由記在 `infra/acceptance/restore-drill.ps1` 的 `.DESCRIPTION`：(a) 執行環境為本機 Docker 而非 runbook 所寫的 secured environment，URI 走 stdin 進入容器 tmpfs；(b) **不使用 `--drop`** —— production 連線字串釘住 `mycollection`，導致無法用同一份 config 讀取暫時庫（mongo tools 拒絕 URI 與 `--db` 指向不同資料庫），因此做不了「目標庫不存在」的前置檢查，與其補檢查再保留破壞性旗標，不如讓旗標消失；(c) 丟棄暫時庫改為手動 —— 備份 image 內沒有 mongosh，而 mongosh 沒有 `--config`，連線字串只能進 argv 或環境變數，兩者 runbook 都明文禁止。
8. **budget 門檻的幣別與金額與計畫不符**。Phase 0 工作 4 寫的是 US$5（50%／90%／100%）與 US$10 高優先，實際建立的是 **TWD 150**（三段）與 **TWD 300**（一段）。金額量級相近，但計畫文字未回寫。

## 未通過的 Gate 與待辦

> 更新於 2026-08-27。

| 項目 | 來源 | 狀態 |
|---|---|---|
| 季度 restore drill 從未執行 | Phase 5 Gate | ✅ **2026-08-27 首次執行並通過**（229 documents／6 collections／索引全建／3 秒）。下次到期 2026-11 |
| 備份失敗時 log 回顯連線字串（含密碼） | Phase 5 Gate | ✅ 已生效。08-25 與 08-26 兩次排程執行皆跑在含遮蔽的 `sha256:258a4bad` 上。**惟 restore 側的遮蔽尚未被真實失敗路徑驗證**。既有的歷史 log entry 仍需決定是否輪替憑證 |
| Cloud Tasks 正式環境零執行 | Phase 3 Gate | 🔴 30 天內無 task handler 呼叫、無佇列日誌。重送冪等、五次 attempts 後手動重跑皆未驗證。解鎖條件：觸發一次 Steam 或 PSN 同步 |
| Steam／PSN 憑證未經實際外部呼叫驗證 | Phase 4 工作 6／Phase 7-2 | 🟡 已輸入未觸發。IGDB 已於 08-27 完成端對端驗證 |
| 篩選與精選的 UI 語意未人工確認 | Phase 7-1 | 🟡 API 層已通過；版面語意屬 UI 行為（ADR-0006／0007／0009），驗收腳本不涵蓋 |
| budget 與 Cloud Run 5xx 通知未被真實事件觸發 | Phase 7-6 | 🟡 政策與 channel 均已上線且 enabled，但兩條皆未實際送達過 |
| Atlas 完全不由 Terraform 管理 | 偏離 5 | 🟡 需決定是否納管，或正式把 Atlas 標為手動維運範圍 |
| `terraform apply` 繞過 canary gate | 偏離 6 | 🟡 已知行為，暫以「service template 變更走 workflow」的紀律規避；Web 流量未跟隨移動的原因未查明 |
| `backup_image` 缺 apply 前的 digest 存在性檢查 | 偏離 3 的後續 | 🟡 格式 `validation` 擋不掉「digest 不存在」，`plan` 不查 registry |
| 孤兒 revision 未清理 | Phase 6 工作 7 | 🟡 `mycollection-api-sha-3a8bc116…`、`mycollection-web-web-9a85fb9-1` 等舊命名殘骸，另有 08-27 apply 產生的自動命名 revision |
| 備份桶實際可回溯範圍小於 30 天 | 本次查證 | 🟡 2026-08-24 14:55–14:57 UTC 有一次手動刪除，移除 6 份 archive（hard delete 排程 08-31）。lifecycle 設定本身正確 |
| API 回應缺 `Vary: Origin` | 本次查證 | 🟡 目前無共用快取層，不構成實際風險，屬正確性缺口 |
