# MongoDB Production Backup and Restore Runbook

## Scope and safeguards

This runbook restores only a production backup archive into a newly generated temporary database. It must never be used to overwrite `mycollection`. The operator must use a secured environment that can read the production MongoDB secret without printing its payload, and must not place the URI in a command argument, Cloud Logging, shell history, Git, or Terraform state.

The daily backup job writes a gzip archive to the private backup bucket. Its service account can create objects but cannot read or list them. A restore operator uses a separate, least-privilege credential with read access to the exact backup object.

## Quarterly restore drill

1. Start by selecting the newest non-empty archive object from the private backup bucket. Record only its object name, generation, size, and creation time in the drill record.
2. Generate a target name in the form `mycollection-restore-YYYYMMDDTHHMMSSZ-<random>`. Before doing any database operation, compare it literally with `mycollection`; if it is equal, contains an operator-supplied override, or cannot be determined, stop.
3. Download the selected archive into an access-controlled temporary directory and verify it is non-empty. Create a `0600` MongoDB Tools configuration file in that directory from the mounted Secret Manager volume; do not use `--uri`, an environment dump, command substitution, or a verbose shell mode. The config file contains the production URI with only the database name replaced by the generated temporary target.
4. Run `mongorestore --config=<temporary-config> --archive=<selected-archive> --gzip --drop`. The `--drop` flag is permitted only after the generated target has passed the literal production-name check, and only because a new temporary database is being restored.
5. Using the same temporary configuration, collect collection document counts and index definitions from both `mycollection-prod` and the generated restore database. Compare all collection names, counts, index key patterns, index names, and uniqueness settings. Record comparisons and elapsed duration without recording the URI or credentials.
6. If the comparison fails, retain the temporary database and archive until the incident is reviewed. If it passes, separately confirm the exact resolved temporary database name one more time, then drop only that database and securely remove the local archive and temporary configuration file.

## Backup failure response

Cloud Run writes a structured `mongo_backup_completed` event only after a non-empty archive is uploaded. Any error causes a structured `mongo_backup_failed` event and a non-zero job execution. The monitoring alert routes matching Cloud Run Job errors to the configured notification channel.

On an alert, inspect the execution and its Cloud Logging entries without copying environment variables, configuration files, Mongo URIs, or credentials. Fix the underlying permissions, Secret Manager mount, Atlas connectivity, or bucket issue; then manually execute the backup job and verify a new non-empty archive before closing the incident.
