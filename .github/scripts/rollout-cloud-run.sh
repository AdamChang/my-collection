#!/usr/bin/env bash
set -Eeuo pipefail

: "${PROJECT_ID:?PROJECT_ID is required}"
: "${REGION:?REGION is required}"
: "${SERVICE:?SERVICE is required}"
: "${IMAGE:?IMAGE is required}"
: "${HEALTH_PATH:?HEALTH_PATH is required}"
: "${RELEASE_TAG:?RELEASE_TAG is required}"

CANARY_PERCENT="${CANARY_PERCENT:-40}"
OBSERVE_MINUTES="${OBSERVE_MINUTES:-15}"
PREVIOUS_PERCENT=$((100 - CANARY_PERCENT))
traffic_shifted=false

max_traffic_tag_length=$((46 - ${#SERVICE}))
if ((max_traffic_tag_length < 1)); then
  echo "Service name is too long to create a Cloud Run traffic tag." >&2
  exit 1
fi
candidate_tag="candidate-$RELEASE_TAG"
candidate_tag="${candidate_tag:0:max_traffic_tag_length}"
candidate_tag="${candidate_tag%-}"

service_json="$(gcloud run services describe "$SERVICE" \
  --project "$PROJECT_ID" \
  --region "$REGION" \
  --format json)"
previous_revision="$(jq -r \
  '.status.traffic | map(select(.percent == 100 and .revisionName != null)) | .[0].revisionName // empty' \
  <<<"$service_json")"

if [[ -z "$previous_revision" ]]; then
  echo "Service $SERVICE has no 100% baseline revision; bootstrap it before canary deployment." >&2
  exit 1
fi

rollback() {
  local status=$?
  if [[ "$traffic_shifted" == true ]]; then
    echo "Canary failed; restoring $previous_revision to 100% traffic." >&2
    gcloud run services update-traffic "$SERVICE" \
      --project "$PROJECT_ID" \
      --region "$REGION" \
      --to-revisions "$previous_revision=100" \
      --quiet || true
  fi
  exit "$status"
}
trap rollback ERR

gcloud run deploy "$SERVICE" \
  --project "$PROJECT_ID" \
  --region "$REGION" \
  --image "$IMAGE" \
  --revision-suffix "$RELEASE_TAG" \
  --tag "$candidate_tag" \
  --no-traffic \
  --quiet

service_json="$(gcloud run services describe "$SERVICE" \
  --project "$PROJECT_ID" \
  --region "$REGION" \
  --format json)"
new_revision="$(jq -r --arg tag "$candidate_tag" \
  '.status.traffic[] | select(.tag == $tag) | .revisionName' <<<"$service_json")"
candidate_url="$(jq -r --arg tag "$candidate_tag" \
  '.status.traffic[] | select(.tag == $tag) | .url' <<<"$service_json")"
stable_url="$(jq -r '.status.url' <<<"$service_json")"

test -n "$new_revision"
test -n "$candidate_url"
test -n "$stable_url"

for attempt in {1..30}; do
  if curl --fail --silent --show-error --max-time 15 "$candidate_url$HEALTH_PATH" >/dev/null; then
    break
  fi
  if [[ "$attempt" -eq 30 ]]; then
    echo "Candidate health check did not become ready." >&2
    exit 1
  fi
  sleep 5
done

if [[ -n "${SMOKE_SCRIPT:-}" ]]; then
  API_BASE_URL="$candidate_url" bash "$SMOKE_SCRIPT"
fi

canary_started_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
gcloud run services update-traffic "$SERVICE" \
  --project "$PROJECT_ID" \
  --region "$REGION" \
  --to-revisions "$new_revision=$CANARY_PERCENT,$previous_revision=$PREVIOUS_PERCENT" \
  --quiet
traffic_shifted=true

for ((minute = 1; minute <= OBSERVE_MINUTES; minute++)); do
  for request in {1..10}; do
    curl --fail --silent --show-error --max-time 15 "$stable_url$HEALTH_PATH" >/dev/null
  done

  error_id="$(gcloud logging read \
    "resource.type=cloud_run_revision AND resource.labels.service_name=$SERVICE AND resource.labels.revision_name=$new_revision AND timestamp>=\"$canary_started_at\" AND (severity>=ERROR OR httpRequest.status>=500)" \
    --project "$PROJECT_ID" \
    --limit 1 \
    --format 'value(insertId)')"
  if [[ -n "$error_id" ]]; then
    echo "Canary revision emitted an error or HTTP 5xx." >&2
    false
  fi

  if [[ "$minute" -lt "$OBSERVE_MINUTES" ]]; then
    sleep 60
  fi
done

gcloud run services update-traffic "$SERVICE" \
  --project "$PROJECT_ID" \
  --region "$REGION" \
  --to-revisions "$new_revision=100" \
  --quiet
traffic_shifted=false
trap - ERR

if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
  {
    echo "service=$SERVICE"
    echo "previous_revision=$previous_revision"
    echo "new_revision=$new_revision"
    echo "stable_url=$stable_url"
  } >>"$GITHUB_OUTPUT"
fi
