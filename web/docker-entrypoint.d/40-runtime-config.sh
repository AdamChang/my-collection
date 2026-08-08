#!/bin/sh
set -eu

envsubst '${API_BASE_URL}' \
  < /usr/share/nginx/html/runtime-config.template.js \
  > /usr/share/nginx/html/config.js
rm /usr/share/nginx/html/runtime-config.template.js
