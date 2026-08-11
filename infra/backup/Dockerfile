# mongodump/mongorestore 的官方 tarball 只提供動態連結的 glibc 建置，
# 因此 builder 與 runtime 都固定在同一個 debian 版本，避免 glibc 版本漂移。
ARG DEBIAN_TAG=12.11-slim

FROM debian:${DEBIAN_TAG} AS mongo-tools

ARG MONGODB_DATABASE_TOOLS_VERSION=100.17.0
ARG MONGODB_DATABASE_TOOLS_PLATFORM=debian12-x86_64

# binutils 只存在於 builder，用來 strip 掉 Go binary 的除錯符號。
RUN apt-get update \
    && apt-get install --no-install-recommends --yes binutils ca-certificates curl \
    && mkdir -p /out \
    && curl --fail --location --silent --show-error \
      "https://fastdl.mongodb.org/tools/db/mongodb-database-tools-${MONGODB_DATABASE_TOOLS_PLATFORM}-${MONGODB_DATABASE_TOOLS_VERSION}.tgz" \
      --output /tmp/mongodb-database-tools.tgz \
    && tar --extract --gzip --file /tmp/mongodb-database-tools.tgz --directory /tmp \
    && install --mode=0755 /tmp/mongodb-database-tools-*/bin/mongodump /out/mongodump \
    && install --mode=0755 /tmp/mongodb-database-tools-*/bin/mongorestore /out/mongorestore \
    && strip --strip-unneeded /out/mongodump /out/mongorestore \
    && /out/mongodump --version \
    && /out/mongorestore --version

FROM debian:${DEBIAN_TAG}

# runtime 只需要 HTTPS 上傳能力（curl + 根憑證），token 改由 metadata server 取得，
# 不再需要 google-cloud-cli 與 python3。
RUN apt-get update \
    && apt-get install --no-install-recommends --yes ca-certificates curl \
    && rm -rf /var/lib/apt/lists/* /usr/share/doc /usr/share/man /usr/share/locale \
    && useradd --system --create-home --home-dir /home/mongobackup --shell /usr/sbin/nologin mongobackup

COPY --from=mongo-tools --chmod=0555 /out/mongodump /usr/local/bin/mongodump
COPY --from=mongo-tools --chmod=0555 /out/mongorestore /usr/local/bin/mongorestore
COPY --chmod=0555 infra/backup/entrypoint.sh /usr/local/bin/backup-entrypoint

USER mongobackup
WORKDIR /home/mongobackup

ENTRYPOINT ["/usr/local/bin/backup-entrypoint"]
