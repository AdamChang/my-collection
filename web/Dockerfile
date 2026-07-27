FROM node:24-alpine AS build
WORKDIR /src

COPY web/package*.json ./
RUN npm ci

COPY web/ ./
RUN npm run build

FROM nginx:alpine AS runtime
COPY web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist/web/browser /usr/share/nginx/html

EXPOSE 80
