-- Yalnız hedef firmanın kontrollü provisioning oturumunda, psql -v ON_ERROR_STOP=1 ile çalıştırılır.
-- database_name, instance_id ve target_password psql değişkenleri; parola konsola/loga yazılmaz.
SELECT current_database() = :'database_name' AS correct_database \gset
\if :correct_database
\else
  \echo 'Yanlış veritabanı; hiçbir rol veya ayar değiştirilmedi.'
  \quit 3
\endif
BEGIN;
SELECT set_config('provision.instance_id', :'instance_id', true);
DO $$
DECLARE existing text;
BEGIN
  SELECT substring(setting FROM length('app.subscription_instance=')+1) INTO existing
    FROM pg_db_role_setting s CROSS JOIN LATERAL unnest(s.setconfig) setting
    WHERE s.setdatabase=(SELECT oid FROM pg_database WHERE datname=current_database())
      AND s.setrole=0 AND setting LIKE 'app.subscription_instance=%';
  IF existing IS NOT NULL AND existing <> current_setting('provision.instance_id') THEN
    RAISE EXCEPTION 'Firma kimliği farklı; hedef yeniden etiketlenmedi';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_db_role_setting s CROSS JOIN LATERAL unnest(s.setconfig) v
    WHERE s.setdatabase=(SELECT oid FROM pg_database WHERE datname=current_database()) AND v LIKE 'app.operator_identity=%') THEN
    RAISE EXCEPTION 'Operatör kimlik DB si firma hedefi yapılamaz';
  END IF;
  IF to_regclass('public."SubscriptionPeriods"') IS NULL OR to_regclass('public."SubscriptionPayments"') IS NULL THEN
    RAISE EXCEPTION 'P07-02 şeması eksik';
  END IF;
END $$;
SELECT format('CREATE ROLE subscription_operator LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS', :'target_password')
  WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='subscription_operator') \gexec
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='subscription_operator' AND (rolsuper OR rolcreatedb OR rolcreaterole OR rolbypassrls))
    OR EXISTS (SELECT 1 FROM pg_auth_members WHERE member='subscription_operator'::regrole) THEN
    RAISE EXCEPTION 'Operatör rolünde geniş yetki var; otomatik değiştirilmedi';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_tables WHERE schemaname='public' AND tablename NOT IN ('SubscriptionPeriods','SubscriptionPayments')
    AND has_table_privilege('subscription_operator', format('%I.%I', schemaname, tablename), 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE')) THEN
    RAISE EXCEPTION 'Operatör rolünde kapsam dışı tablo yetkisi var; otomatik değiştirilmedi';
  END IF;
END $$;
SELECT format('ALTER DATABASE %I SET app.subscription_instance = %L', current_database(), :'instance_id') \gexec
GRANT CONNECT ON DATABASE :"database_name" TO subscription_operator;
GRANT USAGE ON SCHEMA public TO subscription_operator;
GRANT SELECT, INSERT ON "SubscriptionPeriods", "SubscriptionPayments" TO subscription_operator;
GRANT UPDATE ("Version") ON "SubscriptionPeriods" TO subscription_operator;
REVOKE DELETE, TRUNCATE ON "SubscriptionPeriods", "SubscriptionPayments" FROM subscription_operator;
REVOKE UPDATE ON "SubscriptionPayments" FROM subscription_operator;
COMMIT;
