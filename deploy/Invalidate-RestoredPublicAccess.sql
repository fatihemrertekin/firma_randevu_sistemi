-- Yalnız restore edilmiş DB'de, erişim ve OTP kapıları kapalıyken migration rolüyle çalıştırılır.
-- Dönülen eski bağlantılar ve tüketilmiş makbuzlar yeniden yetki üretemez; tarihçe korunur.
BEGIN;
-- Dış sağlayıcıda harcanmış bakiye DB restore'u ile geri gelmez; yeniden onay öncesi uzlaştırma zorunludur.
UPDATE "BookingSmsBudgetPolicies" SET "RevokedAt" = clock_timestamp() WHERE "RevokedAt" IS NULL;
UPDATE "BookingSmsAttempts" SET "Outcome" = 'Unknown' WHERE "Purpose" = 'Reminder' AND "Outcome" = 'Started';
UPDATE "AppointmentReminderJobs"
SET "Status" = CASE WHEN "Status" = 'Processing' THEN 'Unknown' ELSE 'Suppressed' END,
    "ProtectedPhone" = NULL, "LeaseId" = NULL, "LeaseUntil" = NULL,
    "SuppressedAt" = COALESCE("SuppressedAt", clock_timestamp());
UPDATE "PublicAppointmentAccesses"
SET "TokenHash" = upper(encode(sha256(uuid_send(gen_random_uuid()) || uuid_send(gen_random_uuid()) || uuid_send(gen_random_uuid())), 'hex')),
    "Version" = gen_random_uuid();
UPDATE "BookingVerifications"
SET "SecretHash" = upper(encode(sha256(uuid_send(gen_random_uuid()) || uuid_send(gen_random_uuid()) || uuid_send(gen_random_uuid())), 'hex'))
WHERE "AppointmentId" IS NOT NULL;
COMMIT;
