---
name: Randevu — kimlik ve yönetim yüzeyleri
description: Beyaza yakın zemin, dolu mavi/lacivert işlemler ve ölçülü salon fotoğrafıyla işletme erişimi ve yönetimi.
colors:
  action: "#4C72AD"
  navy: "#405775"
  error: "#A44D45"
  canvas: "#FAFBFD"
  surface: "#FFFFFF"
  ink: "#253044"
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
    textColor: "{colors.surface}"
    rounded: "{rounded.control}"
    padding: ".5rem 1rem"
    height: "3rem"
  button-secondary:
    backgroundColor: "{colors.navy}"
    textColor: "{colors.surface}"
    rounded: "{rounded.control}"
    padding: ".5rem 1rem"
  input:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    padding: ".5rem 1rem"
    height: "3rem"
---

# Randevu tasarım sistemi

## Overview

**Creative North Star: "Gün ışığında sade işletme erişimi"**

Bu kayıt, uygulanmış giriş, MFA, parola yenileme, davet kabulü, e-posta doğrulama, ortak yönetim alanı ve işletme profilini tarif eder. Ürün sahibinin seçtiği palet, fotoğraflı yapı ve kontrollerde minimalizm esastır. Görsel kabul, API kabulü ve canlı teslim kanıtları durum/plan kayıtlarında ayrı tutulur; bu belge tasarım sisteminin tarifidir.

05.10.2026 devam kararıyla bütün renk aileleri çok hafif pastel hale getirildi: ana mavi/lacivert, koyu metin, nötr zemin ve hata kırmızısı birlikte yumuşatıldı. Beyaz alanlar ve dolu butonlar korunur.

Kullanıcının dikey boşluk örneği 16 px işlem aralığına çevrildi; `--action-row-gap` merkezi kaynaktır. Kimlikte alan grupları/alan sonrası işlem ve form sonrası ayrı butonlar bu aralığı kullanır; etiket-alan bağı 8 px kalır. Mobil giriş seçenekleri ve satıra taşan işlem grupları da 16 px dikey aralık taşır.

05.10.2026 onayıyla giriş, MFA, diğer kimlik işlemleri ve mevcut yönetim ekranlarının tamamı beyaz/açık gri ve mavi/lacivert palete geçti. Önceki krem/turuncu taslaklar yerleşim kararının tarihsel kanıtıdır; güncel renk kaynağı değildir.

Açık palet `auth-theme` ve `management-theme` kapsamında ortaktır; kompozisyonları ayrıdır. Yönetimde ortak gezinme, profil, işletme/personel haftalık saatleri ve personel liste/ayrıntısı uygulanmıştır; diğer bölümlerin ayrıntılı düzeni T04–T08 kapsamındadır. Uygulamanın renk ve ölçü kaynağı `src/Web/src/styles/tokens.css` dosyasıdır; bu belge onun gözden geçirilmiş tarifidir.

## Colors

### Primary

Hafif pastel mavi ana işlem yüzeyidir; üzerindeki metin beyazdır. Normal, hover ve active durumları ortak buton tokenlarından gelir.

### Secondary

Hafif yumuşatılmış lacivert yardımcı işlemlerin dolu yüzeyidir; üzerindeki metin beyazdır. Hafif dolgulu gezinme/metin eylemlerinde lacivert yazı, odakta mavi çizgi kullanılır. Hata metni ve tehlikeli işlem kırmızıdır; durum mesajı yalnız renkle anlatılmaz.

### Neutral

Beyaza yakın açık gri baskın zemindir; alan yüzeyi beyaz, ana metin koyu laciverttir. Fotoğrafın doğal ahşap/taş tonları arayüz paletine yeni renk ekleme gerekçesi değildir. Dark mode uygulanmış değildir.

## Typography

**İşlem önce gelir.** Form başlıkları, etiketler ve düğmeler sistem sans yazısıyla gösterilir. Yerel Unna dosyaları yalnız marka ve fotoğraflı karşılama başlığı içindir; lisansı font dosyalarının yanında bulunur.

Karşılama başlığı orta masaüstü genişliklerinde küçülür; bu, fotoğrafın koyu kısmına taşmayı önleyen ekran uyarlamasıdır. Marka, form başlığı, ana açıklama ve alt bilgi başlıkları taslaktaki belirgin yazı hiyerarşisini korur. Marka altındaki “İşletme paneli” satırı büyük harf ve geniş harf aralığıyla yazılır. Mobilde marka ve form başlığı ayrı ölçüler kullanır. Başlık bir görev talimatı gibi okunur; süs metni veya uydurma vaat eklenmez.

## Layout

