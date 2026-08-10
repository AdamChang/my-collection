FROM node:24-alpine AS build
WORKDIR /src

COPY web/package*.json ./
RUN npm ci

COPY web/ ./
RUN npm run build

FROM nginx:alpine AS runtime
COPY web/nginx.conf /etc/nginx/templates/default.conf.template
COPY web/runtime-config.template.js /usr/share/nginx/html/runtime-config.template.js
COPY web/docker-entrypoint.d/40-runtime-config.sh /docker-entrypoint.d/40-runtime-config.sh
COPY --from=build /src/dist/web/browser /usr/share/nginx/html
RUN chmod +x /docker-entrypoint.d/40-runtime-config.sh

ENV PORT=8080 \
    API_BASE_URL=/api
EXPOSE 8080
