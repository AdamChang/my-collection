terraform {
  required_version = ">= 1.15.0"

  backend "gcs" {
    bucket = "mycollection-504914-tfstate"
    prefix = "production/bootstrap"
  }

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 7.0"
    }
  }
}
