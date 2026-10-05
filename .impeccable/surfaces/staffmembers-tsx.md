---
version: 1
slug: staffmembers-tsx
primary_target: src/Web/src/features/staff/StaffMembers.tsx
related_targets: [src/Web/src/features/staff/StaffMemberEditor.tsx,src/Web/src/features/staff/StaffServicesEditor.tsx,src/Web/src/features/staff/StaffHoursEditor.tsx,src/Web/src/app/ManagementLayout.tsx]
status: implemented
---

# T03 — Personel listesi ve seçili personel

Mode: operate. Onaylı T03 ekranı uygulandı; yerel kabul aşağıda, GitHub/ana teslim P02 durum kaydındadır.

- Amaç: MFA Owner hizmet veren kişiyi listeden seçer, adı/durumu, hizmetleri ve saatlerini aynı kişi bağlamında yönetir. Personel kaydı giriş hesabı oluşturmaz.
- Ana işlem: listede Yeni personel; her satırda yalnız Ayrıntılar. Seçili kişide Bilgiler / Hizmetler / Saatler görevlerinden biri açıktır; kayıt yalnız mevcut sunucu yanıtıyla başarı olur. Pasifleştir/aktifleştir ayrı onaydır.
- Bilgi sırası: Personel → ad/durum/satır eylemi/sayfalama; ayrıntıda listeye dönüş → seçili ad/durum → görev seçimi → ilgili form → geri bildirim/kaydet/yükle. Ad ile aktiflik aynı gönderime birleştirilmez.
- Mobil: mevcut Menü kapalı; liste satırı ad/durum ve tek eylem olarak düzenlenir. Ayrıntıda üç kısa görev düğmesi tek sırada, form/eylemler tek sütundur. 320 px, en az 44 px tıklama alanı, görünür odak ve uzun adların satır kırması gerekir.
- API: mevcut GET/POST `/api/staff-members/`, GET/POST `/{id}`, POST `/{id}/status`, GET/POST `/{id}/services`, GET/POST `/{id}/hours`. Mevcut DTO/Owner/MFA/CSRF/sürüm/idempotent oluşturma ve personel-hizmet aktiflik kuralları korunur.
- Durumlar: yükleme/boş liste/boş hizmet/saatler belirlenmemiş; alan hatası 400; oturum/yetki 401/403; silinmiş/bulunamayan kayıt 404; sürüm/aktiflik 409; 429; belirsiz sonuç; kaydetme/başarı; kaydedilmemiş değişiklik ve pasif kayıt ayrı görünür.

## Tek düzen ve akış

T01/T02 ortak krem/lacivert/petrol/turuncu paleti ve sistem sans kontrol dili miras alınır; Unna yalnız Randevu metnindedir. Fotoğraf, avatar, logo, yeni istatistik veya kart dünyası eklenmez. Liste ince çizgili ad/durum/tek Ayrıntılar satırıdır. Tekrarlanan Personel listesi başlığı ve her satırdaki dört eylem yerini açık liste → ayrıntı akışına bırakır; işlem kaybolmaz.

Ayrıntıda listede seçilen kişi adı ve Aktif/Pasif yazısı görünür. Bilgiler adı düzenler; durum değişikliği ayrı bölüm ve ayrı onaydır. Hizmetler mevcut sayfalı seçimleri ve tüm sayfaların seçili kümesini yönetir; pasif personel/hizmette eski ilişki korunabilir veya kaldırılabilir, yeni ilişki yasaktır. Saatler T02'nin onaylı düzenleyicisini kullanır; pasif personelde salt okunur kalır. Ad kaydı, hizmet kaydı ve saat kaydı ayrı API işlemleridir; bütün personeli tek atomik kaydetme vaadi yoktur.

Kaydettikten sonra seçili kişinin bağlamı korunur; yeni personel oluşturulduğunda sunucunun döndürdüğü kayıt seçilir. Listeye dönüş bulunduğu sayfayı korur; kayıt oluşturma/liste değişiminde mevcut sayfalama kuralı doğrulanır. İlgili görev açıldığında ve sürüm artıran kayıt/durum işleminden sonra mevcut GET ile güncel kişi/sürüm yüklenir; önceki liste DTO'su güncel kabul edilmez. Aynı anda üç form çalıştırılmaz. Görev/kişi/liste/çıkış geçişi kaydedilmemiş taslağı silmeden onay ister; bekleyen istekte geçiş ve çift gönderim kilitlidir. Vazgeç ve yeniden yükle ayrı anlam taşır; odak açılan göreve veya dönüşte önceki satıra gider.

Ad 1–100 karakterdir; kontrol karakteri reddedilir. Personel listesi mevcut 20 kayıtlık sayfaları, hasMore bilgisini kullanır; toplam personel sayısı/arayıcı/filtre eklenmez. Hizmetler mevcut 10 kayıtlık UI sayfası ve en fazla 500 seçim sınırını korur. Giriş hesapları/oturumlar personel aktifliğiyle değişmez; çalışan erişimleri ayrı mevcut bölümdür. API'nin sağlamadığı hizmet sayısı/saat özeti listede uydurulmaz.

## Onay ve kaynak

04.10.2026, T02 tesliminden sonra ürün sahibinin “Onaylıyorum” yanıtı T03 taslak hazırlığını onayladı. Taslak sunulduktan sonraki ayrı “Onaylıyorum” yanıtı görseli ve bu brief'in ekran kodu kapsamını onayladı. Kullanıcının ek görsel/native turlarıyla vakit kaybedilmemesi tercihi sürer: tek yerleşimin liste/ayrıntı/masaüstü/mobil taslağı hazırlandı, yeni tasarım seçeneği turnuvası veya native faz yeniden başlatması yapılmaz. Mevcut Impeccable shape/operate ilkeleri uygulanır; native finish tamamlandı sayılmaz.

