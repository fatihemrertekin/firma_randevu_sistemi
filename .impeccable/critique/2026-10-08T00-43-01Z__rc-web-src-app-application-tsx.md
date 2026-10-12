---
target: Bütün mevcut frontend
total_score: 28
max_score: 40
na_heuristics: 
p0_count: 0
p1_count: 1
target_identity: "file:C:\\Users\\MONSTER\\Desktop\\YAZILIM PROJELERİM\\firma_randevu_sistemi\\src\\Web\\src\\app\\Application.tsx"
target_fingerprint: "sha256:9f4b40600a873a03b1a64ff4eb47678c77b63008c65976fe67b9c3048ad23fb7"
target_path: "C:\\Users\\MONSTER\\Desktop\\YAZILIM PROJELERİM\\firma_randevu_sistemi\\src\\Web\\src\\app\\Application.tsx"
timestamp: 2026-10-08T00-43-01Z
slug: rc-web-src-app-application-tsx
closed: true
---
# Bütün frontend critique — 08.10.2026

İki bağımsız değerlendirme: /root/design_review (A: tasarım ve akış), /root/detector_review (B: tek detector ve native tarayıcı). A detector ve B bulgularını görmeden tamamlandı. Toplam 28/40; P0 yok.

Kapsam: giriş, MFA uygulama/kurtarma/kurulum/tek gösterim; Owner bağlantı ve manuel parola sıfırlama, Staff sıfırlama, davet kabulü, e-posta doğrulama; işletme profili/saatleri; personel liste/yeni/bilgiler/hizmetler/saatler; hizmet liste/yeni/düzenle; çalışan erişimleri/davetler/manuel reset; hesap e-postası/MFA kod yenileme/parola; audit; bilinmeyen ve yetkisiz route. Hepsi kaynakta incelendi. 20 route × 320/390/768/1280 px screenshot, ayrıca bağımsız native 49 örnek/418 kontrol. Özel token/400/409/429/pasif/boş/kurulum durumlarının tamamı bu ilk turda canlı kabul sayılmıyor.

| Nielsen ilkesi | Puan |
|---|---:|
| Sistem durumu |3/4|
| Gerçek iş dili |3/4|
| Kullanıcı kontrolü |3/4|
| Tutarlılık |3/4|
| Hataları önleme |3/4|
| Tanıma |3/4|
| Verim |2/4|
| Estetik ve minimalizm |3/4|
| Hata sonrası toparlanma |3/4|
| Yardım |2/4|

Güçlü taraflar: liste/düzenleme görevleri ayrılmış, kaydedilmemiş taslaklar korunuyor, yazılı durumlar renk yanında mevcut. Sektöre uygun salon fotoğrafı, ölçülü Unna ve açık palet mevcut dünyaya uyuyor; yeniden markalama gerekmiyor.

## Öncelikler

1. P1 Kimlik görevi değişince odak BODY'de kalıyor. AuthenticationScreens title etkisi yalnız belge başlığını değiştiriyor. Yeni görev h1'ine odak taşı; ilk inputu seçme. Canlı teyit.
2. P2 Kabuk CSS'i işlem rollerini eziyor: yardımcı giriş linkleri, Yeni personel ve Staff Pasifleştir dolu lacivert. Aynı kontrol intent'i üçten çok yüzeyde mevcut; ortak temel kuralları düşük seçicilikle paylaş ve mevcut primary/danger/navigation rollerini koru. Canlı teyit.
3. P2 PasswordResetForm ve StaffInvitationAcceptForm eşleşme hatası tekrar alanıyla ilişkisiz. aria-invalid/aria-describedby ve görünür alan hatası ekle; sır temizleme davranışını koru. Kaynakta doğrulandı.
4. P2 StaffInvitations ilk GET hata dalında boş liste mesajı da gösteriliyor. Başarılı yükleme ile boş sonuç ayır, retry ve önceki listeyi koru. Kaynakta doğrulandı.
5. P2 ServiceRouteEditor geri linkinde readPageSize kullanılmıyor; 50 kayıttan düzenle/dön sonrası boyut20 oluyor. Mevcut pageSearch'e boyutu da aktar. Kaynakta doğrulandı.

Bilişsel yük: kimlik ve kayıt düzenleme tek görevde güçlü. Hesap/çalışan erişimleri uzun; single focus, working memory ve progressive disclosure zayıf. Yedi gün ve hizmet checkbox'ları doğal veri setidir, keyfi azaltılmaz. Mevcut görevleri geniş alanda grupla, güvenlik açıklamalarını koru.

Duygu akışı: sakin giriş, güvenlik adımlarında gerekli öğrenme yükü, hatada alan bulma zorluğu; sunucu sonrası net başarı güçlü son an. Kurtarma kodlarını saklama uyarıları korunur.

Personalar: Alex liste boyutu kaybıyla bağlamı kaybediyor; Sam kimlik odağı ve alan ilişkisi eksikliği yaşıyor; Casey mobil uzun formda kaydırıyor; Jordan davet hata+boş mesajından yanlış anlam çıkarabiliyor. İlk alan otomatik seçilmez.

Küçük gözlemler: adres alanı gereğinden yüksek; global/auth/management kontrol kuralları tekrarlı; DESIGN bazı eski tariflerle drift gösteriyor. Yeni QR/kopyalama işlevi, modül, bağımlılık ve yeniden tasarım gerekmiyor. Detector side-tab uyarısı mevcut yüzeylerde overridden alert fallback'idir; gerçek kart kusuru değildir, exception ekleme.

Ölçümlerde taşma ve 44px altı işlem hedefi yok, en düşük etkin kontrol metni kontrastı4.86:1. Tam WCAG sertifikası veya tüm hata dallarının runtime kabulü değildir.

Kapsam/öncelik/tasarım soruları kullanıcı tarafından bütün frontend, mevcut dünya ve critique→extract→polish sırası ile yanıtlandı; uygulamaya ayrıca sınırsız izin verildi. Aynı karar tekrar istenmez. Extract ortak kontrol/rol stillerini mevcut tokens/global sistemine taşır; polish beş önceliği ve dar yerleşim/ritim düzeltmelerini tamamlar.
