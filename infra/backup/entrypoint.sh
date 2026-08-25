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
dump_log="$work_dir/mongodump.log"

# mongodump 連線失敗時會把它從 --config 讀進來的完整 URI（含密碼）原樣印到 stderr，
# 而 Cloud Run 的 stderr 直接進 Cloud Logging。憑證沒進 argv 不代表不會外洩，
# 錯誤路徑才是實際漏出來的地方（2026-08-16 的失敗即為實例）。
#
# 遮蔽比對 URI 的 userinfo 結構而非密碼值本身：把密碼取出來當 sed pattern，
# 會讓它出現在 sed 的 argv 裡，等於自己打破「憑證不進 argv」這條原則。
# 保留 host 以便診斷連線問題。
redact_uris() {
  sed -e 's|://[^/@[:space:]]*@|://<redacted>@|g'
}

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

# 不用 pipeline 過濾：/bin/sh 是 dash，沒有 PIPESTATUS，
# set -e 之下 mongodump 的失敗會被 sed 的成功蓋掉，變成靜默失敗。
# 改成先落檔、保留 exit code、再輸出遮蔽後的內容。
set +e
mongodump --config="$config_file" --archive="$archive_file" --gzip 2>"$dump_log"
dump_status="$?"
set -e

if [ -s "$dump_log" ]; then
  redact_uris < "$dump_log" >&2
fi

if [ "$dump_status" -ne 0 ]; then
  printf 'mongodump exited with status %s.\n' "$dump_status" >&2
  exit "$dump_status"
fi

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