Görsel kaynak: DESIGN.md, uygulanmış ortak yönetim alanı ve `.local/weekly-hours-design/staff-saved-1280.png` sentetik gerçek API ekranı. Tek taslak `.impeccable/mocks/decision/personnel-list-detail-desktop-mobile.png` altında; exact üretim prompt'u PNG içinde ve yan JSON'dadır, geri okuma eşitliği doğrulandı. Örnek adlar gerçek kişi verisi değildir. Taslak başlık/sekme/işlem yönünü anlatır; üretim ölçüleri veya API kabulü değildir.

Görsel inceleme: liste tek eylemle taranıyor, seçili kişi ve görev geçişleri mobilde okunur, ad kaydı ile aktiflik ayrıdır. Taslaktaki yeşil durum noktası yeni renk tokenı kararı değildir; üretimde mevcut paletteki işaret ve Aktif/Pasif metni kullanılacaktır. Masaüstündeki kaydedilmemiş değişiklik metni yalnız gerçekten değişen taslakta görünür; aynı adla kayıt durumunda gösterilmez. Raster kontrollerin ölçeği 44 px kabul kanıtı değildir; gerçek ölçüler kod sonrası kontrol edilecektir. Onaylı kabuk, T02 saat düzeni ve semantik kontroller korunarak bu noktalar uygulanır; ek raster turu gerekmedi.

## Kod onayından sonraki kabul

Typecheck/lint/test/build; gerçek sentetik MFA Owner ile oluşturma/ad/durum/hizmet/saat kayıtları, sayfalama, pasif eski ilişki koruma/kaldırma ve yeni ilişki reddi, 400/409/401/403, güncel ortak sürüm ve taslak koruması. Mevcut 429/belirsiz hata/çift gönderim testleri korunur. 320/390/768/1280 görüntüleri; uzun ad, boş/yükleme/hata/başarı/pasif durum, görev geçişi, klavye/odak, tıklama alanı/taşma ve azaltılmış hareket incelenir. Mock bunların kanıtı olmaz.

Yeni backend, migration, bağımlılık, personel fotoğrafı/logo, arama/filtre/istatistik, randevu/P03, dark mode, T04 hizmet tanımı ekranı ve T06 giriş hesabı akışları kapsam dışıdır. Kod onayından sonra yalnız T03 uygulanır.

## Uygulama kabulü — 04.10.2026

Liste/oluşturma/ayrıntı ve tek açık görev uygulandı; gerçek kayıt sonrasında seçili kişi/görev korunur. Bilgiler/Hizmetler vazgeçmesi aynı görevde sunucu kaydına döner; Saatler içindeki T02 Listeye dön eylemi ve üstte ortak liste dönüşü korunur. Yeni kayıttan dönüş odağı Yeni personel, satırdan dönüş odağı açan satırdır; liste sayfası korunur. GET/POST sürerken görev/liste/ana gezinme/çıkış ve çift gönderim kilitlidir. Yeni backend/migration/bağımlılık yoktur.

Typecheck/lint, 203/203 Vitest ve build geçti. Ayrı sentetik PostgreSQL/kısıtlı app_user ve MFA Owner ile gerçek Chrome/API: oluşturma 201; ad/hizmet/saat/durum 200; hizmet ve saat kaydı sonrası ad kaydında ortak yeni sürüm; alan 400; eşzamanlı ad değişimi 409/taslak koruma/güncel başlık; pasif eski ilişki koruma/kaldırma 200 ve yeni ilişki 409; pasif saat salt okunur; anonim okuma/yazma 401, Staff okuma/yazma 403. İkinci sayfaya ve satır odağına dönüş, görev/liste/ana gezinme taslak koruması ve çıkış doğrulandı. 429/belirsiz sonuç/çift gönderim/yükleme kilidi ayrı web regresyonlarıdır; hız sınırı değiştirilmedi.

320/390/768/1280 gerçek ekranlarında boş liste, bilgiler, taslak, çatışma, hizmetler, saatler, pasif/salt okunur, dolu liste ve 100 karakter ad incelendi. Taşma/sayfa hatası yok, görünür düğmeler ve checkbox etiketleri en az 44 px; azaltılmış hareketle mobil Menü/Escape odak dönüşü geçti. Ortak düğme stilinin görev alt çizgisini ve geri dönüş boşluğunu ezmesi tek düzeltme grubunda giderildi. Krem/lacivert/petrol/turuncu, tek satır eylemi, ortak kimlik ve T02 saat düzeni taslakla uyumlu. 320 px'te saatlerin uzun dikey akışı doğal kaydırılır; tam WCAG/native finish veya fiziksel cihaz kabulü değildir. Yerel raporlar ve PNG'ler `.local/personnel-design/` içinde Git dışıdır.

## Kullanıcı düzeltmesi — 06.10.2026

Personel listesine dönüş dolu buton yerine sola oklu, altı çizili Personel listesi bağlantısıdır; mevcut sayfa/taslak/bekleyen işlem koruması sürer. Kullanıcının mevcut ekran üzerinde doğrudan istediği dar düzenlemedir; yeni ekran/taslak veya başka aşama başlatılmaz. Kabul kanıtı P02 planındaki mevcut yüzey düzeltmeleri kaydına eklenir.
