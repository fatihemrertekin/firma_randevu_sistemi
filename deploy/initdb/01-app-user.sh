#!/usr/bin/env bash
set -euo pipefail

: "${APP_DB_PASSWORD:?APP_DB_PASSWORD gerekli}"

psql -v ON_ERROR_STOP=1 \
  --username "$POSTGRES_USER" \
  --dbname "$POSTGRES_DB" \
  --set=app_password="$APP_DB_PASSWORD" <<'SQL'
CREATE ROLE app_user LOGIN PASSWORD :'app_password';
GRANT CONNECT ON DATABASE firma_randevu TO app_user;
GRANT USAGE ON SCHEMA public TO app_user;
SQL
