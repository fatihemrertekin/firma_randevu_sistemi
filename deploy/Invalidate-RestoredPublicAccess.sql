-- Yalnız restore edilmiş DB'de, erişim ve OTP kapıları kapalıyken migration rolüyle çalıştırılır.
-- Dönülen eski bağlantılar ve tüketilmiş makbuzlar yeniden yetki üretemez; tarihçe korunur.
BEGIN;
UPDATE "PublicAppointmentAccesses"
SET "TokenHash" = upper(encode(sha256(uuid_send(gen_random_uuid()) || uuid_send(gen_random_uuid()) || uuid_send(gen_random_uuid())), 'hex')),
    "Version" = gen_random_uuid();
UPDATE "BookingVerifications"
SET "SecretHash" = upper(encode(sha256(uuid_send(gen_random_uuid()) || uuid_send(gen_random_uuid()) || uuid_send(gen_random_uuid())), 'hex'))
WHERE "AppointmentId" IS NOT NULL;
COMMIT;
