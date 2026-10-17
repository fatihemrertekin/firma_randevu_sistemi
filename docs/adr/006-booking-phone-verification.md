# ADR-006 — Misafir telefon doğrulamasının sınırı

Tarih: 17.10.2026. Durum: P05-02 uygulamasında kabul edildi; teslim/kabul kanıtı [P05](../plans/P05.md) içindedir.

## İhtiyaç

Tek misafir talebi için telefon erişimini doğrulamak; paralel kota aşımı ve belirsiz SMS tesliminin tekrar maliyetini önlemek. Mevcut Owner kimliği/randevu zaman bloğu güvencesi korunmalı.

## Alternatifler ve karar

- Identity hesabı/genel login token'ı yerine talep özetiyle bağlı kısa ömürlü challenge. OTP müşteri geçmişine veya işletme onayına yetki vermez. Randevu yazımı bu aşamada yoktur.
- Process içi sayaç yerine PostgreSQL kota olayları/dönem sayaçları. Düşük pilot hacminde kısa OTP transaction'ları tek advisory kilitte sıralanır; personel/randevu kilitleri alınmaz, dış çağrı transaction dışındadır.
- Genel kuyruk veya e-posta worker'ını genişletmek yerine dar SMS outbox/worker. Timeout/bitmiş in-flight lease `Unknown` olur; kesin geçici ret dışında otomatik tekrar yoktur. Sağlayıcıya kabul alıcıya teslim değildir.
- Kod/talep/kota özetleri amaç/firma ayrılmış HMAC; challenge gizlisi kriptografik hash; gönderim zarfı ve resend alıcısı ayrı Data Protection amaçları kullanır. Alıcı zarfı challenge sona erene/doğrulanana/kilitlenene kadar gereklidir; işin kod/numara zarfı terminal durumda temizlenir. Aynı challenge'da eski altı rakam yeniden seçilmez.
- Gerçek sağlayıcı adaptörü yoktur. Özellik ana kurulumda kapalı; test sahtesi yalnız test DI kaydındadır. Enabled fakat taşıyıcısız/anahtarsız kurulum başlangıçta reddedilir. Kota satırındaki anahtar kimliği açılışta ve her OTP kilidinde doğrulanır; farklı geçerli anahtarla kota sıfırlanamaz.

## Bedel ve geri dönüş

Tek OTP kilidi yüksek hacimde darboğaz olabilir; ilk sınır 200 yeni iş/gün olduğundan daha karmaşık kilit düzeni eklenmedi. Ölçüm gerektirirse aynı yarış testleriyle kilit kapsamları daraltılır. İlk yanıtı kaybolan request secret'ı tekrar açıklamaz; yeni anahtar aynı kotalara tabidir. OTP saati bloke etmediğinden slot sonradan dolabilir.

Şema eklemelidir; kayıt/kota bulunan Down reddedilir. Gönderimi kapatıp test edilmiş eski imaja dönülür, yeni tablolar silinmez. Restore sonrası gönderim kapalıdır. Gerçek SMS ve rezervasyon tüketimi ayrı kabul gerektirir.