Kimlik yüzeylerinin ortak yerleşimi `AuthenticationLayout` içindedir. Masaüstünde solda form, sağda geniş fotoğraf bulunur. Marka ve form aynı genişlik sınırına ve sol hizaya bağlıdır; geniş masaüstü ve tablette bağımsız hizalanmaz. Form ve bilgi grupları içerik akışında kalır; sabit yükseklik nedeniyle kesilmez.

900 px ve altında fotoğraf kısa üst alana dönüşür; marka bu alanda, form altta beyaza yakın zeminde gösterilir. 320 px'te yardımcı eylemler ve onay düğmeleri alt alta gelir. Bu iki sütunlu kompozisyon diğer yönetim ekranlarına genel şablon olarak dayatılmaz.

Tüm kimlik formları aynı sıkı ölçüleri kullanır: marka 48 px, görev başlığı 32 px, gövde/etiket 16 px, buton metni 14 px/600, alan yüksekliği 48 px ve form aralığı 8 px. Buton hedefi en az 44 px, genişliği içeriğe göredir; ayrı işlem satırlarında 16 px dikey boşluk korunur. Ölçüler merkezi tokenlardan gelir; ayrı form çeşitleri veya kopyalanmış stiller yoktur. Fotoğraf sütununun ölçeği korunur. Davet formundaki ana işlem ve geri dönüş masaüstünde aynı sırada, mobilde alt alta durur. Kısa mobil ekranda içerik gerektiğinde dikey kayar; alanlar veya hata mesajları kesilmez.

## Elevation & Depth

Yönetimde beyaza yakın başlık, koyu lacivert dar grup menüsü ve kapanabilir açık alt menü vardır. İşletme/Ekip/Hesap grupları yalnız mevcut yetkili bölümlere götürür; değişiklik kayıtları ayrı bağlantıdır. Logo simgesi yoktur. Masaüstü marka ve görev başlığı 44 px; mobil başlık 32 px ve kontroller 48 px'tir. Yönetimin bütün ölçüleri merkezi tokenlarla tanımlanır.

Profil formu geniş masaüstünde küçük fotoğraflı bilgi özetiyle yan yanadır. 1280 px ve altında özet formun altına geçer; 900 px ve altında menü kapalı başlar, eylemler tek sütun olur, fotoğraf 112 px yüksekliğe iner. Taslak özeti değişen alanları gösterir; sunucu yüklemesi/kayıt sonrası profil özeti, belirsiz kayıt veya sürüm çatışmasında doğrulanmamış bilgiler olarak etiketlenir. Bölüm değişiminde profil taslağı korunur; yenileme ve çıkışta silme onayı gerekir.

Hata metni ve durum başlığı renkten bağımsızdır. İçeriğe geç bağlantısı, görünür odak, mobil Menü/Escape odak dönüşü ve azaltılmış hareket tercihi uygulanır. Fotoğraf yüklenmese de ana işlemler semantik form ve düğmelerde kalır.

Kimlik kontrollerinde gölge yoktur. Ayrım, boşluk ve ince sınırla sağlanır. Fotoğraf kendi ışığı ve gerçek mekân derinliğiyle görünür; düğmelerde yapay kabartma kullanılmaz.

## Shapes

Kontroller mütevazı yuvarlak köşelidir. İşlem hedefi en az 44 × 44 px'tir; metin eylemleri de aynı dokunma alanını sağlar. Ana işlem düz mavi, yardımcı işlem dolu laciverttir; her ikisinde beyaz yazı kullanılır.

## Components

**Düğmeler:** tek ana işlem belirgindir. Ortak metin 14 px/600, yatay iç boşluk 12 px, köşe 6 px, asgari hedef 44×44 px'tir. Genişlik metne göre belirlenir; gereksiz tam genişlik yardımcı işlemler kaldırılır. Grup menüsünün çok satırlı etiketi daha yüksek kalabilir. Yardımcı işlemler dolu lacivert, gezinme ve metin eylemleri hafif dolguludur; buton zemini transparan değildir. Hover, active, disabled ve loading davranışı ortak yerleşim içinde tutarlıdır; 180 ms renk geçişleri azaltılmış hareket tercihinde kapanır.

