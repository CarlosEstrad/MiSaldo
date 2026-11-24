#!/bin/sh
# Runs once, when the PostgreSQL container starts with an empty data volume.
#
# Creates misaldo_app, the role the API connects with. It is not a superuser
# and does NOT bypass Row Level Security, so every per-user policy applies to
# it (RNF-SEG-04), exactly as in the tests. The migrations run as the
# container's admin user (POSTGRES_USER); the default privileges below give
# misaldo_app access to every table and sequence those migrations create.
set -eu

if [ -z "${MISALDO_APP_PASSWORD:-}" ]; then
  echo "MISALDO_APP_PASSWORD is not set" >&2
  exit 1
fi

psql -v ON_ERROR_STOP=1 \
  --username "$POSTGRES_USER" \
  --dbname "$POSTGRES_DB" \
  --set app_password="$MISALDO_APP_PASSWORD" <<'SQL'
CREATE ROLE misaldo_app LOGIN PASSWORD :'app_password' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;

GRANT CONNECT ON DATABASE misaldo TO misaldo_app;
GRANT USAGE ON SCHEMA public TO misaldo_app;

-- Tables and sequences the admin creates from now on (the migrations).
ALTER DEFAULT PRIVILEGES FOR ROLE CURRENT_USER IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO misaldo_app;
ALTER DEFAULT PRIVILEGES FOR ROLE CURRENT_USER IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO misaldo_app;
SQL

echo "misaldo_app created (NOSUPERUSER, NOBYPASSRLS)."
