#!/bin/sh
set -eu

: "${BACKUP_BUCKET:?BACKUP_BUCKET must be set}"
: "${MONGODB_URI_FILE:=/var/run/secrets/mongo-uri/uri}"

work_dir="$(mktemp -d)"
config_file="$work_dir/mongodump.yaml"
archive_file="$work_dir/mongodump.archive.gz"
response_file="$work_dir/upload-response.json"
curl_config="$work_dir/curl.conf"

on_exit() {
  status="$?"
  rm -rf "$work_dir"
  if [ "$status" -ne 0 ]; then
    printf '%s\n' '{"event":"mongo_backup_failed"}' >&2
  fi
  exit "$status"
}

trap on_exit EXIT
trap 'exit 130' HUP INT TERM

umask 077
uri="$(tr -d '\r\n' < "$MONGODB_URI_FILE")"
[ -n "$uri" ] || { printf '%s\n' 'Mongo URI secret is empty.' >&2; exit 1; }
printf 'uri: %s\n' "$uri" > "$config_file"
unset uri

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
object_name="mycollection-prod/$timestamp/mongodump.archive.gz"

mongodump --config="$config_file" --archive="$archive_file" --gzip
test -s "$archive_file"

encoded_bucket="$(python3 -c 'import sys, urllib.parse; print(urllib.parse.quote(sys.argv[1], safe=""))' "$BACKUP_BUCKET")"
encoded_object="$(python3 -c 'import sys, urllib.parse; print(urllib.parse.quote(sys.argv[1], safe=""))' "$object_name")"
access_token="$(gcloud auth print-access-token --quiet)"
printf 'header = "Authorization: Bearer %s"\n' "$access_token" > "$curl_config"
unset access_token
upload_status="$(curl --silent --show-error \
  --config "$curl_config" \
  --output "$response_file" \
  --write-out '%{http_code}' \
  --request POST \
  --header 'Content-Type: application/octet-stream' \
  --data-binary "@$archive_file" \
  "https://storage.googleapis.com/upload/storage/v1/b/$encoded_bucket/o?uploadType=media&name=$encoded_object&ifGenerationMatch=0")"

case "$upload_status" in
  200|201) ;;
  *)
    printf 'Backup upload failed with HTTP %s.\n' "$upload_status" >&2
    exit 1
    ;;
esac

printf '{"event":"mongo_backup_completed","object":"%s"}\n' "$object_name"
