-- Ayrı, önceden oluşturulmuş operatör kimlik DB'sinde psql -v ON_ERROR_STOP=1 ile çalıştırılır.
-- Firma DB'sinde çalıştırılmaz. database_name, instance_id, identity_password zorunludur.
SELECT current_database() = :'database_name' AS correct_database \gset
\if :correct_database
\else
  \echo 'Yanlış kimlik veritabanı; hiçbir ayar değiştirilmedi.'
  \quit 3
\endif
BEGIN;
SELECT set_config('provision.operator_instance', :'instance_id', true);
DO $$
DECLARE existing text;
BEGIN
  IF EXISTS (SELECT 1 FROM pg_tables WHERE schemaname='public' AND tablename NOT IN
      ('AspNetUsers','AspNetRoles','AspNetUserClaims','AspNetRoleClaims','AspNetUserRoles','AspNetUserLogins','AspNetUserTokens'))
    OR EXISTS (SELECT 1 FROM pg_db_role_setting s CROSS JOIN LATERAL unnest(s.setconfig) v
      WHERE s.setdatabase=(SELECT oid FROM pg_database WHERE datname=current_database()) AND v LIKE 'app.subscription_instance=%') THEN
    RAISE EXCEPTION 'Kimlik DB hedefinde başka ürün verisi/işaretçisi var; işlem yapılmadı';
  END IF;
  SELECT substring(v FROM length('app.operator_identity=')+1) INTO existing
    FROM pg_db_role_setting s CROSS JOIN LATERAL unnest(s.setconfig) v
    WHERE s.setdatabase=(SELECT oid FROM pg_database WHERE datname=current_database()) AND s.setrole=0 AND v LIKE 'app.operator_identity=%';
  IF existing IS NOT NULL AND existing <> current_setting('provision.operator_instance') THEN
    RAISE EXCEPTION 'Başka operatör kimliği yeniden etiketlenmedi';
  END IF;
END $$;
SELECT format('CREATE ROLE operator_identity_user LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS', :'identity_password')
  WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='operator_identity_user') \gexec
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='operator_identity_user' AND (rolsuper OR rolcreatedb OR rolcreaterole OR rolbypassrls))
    OR EXISTS (SELECT 1 FROM pg_auth_members WHERE member='operator_identity_user'::regrole) THEN
    RAISE EXCEPTION 'Operatör kimlik rolünde geniş yetki var; otomatik değiştirilmedi';
  END IF;
END $$;
SELECT format('ALTER DATABASE %I SET app.operator_identity = %L', current_database(), :'instance_id') \gexec
GRANT CONNECT ON DATABASE :"database_name" TO operator_identity_user;
GRANT USAGE ON SCHEMA public TO operator_identity_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO operator_identity_user;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO operator_identity_user;
COMMIT;
