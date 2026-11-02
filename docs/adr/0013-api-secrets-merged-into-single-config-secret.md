# ADR-0013：API 機密合併為單一 `api-config` secret，Mongo 連線字串維持獨立

- 狀態：已接受
- 日期：2026-11-02
- 相關：[ADR-0011](0011-low-cost-production-on-cloud-run-and-atlas-free.md)（低成本正式環境）、[維運文件](../deployment/production-operations.md)

## 背景

GCP Secret Manager 的免費額度是**每個 billing account 6 個 active secret version**，由同一個 billing account 底下的所有 project 共用。my-collection 原本有 4 個 secret：`mongo-connection-string`、`jwt-signing-key`、`secret-protection-key`、`igdb-client-secret`，每個至少佔 1 個 version，輪替期間新舊並存時還會更多。

## 決定

- `jwt-signing-key`、`secret-protection-key`、`igdb-client-secret` 合併成一個 secret **`api-config`**，內容是 JSON：
  `{"Jwt":{"Key":"…"},"SecretProtection":{"Key":"…"},"Igdb":{"ClientId":"…","ClientSecret":"…"}}`。
- 是否啟用 IGDB 只看 JSON 裡有沒有 `Igdb` 區塊（`IgdbOptions.IsConfigured`）。Terraform 的 `igdb_enabled`、`igdb_client_id` 移除，公開的 ClientId 一起放進 JSON，讓 IGDB 設定只有一個來源。
- Cloud Run 以 secret **volume** 掛載到專屬目錄 `/secrets/api/config.json`，環境變數 `SECRETS_CONFIG_FILE` 指向它；`SecretsFileConfiguration.AddSecretsFile` 把它加入 configuration。有設定路徑但檔案不存在時直接拋例外，讓 revision 啟動失敗，由 canary 擋下，而不是帶著空金鑰上線。
- **`mongo-connection-string` 刻意維持獨立。** 備份 Job 的 service account（`backups.tf`）只需要它；合併進 `api-config` 會讓備份身分讀得到 JWT 簽章金鑰與外部帳號加密金鑰，違反最小權限。
- 合併時三個值原樣沿用，不輪替：換掉 `secret-protection-key` 會讓所有已綁定的外部帳號憑證無法解密。

## 後果

- 本專案從 4 個 version 降到 2 個。
- **三種金鑰的輪替綁在同一個 version。** 只換 IGDB secret 也要讀出整份 JSON、改一個欄位、新增 version；改錯其他欄位會同時影響登入與外部帳號解密。輪替流程見維運文件。
- 每個 secret 只保留 1 個非 DESTROYED 的 version，新版本確認可用後就銷毀舊版，否則額度會被舊版吃掉。
- 本機與 docker compose 不設定 `SECRETS_CONFIG_FILE`，照舊用 user secrets 或環境變數，不受影響。
- **不要再把 `api-config` 拆回多個 secret。** 拆開會直接吃掉免費額度；若是為了權限隔離，應先確認是否真的出現只需要其中一把金鑰的新身分。
