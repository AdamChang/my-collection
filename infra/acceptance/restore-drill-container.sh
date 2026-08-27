#!/bin/sh
# Phase 7-5 / Phase 5 Gate：受控還原演練，在備份 image 內執行。
#
# 由 restore-drill.ps1 呼叫，不單獨使用。production URI 由 stdin 進來，
# 寫進 tmpfs 的 0600 檔案，不落磁碟、不進 argv、不進環境變數。
set -eu
umask 077

: "${TARGET_DB:?TARGET_DB must be set}"
: "${ARCHIVE_FILE:=/work/selected.archive.gz}"
: "${VERIFY_DIR:=/work/verify}"

config="/secure/mongo.yaml"
log="/secure/tool.log"

# 連線失敗時 mongodump / mongorestore 會把它從 --config 讀進來的完整 URI（含密碼）
# 原樣寫到 stderr。遮蔽比對 URI 的 userinfo 結構而非密碼值本身：把密碼取出來當
# sed pattern 會讓它出現在 argv 裡，等於自己打破「憑證不進 argv」這條原則。
# 保留 host 以便診斷連線問題。與 infra/backup/entrypoint.sh 的做法一致。
redact_uris() {
  sed -e 's|://[^/@[:space:]]*@|://<redacted>@|g'
}

emit_log() {
  [ -s "$log" ] || return 0
  redact_uris < "$log" >&2
}

fail() {
  printf '%s\n' "$1" >&2
  exit 1
}

printf 'uri: %s\n' "$(tr -d '\r\n')" > "$config"
[ -s "$config" ] || fail 'Mongo URI is empty.'

# --- 護欄：任何一項不成立就停在動資料庫之前 --------------------------------
case "$TARGET_DB" in
  mc-r-[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]T[0-9][0-9][0-9][0-9][0-9][0-9]Z-*) ;;
  *) fail "Target name does not match the drill format: $TARGET_DB" ;;
esac
[ "$TARGET_DB" != "mycollection" ] || fail 'Target name is the production database.'
[ "${#TARGET_DB}" -lt 38 ] || fail "Target name exceeds MongoDB's limit: ${#TARGET_DB} bytes"
[ -s "$ARCHIVE_FILE" ] || fail "Archive is missing or empty: $ARCHIVE_FILE"

# db 名不是機密。連線字串裡有沒有 database path 決定 mongodump 能不能吃 --db：
# 兩者同時指定且不同時，tools 會拒絕：
#   Invalid Options: Cannot specify different database in connection URI and command-line option
uri_db="$(sed -n 's|^uri: mongodb[^:]*://[^/]*/\([^?]*\).*|\1|p' "$config")"
if [ -n "$uri_db" ]; then
  printf 'Connection string pins database: %s\n' "$uri_db" >&2
else
  printf 'Connection string has no database path.\n' >&2
fi

# --- 還原 -------------------------------------------------------------------
# 不使用 --drop。目標庫是帶 UTC 時戳與 8 位隨機值的全新名稱，--drop 沒有必要；
# 而在無法讀取目標庫（URI 釘住 database）的情況下也做不了 runbook 要求的
# 「不存在」前置檢查。與其補一個檢查再保留破壞性旗標，不如讓旗標消失：
# 目標庫萬一真的存在，還原會是合併而非覆寫，且會在下面的 counts 比對中現形。
printf 'Restoring into %s ...\n' "$TARGET_DB" >&2
restore_started="$(date -u +%s)"
set +e
mongorestore --config="$config" \
  --archive="$ARCHIVE_FILE" \
  --gzip \
  --nsFrom='mycollection.*' \
  --nsTo="${TARGET_DB}.*" \
  --verbose 2>"$log"
restore_status=$?
set -e
emit_log
[ "$restore_status" -eq 0 ] || fail "mongorestore exited with status $restore_status."
restore_seconds=$(( $(date -u +%s) - restore_started ))
printf 'Restore completed in %s seconds.\n' "$restore_seconds" >&2

mkdir -p "$VERIFY_DIR"

# mongo tools 用反引號包住 namespace（`db.coll`）。先整批拔掉再比對，
# 比在每個 pattern 裡處理反引號可靠 —— 反引號在雙引號字串裡還會被 shell 當成命令替換。
# \140 是反引號的八進位值，用它就完全避開引號問題。
strip_backticks() {
  tr -d '\140'
}

# 還原庫的 counts 與 index 建立情形只能取自 mongorestore 自己的輸出：
# URI 釘住 production database 時，無法用同一份 config 再去讀取還原庫。
redact_uris < "$log" | strip_backticks \
  | sed -n "s|.*finished restoring ${TARGET_DB}\\.\\([^ ]*\\) (\\([0-9]*\\) document.*|\\1 \\2|p" \
  | sort > "$VERIFY_DIR/counts-restored.txt"

# 實際輸出是 "restoring indexes for collection <ns> from metadata"；
# TARGET_DB 前面的 .* 同時吸收 "collection " 這個字，也相容沒有它的舊版輸出。
redact_uris < "$log" | strip_backticks \
  | sed -n "s|.*restoring indexes for .*${TARGET_DB}\\.\\([^ ]*\\) .*|\\1|p" \
  | sort -u > "$VERIFY_DIR/indexes-restored.txt"
: > "$log"

# --- production 側：counts 與 index 定義 ------------------------------------
rm -rf "$VERIFY_DIR/prod"
mkdir -p "$VERIFY_DIR/prod"
set +e
if [ -n "$uri_db" ]; then
  mongodump --config="$config" --out="$VERIFY_DIR/prod" --verbose 2>"$log"
else
  mongodump --config="$config" --db=mycollection --out="$VERIFY_DIR/prod" --verbose 2>"$log"
fi
dump_status=$?
set -e
if [ "$dump_status" -ne 0 ]; then
  emit_log
  fail 'Could not read production for comparison; the restored database is retained.'
fi

redact_uris < "$log" | strip_backticks \
  | sed -n 's|.*done dumping [^.]*\.\([^ ]*\) (\([0-9]*\) document.*|\1 \2|p' \
  | sort > "$VERIFY_DIR/counts-prod.txt"
: > "$log"

# bson 是實際的 production 資料，比對只需要 metadata。留在開發機上沒有理由。
find "$VERIFY_DIR/prod" -name '*.bson' -delete

printf '%s\n' "$restore_seconds" > "$VERIFY_DIR/restore-seconds.txt"
printf 'Verification data written to %s\n' "$VERIFY_DIR" >&2
