# ADR-0011：以 Cloud Run 與 Atlas Free 運行低流量正式環境

- 狀態：已接受
- 日期：2026-08-08
- 相關：[部署計畫](../deployment/cloud-run-production-plan.md)

## 背景

MyCollection 是單一使用者、低流量的正式網站，只有分享連結需要匿名公開。既有程式由 Angular static SPA、ASP.NET Core API、MongoDB、LocalFileStorage，以及 process-local 背景佇列組成；Docker Compose 以 nginx 的 `/api` reverse proxy 串接 Web 與 API。

部署必須優先控制固定成本，同時保留正式資料的持久性、可復原性與可重複部署。現有 development Atlas 不得成為 production 的持續讀寫資料庫。

## 決定

### 一、Web 與 API 分別部署至 Cloud Run

在獨立 GCP Project 的 `asia-east1` 建立兩個 Cloud Run services，第一版各自使用 `run.app` URL。Web 以 runtime config 取得 API URL；API 的 CORS allowlist 只接受正式 Web origin。

API 使用 `min-instances=0`、`max-instances=1`，接受 scale-from-zero cold start。Cloud Run ingress 允許匿名連線，實際授權由應用程式 JWT、Share Link 與 Cloud Tasks OIDC 負責。

### 二、Production 使用 GCP 上的 Atlas Free

建立新的 Atlas Free cluster 與 `mycollection-prod` database，不沿用 development cluster。Atlas Network Access 暫時允許 `0.0.0.0/0`，因此不建立 VPC、Cloud NAT 或固定出口 IP。

這是刻意接受的低成本取捨，不把 Atlas Free 誤稱為 production-grade tier。補償控制包括 TLS、production 專用 DB user、強隨機密碼、只授予 production database 的 `readWrite` 權限，以及 Secret Manager。

搬移前，documents 與 indexes 的總使用量必須低於 350 MB，為 Atlas Free 的 512 MB 上限保留成長空間；超過 Gate 就升級 Atlas Flex，不刪除歷史資料。

### 三、持久資料不留在 Cloud Run 本機檔案系統

圖片改存 private GCS bucket，由 API 串流。已登入使用者可讀取自己的圖片；匿名使用者必須透過有效 Share Link，且只能讀取分享範圍內的圖片。只遷移不能重建的使用者上傳圖片，外部來源衍生圖片允許重新下載。

Atlas Free 沒有原生 backup。每日 Cloud Run Job 執行 `mongodump`，壓縮後寫入獨立 private GCS bucket 並保留 30 天；接受 RPO 24 小時、人工復原 RTO 4 小時，且每季執行 restore drill。

### 四、需要可靠性的背景作業使用 Cloud Tasks

同步與補完由 Cloud Tasks dispatch，採 at-least-once delivery。Handler 必須 idempotent，以穩定 operation ID 去重，最多嘗試五次並使用 exponential backoff；最終失敗寫回 Sync Job，供使用者手動重跑。Task handler 只接受指定 service account 的 OIDC。

外部來源衍生圖片仍允許失敗後重新產生，不提供與同步／補完相同的持久化保證。

### 五、Production 只能手動觸發並以 canary 發布

GitHub Actions 只接受 `workflow_dispatch`。測試與 build 成功後，以 commit SHA 標記 image，先部署 0% traffic 的 tagged revision 並執行 smoke tests；通過後導入 10% traffic，以 synthetic requests 觀察 15 分鐘。沒有非預期 5xx、health failure 或關鍵 smoke-test failure 才升至 100%，否則自動切回前一個 revision。

API 先完成 canary 與 promotion，再部署 Web。Rollback 只切 Cloud Run revision，不自動倒回 MongoDB；資料變更必須至少向後相容一個 revision。

## 被否決的替代方案

- **持續沿用 development Atlas**：省去搬移，但 production 與 development 的資料、帳號、變更風險無法隔離。
- **Atlas Dedicated 配合 Private Service Connect**：正式環境能力完整，但 database 與 PSC 的固定成本不符合目前使用規模。
- **Firestore with MongoDB compatibility**：可能降低低流量成本，但不是完整 MongoDB；需要重新驗證 query、index、transaction、BSON 與 driver semantics。
- **Serverless VPC Access Connector 加 Cloud NAT**：至少兩個 connector instances 的固定費超過每月 US$5 成本上限。
- **單一 Cloud Run service 或 Load Balancer path routing**：前者耦合 Web/API 發布，後者增加不必要的固定成本與基礎架構。

## 後果

- Atlas Free、`0.0.0.0/0` 與每日 dump 是已知風險；容量、備份與異常必須被監控，而不是視為永久免費且無限制。
- 新 production JWT／SecretProtection keys 會使舊 token 與 development Provider 密文失效。一般收藏資料搬移後，Provider credentials 必須清除並重新輸入。
- Web/API 分離後必須新增 runtime API config、嚴格 CORS 與 forwarded-header handling，不能沿用 Compose 的 `api:8080` 假設。
- `min-instances=0` 會有 cold start；只有實際監控證明影響不可接受時，才重新評估常駐 instance。
- Terraform 管理可安全宣告的 GCP 與 Atlas resources；任何會讓明文秘密進入 state 的資源改由安全 bootstrap 建立。
