FROM debian:12.11-slim AS mongo-tools

ARG MONGODB_DATABASE_TOOLS_VERSION=100.17.0

RUN apt-get update \
    && apt-get install --no-install-recommends --yes ca-certificates curl \
    && curl --fail --location --silent --show-error \
      "https://fastdl.mongodb.org/tools/db/mongodb-database-tools-ubuntu2204-x86_64-${MONGODB_DATABASE_TOOLS_VERSION}.tgz" \
      --output /tmp/mongodb-database-tools.tgz \
    && tar --extract --gzip --file /tmp/mongodb-database-tools.tgz --directory /tmp \
    && install --mode=0555 /tmp/mongodb-database-tools-*/bin/mongodump /usr/local/bin/mongodump \
    && install --mode=0555 /tmp/mongodb-database-tools-*/bin/mongorestore /usr/local/bin/mongorestore

FROM gcr.io/google.com/cloudsdktool/google-cloud-cli:496.0.0-slim

COPY --from=mongo-tools /usr/local/bin/mongodump /usr/local/bin/mongodump
COPY --from=mongo-tools /usr/local/bin/mongorestore /usr/local/bin/mongorestore
COPY infra/backup/entrypoint.sh /usr/local/bin/backup-entrypoint

RUN chmod 0555 /usr/local/bin/mongodump /usr/local/bin/mongorestore /usr/local/bin/backup-entrypoint

USER cloudsdk
WORKDIR /home/cloudsdk

ENTRYPOINT ["/usr/local/bin/backup-entrypoint"]
