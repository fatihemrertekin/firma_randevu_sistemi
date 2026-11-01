-- Yalnız yeni, kapalı, tek sahte operatör kurtarma hedefinde çalıştırılır.
-- Güncel iptal kaynağı yoksa eski yedek kimlik doğrulama yetkisi veremez.
BEGIN;
DO $$
DECLARE expected text := current_setting('recovery.expected_operator', true);
BEGIN
    IF current_database() <> 'operator_identity' OR expected IS NULL OR expected = '' OR
        NOT EXISTS (SELECT 1 FROM pg_db_role_setting s CROSS JOIN LATERAL unnest(s.setconfig) v
            WHERE s.setdatabase=(SELECT oid FROM pg_database WHERE datname=current_database())
                AND s.setrole=0 AND v='app.operator_identity=' || expected) OR
        (SELECT array_agg(tablename::text ORDER BY tablename COLLATE "C") FROM pg_tables WHERE schemaname='public') IS DISTINCT FROM
            ARRAY['AspNetRoleClaims','AspNetRoles','AspNetUserClaims','AspNetUserLogins','AspNetUserRoles','AspNetUserTokens','AspNetUsers'] OR
        (SELECT count(*) FROM "AspNetUsers") <> 1 OR
        NOT EXISTS (SELECT 1 FROM "AspNetUsers" WHERE "Email"='operator@example.test') THEN
        RAISE EXCEPTION 'Yalnız doğru işaretli, tek sahte hesaplı operatör kurtarma hedefi kabul edilir';
    END IF;
END $$;
UPDATE "AspNetUsers"
SET "SecurityStamp"=gen_random_uuid()::text, "ConcurrencyStamp"=gen_random_uuid()::text,
    "PasswordHash"=NULL, "TwoFactorEnabled"=false, "LockoutEnabled"=true,
    "LockoutEnd"=TIMESTAMPTZ '9999-12-31 23:59:59+00';
DELETE FROM "AspNetUserTokens";
COMMIT;
