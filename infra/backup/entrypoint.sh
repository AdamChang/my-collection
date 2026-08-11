#!/bin/sh
set -eu

: "${BACKUP_BUCKET:?BACKUP_BUCKET must be set}"
: "${MONGODB_URI_FILE:=/var/run/secrets/mongo-uri/uri}"
: "${GCE_METADATA_HOST:=metadata.google.internal}"

# 取代 python3 的 urllib.parse.quote(safe="")，避免為了兩行字串編碼而裝整個 python。
urlencode() {
  awk -v input="$1" 'BEGIN {
    for (i = 0; i < 256; i++) ord[sprintf("%c", i)] = i
    for (i = 1; i <= length(input); i++) {
      c = substr(input, i, 1)
      if (c ~ /[A-Za-z0-9._~-]/) printf "%s", c
      else printf "%%%02X", ord[c]
    }
  }'
}

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

encoded_bucket="$(urlencode "$BACKUP_BUCKET")"
encoded_object="$(urlencode "$object_name")"

# 直接向 metadata server 換 access token，token 只流經 pipe 後落在 curl config，
# 不會出現在 shell 變數或 process 參數中。
curl --silent --show-error --fail \
  --header 'Metadata-Flavor: Google' \
  "http://$GCE_METADATA_HOST/computeMetadata/v1/instance/service-accounts/default/token" \
  | sed -n 's/.*"access_token"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/header = "Authorization: Bearer \1"/p' \
  > "$curl_config"
test -s "$curl_config" || {
  printf '%s\n' 'Failed to obtain an access token from the metadata server.' >&2
  exit 1
}
printf '\n' >> "$curl_config"

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
