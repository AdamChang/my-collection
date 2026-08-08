# MyCollection Cloud Run Production 部署計畫

- 狀態：已確認，尚未實作
- 日期：2026-08-08
- 決策依據：[ADR-0011](../adr/0011-low-cost-production-on-cloud-run-and-atlas-free.md)

## 完成定義

- Angular Web 與 ASP.NET Core API 分別在 `asia-east1` 的 Cloud Run 運行。
- Production Atlas、GCS、Cloud Tasks 與備份均不依賴開發機或 Cloud Run 本機磁碟。
- Production 只能由 GitHub Actions `workflow_dispatch` 發布，且 canary gate 通過後才取得 100% 流量。
- 可從前一個 Cloud Run revision 回復服務，也能從最近一次 production dump 復原資料。
- Repository、GitHub Actions logs、Terraform variables/state output 均不洩漏 secrets。

## Phase 0：Preflight 與 Bootstrap

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

### Terraform ownership

- GCP APIs、IAM、Artifact Registry、GCS、Secret Manager metadata、Cloud Run、Cloud Tasks、Cloud Scheduler、backup job、budget notifications。
- Atlas project、Free cluster、network access list 與不會讓明文秘密落入 state 的 resources。
- 若 Atlas DB user password 無法避免進入 state，改由一次性安全 bootstrap 建立。

### `workflow_dispatch` pipeline

1. 執行 backend／frontend tests 與 production builds。
2. 建立 Web/API images，以 commit SHA 標記並推至 Artifact Registry。
3. 部署 API 0% traffic tagged revision，對 tag URL 執行 health、登入、Mongo CRUD、GCS、Share Link smoke tests。
4. API 切入 10% traffic，以 synthetic requests 觀察 15 分鐘；失敗自動切回前一 revision，成功升至 100%。
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
