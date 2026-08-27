# 服務端告警與備份告警分開走各自的 channel：兩者的處理急迫性不同，
# 分開才能各自靜音而不互相影響。收件位址相同不構成合併的理由。
resource "google_monitoring_notification_channel" "service_alert_email" {
  display_name = "MyCollection service errors"
  type         = "email"

  labels = {
    email_address = var.service_alert_email
  }
}

# 條件用 5xx 計數而非 severity>=ERROR 的 log 比對：
# 後者會被任何一行 Error 等級的應用程式日誌觸發，對單一使用者的專案是純噪音。
# 5xx 是使用者實際看得到的失敗，也正是 Phase 6 canary 判定 rollback 的同一個訊號。
resource "google_monitoring_alert_policy" "cloud_run_server_errors" {
  display_name = "MyCollection Cloud Run 5xx"
  combiner     = "OR"

  alert_strategy {
    # min=0 的服務沒有流量時指標會缺值。缺值本身不觸發告警，
    # 但已開啟的 incident 必須能自行關閉，否則 scale-to-zero 後會一直掛著。
    auto_close = "1800s"

    # 這裡不設 notification_rate_limit：它只允許用在 log-based policy，metric policy 指定會被 API 以 400 拒絕。
    # metric policy 以 incident 為單位通知（開啟一次、關閉一次），節流由 alignment_period 提供，不需要它。
  }

  conditions {
    display_name = "API or Web returned 5xx"

    condition_threshold {
      filter = join(" AND ", [
        "resource.type = \"cloud_run_revision\"",
        "metric.type = \"run.googleapis.com/request_count\"",
        "metric.labels.response_code_class = \"5xx\"",
        "resource.labels.service_name = one_of(\"${google_cloud_run_v2_service.api.name}\", \"${google_cloud_run_v2_service.web.name}\")",
      ])

      comparison      = "COMPARISON_GT"
      threshold_value = 0
      duration        = "0s"

      aggregations {
        alignment_period     = "300s"
        per_series_aligner   = "ALIGN_SUM"
        cross_series_reducer = "REDUCE_SUM"
        group_by_fields      = ["resource.label.service_name"]
      }

      trigger {
        count = 1
      }
    }
  }

  notification_channels = [google_monitoring_notification_channel.service_alert_email.name]
}
