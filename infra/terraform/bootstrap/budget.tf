resource "google_monitoring_notification_channel" "budget_email" {
  project      = var.project_id
  display_name = "MyCollection budget email"
  type         = "email"

  labels = {
    email_address = var.budget_email
  }

  depends_on = [google_project_service.required]
}

resource "google_billing_budget" "monthly_warning" {
  billing_account = var.billing_account
  display_name    = "MyCollection monthly TWD 150"

  budget_filter {
    projects = ["projects/${data.google_project.current.number}"]
  }

  amount {
    specified_amount {
      currency_code = "TWD"
      units         = "150"
    }
  }

  threshold_rules {
    threshold_percent = 0.50
  }

  threshold_rules {
    threshold_percent = 0.90
  }

  threshold_rules {
    threshold_percent = 1.00
  }

  all_updates_rule {
    monitoring_notification_channels = [google_monitoring_notification_channel.budget_email.name]
    disable_default_iam_recipients   = false
  }

  depends_on = [google_project_service.required]
}

resource "google_billing_budget" "monthly_high_priority" {
  billing_account = var.billing_account
  display_name    = "MyCollection monthly TWD 300"

  budget_filter {
    projects = ["projects/${data.google_project.current.number}"]
  }

  amount {
    specified_amount {
      currency_code = "TWD"
      units         = "300"
    }
  }

  threshold_rules {
    threshold_percent = 1.00
  }

  all_updates_rule {
    monitoring_notification_channels = [google_monitoring_notification_channel.budget_email.name]
    disable_default_iam_recipients   = false
  }

  depends_on = [google_project_service.required]
}