**Hizmetler:** tek başlık altında ad/dakika/TL fiyatı/yazılı durum sütunları ve satır başına tek Düzenle bağlantısı bulunur. Ekleme ve düzenleme ayrı URL/formdadır; form altında liste yoktur. Ad tam genişlik, süre/fiyat geniş alanda iki sütun, dar alanda tek sütundur. Kaydet/Vazgeç ve Güncel kaydı yükle form işlemleridir. Durum ve silme ayrı bölümde ve ayrı onayla yapılır; kirli formda veya bekleyen istekte kapalıdır. Durum işlemi sonrası GET ile güncel sürüm yüklenir, silme 204 sonrası listeye döner. Onaylı masaüstü/mobil öneri ve kapsam [T04 brief'inde](.impeccable/surfaces/services-tsx.md); buton stili mevcut diğer yüzeylerin görev düzenini değiştirmez.

**Alanlar:** görünür etiket, ince lacivert sınır, beyaz iç yüzey ve mavi odak çizgisi kullanılır. Hata mesajı kırmızı metinle ve ince sınırla gösterilir. API/CSRF/oturum kuralları görsel katman tarafından değiştirilmez.

Parola yenileme bağlantısı formunda hesap e-postası yazılabilir; e-posta gönderimi kullanılamıyorsa uyarı ve pasif gönderme düğmesi gösterilir. Kullanılabilirlik denetimi sunucuya aittir; e-posta kutusuna yazmak bağlantı gönderildiği anlamına gelmez. İstek sürerken alan ve eylemler kilitlenir.

**Haftalık saatler:** aynı semantik düzenleyici işletme ve personelde kullanılır. Geniş alanda dört hizalı sütun ve ince gün çizgileri, dar alanda gün/durum üstte iki etiketli saat alanı altta bulunur. Uyarlama ekran yerine düzenleyicinin kendi genişliğine göre yapılır; genişlik sınırı `--hours-editor-max` tokenıdır. Checkbox küçük kalır, etiketinin tıklama alanı 48 px yüksekliğindedir. Kapalı/çalışmayan gün saat alanları yerine yazılı açıklama gösterir. Kaydet/yükle ve personelde Listeye dön hafta sonunda yer alır; kaydedilmemiş değişiklik, hata ve sunucunun başarı mesajı eylemlerden önce görünür. Pasif personel saatleri salt okunur ve ad görünür kalır. Saat ekranında fotoğraf kullanılmaz.

**Fotoğraf:** kimlikte ve yönetim profilinde `assets/plates/salon-background-neutral.png` gerçek bitmap olarak yüklenir. Kullanıcı krem olmayan benzer salon fotoğrafı üretimine yetki verdi; tek görselde kompozisyon korunup beyaz/açık gri yüzeyler, nötr gün ışığı ve soluk mavi detaylar uygulandı. Exact prompt ve kaynak yan JSON kaydındadır; önceki fotoğraflar tarihsel özgünler olarak korunur. Dekoratif üretilmiş salon gerçek işletme referansı değildir. Kimlikte yazı bölgesinin okunurluğu güçlü beyaza yakın örtüyle, sağdaki fotoğraf görünürlüğü kademeli geçişle, mobil geçiş alttan zemine karışımla sağlanır; yönetimde mevcut küçük özet alanı ve fotoğraf oranı korunur. Fotoğraf CSS resmiyle taklit edilmez.

**Personel:** liste ad/durum/tek Ayrıntılar eylemini ince satır çizgileriyle ayırır; Yeni personel ana işlemdir. Seçili kişinin adı ve yazılı Aktif/Pasif durumu ortak başlıkta kalır. Bilgiler/Hizmetler/Saatler semantik görev gezinmesidir; seçili görev açık mavi dolgu, lacivert alt çizgiyle ve basılı düğme durumu ile belirtilir. Bir form açıktır; kayıt sonrası aynı kişi/görev korunur, güncel sürüm sunucudan yüklenir. Ad kaydı ve durum onayı ayrıdır; giriş hesaplarının değişmediği açıklanır. Hizmet checkbox'ı küçük, tıklanabilir etiketi en az 44 px'tir. Mobilde satır eylemi/form düğmeleri alt alta; uzun ad satıra kırılır. Personel yüzeyinde fotoğraf/avatar/logo/uydurulmuş özet bulunmaz.

## Do's and Don'ts

Mevcut tanımlarda Sil, pasifleştirmeden ayrı ve tehlikeli işlem dolgusu ile gösterilir; ad ve korunacak geçmiş onayda açıklanır. Vazgeç odağı açan düğmeye döndürür. Gönderim sırasında geçiş ve çift gönderim kilitlidir; sunucu 204 onayı sonrası liste güncellenir. Personel ayrıntısında kaydedilmemiş taslak koruması sürer. Giriş hesabı Etkinleştir ana işlem dolgusu kullanır; hesap/personel ayrımı görünür metindir. [İşlem brief'i](.impeccable/surfaces/definition-access-actions.md).

- Do: Yeni kimlik ekranında ortak yerleşimi ve merkezi tokenları kullan.
- Do: Boş, yükleme, hata ve başarı durumlarını gerçek API yanıtlarına bağla.
- Do: Fotoğrafın üstündeki yazıyı okunabilir açık alanda tut; klavye odağını görünür bırak.
- Don't: Minimalizmi fotoğrafı kaldırmak veya tüm sayfayı boşaltmak olarak yorumlama.
- Don't: Gelecekteki modülü, istatistiği veya başarı mesajını uydurma.
- Don't: Bu değişikliği yönetim ekranlarının ya da dark mode'un tamamlandığı şeklinde gösterme.
