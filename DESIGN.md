---
name: Randevu — kimlik ve yönetim yüzeyleri
description: Krem zemin, ölçülü salon fotoğrafı ve sade kontrollerle işletme erişimi ve yönetimi.
colors:
  action: "#FF7D00"
  petrol: "#15616D"
  warm-accent: "#78290F"
  cream: "#FFECD1"
  ink: "#001524"
typography:
  display:
    fontFamily: "Unna, serif"
    fontSize: "5rem"
    fontWeight: 700
    lineHeight: 1.08
    letterSpacing: "-.02em"
  title:
    fontFamily: "system-ui, sans-serif"
    fontSize: "2rem"
    fontWeight: 700
    lineHeight: 1.2
  body:
    fontFamily: "system-ui, sans-serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.5
  label:
    fontFamily: "system-ui, sans-serif"
    fontSize: "1rem"
    fontWeight: 600
    lineHeight: 1.5
rounded:
  control: ".375rem"
spacing:
  compact: ".5rem"
  control: ".75rem"
  regular: "1rem"
  group: "1.5rem"
  section: "2rem"
  panel: "3rem"
components:
  button-primary:
    backgroundColor: "{colors.action}"
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    padding: ".5rem 1rem"
    height: "3rem"
  button-secondary:
    backgroundColor: "{colors.cream}"
    textColor: "{colors.petrol}"
    rounded: "{rounded.control}"
    padding: ".5rem 1rem"
  input:
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    padding: ".5rem 1rem"
    height: "3rem"
---

# Randevu tasarım sistemi

## Overview

**Creative North Star: "Gün ışığında sade işletme erişimi"**

Bu kayıt, uygulanmış giriş, MFA, parola yenileme, davet kabulü, e-posta doğrulama, ortak yönetim alanı ve işletme profilini tarif eder. Ürün sahibinin seçtiği palet, fotoğraflı yapı ve kontrollerde minimalizm esastır. Görsel kabul, API kabulü ve canlı teslim kanıtları durum/plan kayıtlarında ayrı tutulur; bu belge tasarım sisteminin tarifidir.

Açık palet `auth-theme` ve `management-theme` kapsamında ortaktır; kompozisyonları ayrıdır. Yönetimde ortak gezinme, profil, işletme/personel haftalık saatleri ve personel liste/ayrıntısı uygulanmıştır; diğer bölümlerin ayrıntılı düzeni T04–T08 kapsamındadır. Uygulamanın renk ve ölçü kaynağı `src/Web/src/styles/tokens.css` dosyasıdır; bu belge onun gözden geçirilmiş tarifidir.

## Colors

### Primary

Turuncu ana işlem yüzeyini, lacivert ise bu yüzeyin okunabilir metnini taşır. Turuncu üzerinde beyaz veya krem yazı kullanılmaz.

### Secondary

Petrol, yardımcı eylemler ve odak için; sıcak kahverengi hata metni ve mevcut kısa tanıtım metni için kullanılır. Durum mesajı yalnız renkle anlatılmaz.

### Neutral

Krem baskın zemindir; lacivert ana metindir. Fotoğrafın doğal ahşap/taş tonları arayüz paletine yeni renk ekleme gerekçesi değildir. İlerideki dark mode için lacivert baskın zemin kararı PRODUCT'tadır; dark mode uygulanmış değildir.

## Typography

**İşlem önce gelir.** Form başlıkları, etiketler ve düğmeler sistem sans yazısıyla gösterilir. Yerel Unna dosyaları yalnız marka ve fotoğraflı karşılama başlığı içindir; lisansı font dosyalarının yanında bulunur.

Karşılama başlığı orta masaüstü genişliklerinde küçülür; bu, fotoğrafın koyu kısmına taşmayı önleyen ekran uyarlamasıdır. Marka, form başlığı, ana açıklama ve alt bilgi başlıkları taslaktaki belirgin yazı hiyerarşisini korur. Marka altındaki “İşletme paneli” satırı büyük harf ve geniş harf aralığıyla yazılır. Mobilde marka ve form başlığı ayrı ölçüler kullanır. Başlık bir görev talimatı gibi okunur; süs metni veya uydurma vaat eklenmez.

## Layout

Kimlik yüzeylerinin ortak yerleşimi `AuthenticationLayout` içindedir. Masaüstünde solda form, sağda geniş fotoğraf bulunur. Marka ve form aynı genişlik sınırına ve sol hizaya bağlıdır; geniş masaüstü ve tablette bağımsız hizalanmaz. Form ve bilgi grupları içerik akışında kalır; sabit yükseklik nedeniyle kesilmez.

900 px ve altında fotoğraf kısa üst alana dönüşür; marka bu alanda, form altta krem zeminde gösterilir. 320 px'te yardımcı eylemler ve onay düğmeleri alt alta gelir. Bu iki sütunlu kompozisyon diğer yönetim ekranlarına genel şablon olarak dayatılmaz.

Tüm kimlik formları aynı sıkı ölçüleri kullanır: marka 48 px, görev başlığı 32 px, gövde/etiket 16 px, ana işlem 18 px, alan yüksekliği 48 px ve form aralığı 8 px. Ölçüler merkezi tokenlardan gelir; ayrı form çeşitleri veya kopyalanmış stiller yoktur. Fotoğraf sütununun ölçeği korunur. Davet formundaki ana işlem ve geri dönüş masaüstünde aynı sırada, mobilde alt alta durur. Kısa mobil ekranda içerik gerektiğinde dikey kayar; alanlar veya hata mesajları kesilmez.

## Elevation & Depth

