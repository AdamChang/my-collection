#!/usr/bin/env bash
set -Eeuo pipefail

required_variables=(API_BASE_URL SMOKE_EMAIL SMOKE_PASSWORD)
for variable_name in "${required_variables[@]}"; do
  if [[ -z "${!variable_name:-}" ]]; then
    echo "Missing required environment variable: ${variable_name}" >&2
    exit 1
  fi
done

for command_name in curl jq base64; do
  if ! command -v "${command_name}" >/dev/null 2>&1; then
    echo "Required command is unavailable: ${command_name}" >&2
    exit 1
  fi
done

api_base_url="${API_BASE_URL%/}"
work_directory="$(mktemp -d)"
auth_config="${work_directory}/curl-auth.conf"
category_id=""
item_id=""
image_id=""
share_id=""

authenticated_delete() {
  local path="$1"

  curl --config "${auth_config}" \
    --silent \
    --show-error \
    --fail \
    --request DELETE \
    --output /dev/null \
    "${api_base_url}${path}"
}

cleanup() {
  local exit_code=$?
  trap - EXIT

  if [[ -s "${auth_config}" ]]; then
    [[ -z "${share_id}" ]] || authenticated_delete "/shares/${share_id}" || true
    [[ -z "${image_id}" || -z "${item_id}" ]] || authenticated_delete "/items/${item_id}/images/${image_id}" || true
    [[ -z "${item_id}" ]] || authenticated_delete "/items/${item_id}" || true
    [[ -z "${category_id}" ]] || authenticated_delete "/categories/${category_id}" || true
  fi

  rm -rf -- "${work_directory}"
  if [[ ${exit_code} -ne 0 ]]; then
    echo "Production API smoke test failed; cleanup was attempted." >&2
  fi
  exit "${exit_code}"
}
trap cleanup EXIT

authenticated_json_request() {
  local method="$1"
  local path="$2"
  local request_file="$3"
  local response_file="$4"

  curl --config "${auth_config}" \
    --silent \
    --show-error \
    --fail \
    --request "${method}" \
    --header "Content-Type: application/json" \
    --data-binary "@${request_file}" \
    --output "${response_file}" \
    "${api_base_url}${path}"
}

echo "Checking API health endpoint."
curl --silent --show-error --fail --output /dev/null "${api_base_url}/health/live"

echo "Authenticating the production smoke-test user."
export SMOKE_EMAIL SMOKE_PASSWORD
jq -n '{email: env.SMOKE_EMAIL, password: env.SMOKE_PASSWORD}' >"${work_directory}/login.json"
curl --silent \
  --show-error \
  --fail \
  --request POST \
  --header "Content-Type: application/json" \
  --data-binary "@${work_directory}/login.json" \
  --output "${work_directory}/login-response.json" \
  "${api_base_url}/auth/login"
access_token="$(jq -er '.accessToken | select(type == "string" and length > 0)' "${work_directory}/login-response.json")"
printf 'header = "Authorization: Bearer %s"\n' "${access_token}" >"${auth_config}"
chmod 600 "${auth_config}"
unset access_token SMOKE_PASSWORD

echo "Verifying authenticated access."
curl --config "${auth_config}" --silent --show-error --fail --output /dev/null "${api_base_url}/auth/me"

run_suffix="${GITHUB_SHA:-local}-$(date -u +%Y%m%d%H%M%S)"
run_suffix="${run_suffix:0:48}"

echo "Creating smoke-test category and item."
jq -n --arg name "cloud-run-smoke-${run_suffix}" \
  '{name: $name, icon: "figure", kind: "Physical", defaultDisplayMode: "List", fields: []}' \
  >"${work_directory}/category.json"
authenticated_json_request POST /categories "${work_directory}/category.json" "${work_directory}/category-response.json"
category_id="$(jq -er '.id | select(type == "string" and length > 0)' "${work_directory}/category-response.json")"

jq -n --arg category_id "${category_id}" --arg name "cloud-run-smoke-item-${run_suffix}" \
  '{categoryId: $category_id, name: $name, description: null, tags: [], isShowcased: true, attributes: {}, acquisition: null}' \
  >"${work_directory}/item.json"
authenticated_json_request POST /items "${work_directory}/item.json" "${work_directory}/item-response.json"
item_id="$(jq -er '.id | select(type == "string" and length > 0)' "${work_directory}/item-response.json")"

echo "Uploading and reading a GCS-backed image."
printf '%s' 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=' \
  | base64 --decode >"${work_directory}/pixel.png"
curl --config "${auth_config}" \
  --silent \
  --show-error \
  --fail \
  --request POST \
  --form "file=@${work_directory}/pixel.png;type=image/png" \
  --output "${work_directory}/image-response.json" \
  "${api_base_url}/items/${item_id}/images"
image_id="$(jq -er '.id | select(type == "string" and length > 0)' "${work_directory}/image-response.json")"
card_path="$(jq -er '.cardPath | select(type == "string" and length > 0)' "${work_directory}/image-response.json")"
curl --config "${auth_config}" \
  --silent \
  --show-error \
  --fail \
  --output "${work_directory}/authenticated-image.webp" \
  "${api_base_url}/media/${card_path}"
[[ -s "${work_directory}/authenticated-image.webp" ]]

echo "Creating and verifying an anonymous public share."
jq -n \
  '{scope: "Showcase", includeCategoryIds: [], includePrice: false, includeRating: false, collageSlotCount: 4, expiresAt: null}' \
  >"${work_directory}/share.json"
authenticated_json_request POST /shares "${work_directory}/share.json" "${work_directory}/share-response.json"
share_id="$(jq -er '.id | select(type == "string" and length > 0)' "${work_directory}/share-response.json")"
share_slug="$(jq -er '.slug | select(type == "string" and length > 0)' "${work_directory}/share-response.json")"
curl --silent \
  --show-error \
  --fail \
  --output "${work_directory}/public-share.json" \
  "${api_base_url}/public/${share_slug}"
jq -e --arg item_id "${item_id}" '.. | strings | select(. == $item_id)' \
  "${work_directory}/public-share.json" >/dev/null
curl --silent \
  --show-error \
  --fail \
  --output "${work_directory}/public-image.webp" \
  "${api_base_url}/public/${share_slug}/media/${card_path}"
[[ -s "${work_directory}/public-image.webp" ]]

echo "Production API smoke test passed."
