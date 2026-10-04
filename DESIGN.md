---
name: Randevu — açık kimlik yüzeyleri
description: Krem zemin, salon fotoğrafı ve sade kontrollerle işletme erişimi.
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

Bu kayıt, uygulanmış giriş, MFA, parola yenileme, davet kabulü ve e-posta doğrulama yüzeylerini tarif eder. Ürün sahibinin seçtiği palet, fotoğraflı yapı ve kontrollerde minimalizm esastır. Görsel kabul, API kabulü ve canlı teslim kanıtları durum/plan kayıtlarında ayrı tutulur; bu belge tasarım sisteminin tarifidir.

Yönetim ekranları bu dönüşümde yeniden tasarlanmadı. Açık kimlik teması `auth-theme` kapsamında uygulanır. Uygulamanın renk ve ölçü kaynağı `src/Web/src/styles/tokens.css` dosyasıdır; bu belge onun gözden geçirilmiş tarifidir.

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

Kimlik kontrollerinde gölge yoktur. Ayrım, boşluk ve ince sınırla sağlanır. Fotoğraf kendi ışığı ve gerçek mekân derinliğiyle görünür; düğmelerde yapay kabartma kullanılmaz.

## Shapes

Kontroller mütevazı yuvarlak köşelidir. İşlem hedefi en az 44 × 44 px'tir; metin eylemleri de aynı dokunma alanını sağlar. Ana işlem düz turuncudur, yardımcı işlem petrol metnidir.

## Components

**Düğmeler:** tek ana işlem belirgindir. Yardımcı eylemler gereksiz kutular taşımaz. Hover, active, disabled ve loading davranışı ortak yerleşim içinde tutarlıdır; 180 ms renk geçişleri azaltılmış hareket tercihinde kapanır.

**Alanlar:** görünür etiket, ince petrol sınır, hafif krem iç yüzey ve petrol odak çizgisi kullanılır. Hata mesajı kahverengi metinle ve ince sınırla gösterilir. API/CSRF/oturum kuralları görsel katman tarafından değiştirilmez.

Parola yenileme bağlantısı formunda hesap e-postası yazılabilir; e-posta gönderimi kullanılamıyorsa uyarı ve pasif gönderme düğmesi gösterilir. Kullanılabilirlik denetimi sunucuya aittir; e-posta kutusuna yazmak bağlantı gönderildiği anlamına gelmez. İstek sürerken alan ve eylemler kilitlenir.

**Fotoğraf:** onaylı `assets/plates/salon-background.png` gerçek bitmap olarak yüklenir. Mobilde zemine geçiş için krem örtü kullanılır; fotoğraf CSS resmiyle taklit edilmez. Yeni fotoğraf kullanıcı incelemesini yeniden gerektirir.

## Do's and Don'ts

- Do: Yeni kimlik ekranında ortak yerleşimi ve merkezi tokenları kullan.
- Do: Boş, yükleme, hata ve başarı durumlarını gerçek API yanıtlarına bağla.
- Do: Fotoğrafın üstündeki yazıyı okunabilir açık alanda tut; klavye odağını görünür bırak.
- Don't: Minimalizmi fotoğrafı kaldırmak veya tüm sayfayı boşaltmak olarak yorumlama.
- Don't: Gelecekteki modülü, istatistiği veya başarı mesajını uydurma.
- Don't: Bu değişikliği yönetim ekranlarının ya da dark mode'un tamamlandığı şeklinde gösterme.