Yönetimde krem başlık, petrol dar grup menüsü ve kapanabilir krem alt menü vardır. İşletme/Ekip/Hesap grupları yalnız mevcut yetkili bölümlere götürür; değişiklik kayıtları ayrı bağlantıdır. Logo simgesi yoktur. Masaüstü marka ve görev başlığı 44 px; mobil başlık 32 px ve kontroller 48 px'tir. Yönetimin bütün ölçüleri merkezi tokenlarla tanımlanır.

Profil formu geniş masaüstünde küçük fotoğraflı bilgi özetiyle yan yanadır. 1280 px ve altında özet formun altına geçer; 900 px ve altında menü kapalı başlar, eylemler tek sütun olur, fotoğraf 112 px yüksekliğe iner. Taslak özeti değişen alanları gösterir; sunucu yüklemesi/kayıt sonrası profil özeti, belirsiz kayıt veya sürüm çatışmasında doğrulanmamış bilgiler olarak etiketlenir. Bölüm değişiminde profil taslağı korunur; yenileme ve çıkışta silme onayı gerekir.

Hata metni ve durum başlığı renkten bağımsızdır. İçeriğe geç bağlantısı, görünür odak, mobil Menü/Escape odak dönüşü ve azaltılmış hareket tercihi uygulanır. Fotoğraf yüklenmese de ana işlemler semantik form ve düğmelerde kalır.

Kimlik kontrollerinde gölge yoktur. Ayrım, boşluk ve ince sınırla sağlanır. Fotoğraf kendi ışığı ve gerçek mekân derinliğiyle görünür; düğmelerde yapay kabartma kullanılmaz.

## Shapes

Kontroller mütevazı yuvarlak köşelidir. İşlem hedefi en az 44 × 44 px'tir; metin eylemleri de aynı dokunma alanını sağlar. Ana işlem düz turuncudur, yardımcı işlem petrol metnidir.

## Components

**Düğmeler:** tek ana işlem belirgindir. Yardımcı eylemler gereksiz kutular taşımaz. Hover, active, disabled ve loading davranışı ortak yerleşim içinde tutarlıdır; 180 ms renk geçişleri azaltılmış hareket tercihinde kapanır.

**Alanlar:** görünür etiket, ince petrol sınır, hafif krem iç yüzey ve petrol odak çizgisi kullanılır. Hata mesajı kahverengi metinle ve ince sınırla gösterilir. API/CSRF/oturum kuralları görsel katman tarafından değiştirilmez.

Parola yenileme bağlantısı formunda hesap e-postası yazılabilir; e-posta gönderimi kullanılamıyorsa uyarı ve pasif gönderme düğmesi gösterilir. Kullanılabilirlik denetimi sunucuya aittir; e-posta kutusuna yazmak bağlantı gönderildiği anlamına gelmez. İstek sürerken alan ve eylemler kilitlenir.

**Haftalık saatler:** aynı semantik düzenleyici işletme ve personelde kullanılır. Geniş alanda dört hizalı sütun ve ince gün çizgileri, dar alanda gün/durum üstte iki etiketli saat alanı altta bulunur. Uyarlama ekran yerine düzenleyicinin kendi genişliğine göre yapılır; genişlik sınırı `--hours-editor-max` tokenıdır. Checkbox küçük kalır, etiketinin tıklama alanı 48 px yüksekliğindedir. Kapalı/çalışmayan gün saat alanları yerine yazılı açıklama gösterir. Kaydet/yükle ve personelde Listeye dön hafta sonunda yer alır; kaydedilmemiş değişiklik, hata ve sunucunun başarı mesajı eylemlerden önce görünür. Pasif personel saatleri salt okunur ve ad görünür kalır. Saat ekranında fotoğraf kullanılmaz.

**Fotoğraf:** kimlikte onaylı `assets/plates/salon-background.png`, yönetim profilinde `assets/plates/salon-photo.png` gerçek bitmap olarak yüklenir; exact üretim prompt'u ve kullanıcı onayı yan kayıttadır. Dekoratif üretilmiş salonlar gerçek işletme referansı değildir. Kimlikte mobil zemine geçiş için krem örtü kullanılır; yönetimde fotoğraf küçük özet alanına aittir. Fotoğraf CSS resmiyle taklit edilmez.

**Personel:** liste ad/durum/tek Ayrıntılar eylemini ince satır çizgileriyle ayırır; Yeni personel ana işlemdir. Seçili kişinin adı ve yazılı Aktif/Pasif durumu ortak başlıkta kalır. Bilgiler/Hizmetler/Saatler semantik görev gezinmesidir; seçili görev petrol alt çizgiyle ve basılı düğme durumu ile belirtilir. Bir form açıktır; kayıt sonrası aynı kişi/görev korunur, güncel sürüm sunucudan yüklenir. Ad kaydı ve durum onayı ayrıdır; giriş hesaplarının değişmediği açıklanır. Hizmet checkbox'ı küçük, tıklanabilir etiketi en az 44 px'tir. Mobilde satır eylemi/form düğmeleri alt alta; uzun ad satıra kırılır. Personel yüzeyinde fotoğraf/avatar/logo/uydurulmuş özet bulunmaz.

## Do's and Don'ts

- Do: Yeni kimlik ekranında ortak yerleşimi ve merkezi tokenları kullan.
- Do: Boş, yükleme, hata ve başarı durumlarını gerçek API yanıtlarına bağla.
- Do: Fotoğrafın üstündeki yazıyı okunabilir açık alanda tut; klavye odağını görünür bırak.
- Don't: Minimalizmi fotoğrafı kaldırmak veya tüm sayfayı boşaltmak olarak yorumlama.
- Don't: Gelecekteki modülü, istatistiği veya başarı mesajını uydurma.
- Don't: Bu değişikliği yönetim ekranlarının ya da dark mode'un tamamlandığı şeklinde gösterme.
