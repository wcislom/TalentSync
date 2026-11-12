#!/bin/sh
# Entry point of the migrations image, run by the Helm hook Job before any host rolls out (ADR 011): creates the
# database if needed, then applies the idempotent script of each schema.
# SQL_SERVER, SQL_DATABASE, SQL_USER from the environment; sqlcmd reads the password from SQLCMDPASSWORD itself.
# SQL_TRUST_SERVER_CERTIFICATE=true only for a local SQL Server with its self-signed certificate.
set -eu

trust=""
if [ "${SQL_TRUST_SERVER_CERTIFICATE:-false}" = "true" ]; then
  trust="-C"
fi

# -b: stop at the first failing batch with a non-zero exit code, so the Job fails and Helm stops the upgrade.
# -I: QUOTED_IDENTIFIER ON. sqlcmd defaults to OFF, unlike ADO.NET, and filtered indexes fail without it.
run() {
  /opt/mssql-tools18/bin/sqlcmd -S "$SQL_SERVER" -U "$SQL_USER" $trust -b -I "$@"
}

run -Q "IF DB_ID(N'$SQL_DATABASE') IS NULL CREATE DATABASE [$SQL_DATABASE]"
for schema in ingress sync matching; do
  echo "Applying $schema"
  run -d "$SQL_DATABASE" -i "/migrations/$schema.sql"
done
