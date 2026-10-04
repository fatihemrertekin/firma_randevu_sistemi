---
version: 1
slug: weeklyhourseditor-tsx
primary_target: src/Web/src/components/WeeklyHoursEditor.tsx
related_targets: [src/Web/src/components/WeeklyHoursEditor.module.css,src/Web/src/features/business/BusinessHours.tsx,src/Web/src/features/staff/StaffHoursEditor.tsx]
status: implemented
---

# T02 — İşletme ve personel saatleri

Mode: operate. Onaylı taslak ve uygulanmış T02 kapsamı; GitHub/ana yerel teslim kanıtı P02 planında ayrı tutulur.

- Amaç: MFA Owner haftanın yedi gününü, kapalı/çalışmıyor durumunu ve aynı gün içindeki tek saat aralığını kolayca düzenler.
- Ana işlem: bütün haftayı mevcut sürümle kaydet; başarı yalnız sunucu 200 yanıtından sonra görünür. Güncel saatleri yükle eylemi taslağı silmeden önce onay ister.
- Bilgi sırası: görev başlığı ve personelde seçili kişi → Türkiye saati/tek aralık açıklaması → yedi gün → kaydedilmemiş değişiklik/hata/başarı → kaydet/yükle; personelde Listeye dön.
- Mobil: mevcut Menü kapalı başlar; her gün adı ve durum seçimi üstte, iki etiketli saat alanı altta; eylemler tek sütun. 320 px'te taşma olmadan 44 px hedefler ve görünür klavye odağı.
- API: işletmede GET/POST `/api/business-hours/`, personelde GET/POST `/api/staff-members/{id}/hours`. DTO `{version,days:[{day,isClosed,opensAt,closesAt}]}` değişmez; personel sürümü, aktiflik ve CSRF/Owner yetkisi korunur.
- Durumlar: yükleme; ilk kullanımda kaydedilmiş saat yok (kapalı seçimler yalnız taslak); alan hatası 400; oturum/yetki 401/403; personel yok 404; sürüm/pasiflik 409; Retry-After 429; belirsiz sonuçta yeniden yükleme; kaydetme/başarı/kaydedilmemiş değişiklik/pasif personel salt okunur görünümü.

## Tek tasarım önerisi

T01'de uygulanmış krem zemin, lacivert metin, petrol gezinme, turuncu ana işlem ve mevcut sistem yazısı miras alınır. Yeni fotoğraf, raster üretim varlığı veya tasarım dünyası eklenmez. Masaüstünde yedi gün ayrı kartlar yerine ince çizgili ortak sütunlarda görünür: Gün / Durum / Açılış / Kapanış. Kapalı günde saat alanları kalkar, “Saat girişi gerekmiyor” metni görünür. Mobilde aynı sıra her güne ait iki saat alanıyla alt alta uyarlanır.

Personelde aynı düzen ve bileşen kullanılır: kişinin adı görünür; durum “Çalışmıyor”, alanlar “Başlangıç / Bitiş” olur. Pasif personelin kayıtlı saatleri salt okunur kalır, mevcut açıklama ve Listeye dön korunur. T03 personel listesi/ayrıntı tasarımı bu işte yeniden kurulmaz.

Saat kopyalama/toplu uygulama, çok aralık, mola/istisna/izin, gece taşan saat, randevu/takvim, yeni API/migration/bağımlılık, logo ve dark mode kapsam dışıdır. İlk kullanımda taslaktaki örnek 09:00–19:00 saatleri varsayılan yapılmaz; gerçek API verisi kullanılır. Kapalı gün saatleri DTO'da null kalır; bitiş aynı gün başlangıçtan sonra olmalıdır.

## Onay ve görsel kaynak

Ürün sahibinin ilk 04.10.2026 “Onaylıyorum” yanıtı T02 taslağına, taslak sunulduktan sonraki ayrı “Onaylıyorum” yanıtı işletme/personel saatleri koduna yetki verdi. Önceki ek görsel/motor turlarıyla vakit kaybedilmemesi tercihi sürer. Impeccable shape/operate/craft-floor ilkeleri ve mevcut DESIGN uygulanır; yeni seçenek turnuvası veya native kapı turu yapılmaz.

Onaylı taslak: `.impeccable/mocks/decision/weekly-hours-desktop-mobile.png`; örnek saatler gerçek işletme verisi değildir. Metin ve kontroller CSS Modules/semantik React ve merkezi tokens.css ile uygulandı.

Görsel inceleme: yedi gün ve kapalı Pazar ayrımı, hizalı masaüstü sütunları, mobilde gün/saat alanlarının sırası ve eylemler mevcut sisteme uygundur. Rasterda gösterilen küçük mobil kontroller gerçek CSS ölçüsü değildir; uygulamada 44 px hedef ve 48 px alan tokenları korunacaktır. Aynı düzenin personel bağlamında başlık/etiket/geri dönüş uyarlaması bu brief'e göre yapılır. Ek görsel varyantı gerekmedi; PNG içinde exact prompt geri okuma doğrulandı. Taslak sunucu verisi veya mobil/API kabul kanıtı değildir.

## Uygulama ve yerel kabul

Typecheck/lint, mevcut 196/196 web testi ve build geçti. Ayrı geçici PostgreSQL ve sentetik MFA Owner/Staff ile gerçek Chrome/API: işletme/personel kayıt 200, kapalı gün null saat, geçersiz istek 400, iki bağlamda sürüm çatışması ve pasif personel 409, anonim 401, Staff okuma/yazma 403, yenileme/gezinme taslak koruması ve alan hatasında odak doğrulandı. Mevcut testler 429, belirsiz hata ve çift gönderim kilidini de kapsar; bunlar gerçek tarayıcıda ayrıca 429 üretildiği anlamına gelmez.

320/390/768/1280 ve ek 1536 px'te ilk kullanım/taslak/kayıt/çatışma/personel salt okunur görüntüleri alındı. Yatay taşma/sayfa hatası yok; görünür düğmeler ve checkbox etiketlerinin tıklama alanı en az 44 px. Bir görsel inceleme grubunda onaylı yapı, iki saat alanı, sütun hizası, pasif durum/odak ve geri bildirim incelendi; ek raster turu gerekmedi. Dar personel alanı düzenleyicinin kendi genişliğine göre uyarlanır. Ortak azaltılmış hareket/odak kuralları miras alınır; tam WCAG sertifikasyonu veya native finish kabulü değildir. Kanıt: `.local/weekly-hours-design/browser-report.json` ve PNG'ler; ana 8080 verisine giriş/yazma yapılmadı. Yalnız T02 uygulandı; T03 başlamadı.
