---
version: 1
slug: managementlayout-tsx
primary_target: src/Web/src/app/ManagementLayout.tsx
related_targets: [src/Web/src/app/ManagementLayout.module.css,src/Web/src/features/business/BusinessProfile.tsx,src/Web/src/features/business/BusinessProfile.module.css,src/Web/src/styles/tokens.css]
---

# Management shell and business profile

Mode: operate. Owner and Staff use existing authorized operations. T01 scope is the shared shell and business profile only; later seven-section detailed redesign is staged in docs/plans/P02.md. Keep API/DTO/cookie/CSRF/MFA/version/busy/error behavior. Profile draft stays mounted across sections. No logo, new module, migration or dark mode.

Approved desktop comp: .impeccable/mocks/decision/management-combined-desktop.png
Approved mobile comp: .impeccable/mocks/decision/management-combined-mobile.png
Approval: human chat, 2026-10-04: “2. ve 3. tasarımı birleştirdiğin versiyonu onaylıyorum”. This follows the displayed combined desktop and mobile comps and T01 implementation scope.

## Direction contract

THESIS: Dar ana gezinme ve açılan alt menü, profil düzenlemesini aynı bağlamda okunabilir taslak özetiyle birleştirir.

OWN-WORLD: Onaylı girişin krem/lacivert/petrol/turuncu/kahverengi paleti; Unna yalnız metin markası, sistem sans iş alanları. Düz kontroller, ince sınır ve ölçülü fotoğraf. PRODUCT paleti üretilmiş raster tonlarından üstündür.

STORY: Kullanıcı İşletme/Ekip/Hesap grubunu ve mevcut yetkili bölümünü seçer; profil alanlarını düzenler, özeti kontrol eder ve sunucu onayından sonra kayıt sonucunu görür.

FIRST VIEWPORT: Krem üst başlıkta marka/hesap/ikincil çıkış; petrol dar grup menüsü, krem kapanabilir alt menü; görev başlığı altında form ve sağda fotoğraflı yerel taslak özeti. Mobilde Menü kapalı başlar; form, kaydet/yükle, fotoğraf ve özet tek sütuna geçer. Dar masaüstünde özet formun altına alınabilir. Özet yayımlanmış sayfa değildir.

FORM: 889ffadc surface/operate, dealt 7/6/3. Human steering pinned a blend of candidates 6 and 3; combined desktop and mobile approved. Icons are consistent semantic SVG; core text/controls remain code. The modest photograph uses generated assets/plates/salon-photo.png with its prompt and chat approval sidecar. No baked sample data; real profile drives fields and summary.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

Constraints: 320/390/768/1280px, keyboard/focus/reduced-motion, WCAG AA, 44px targets. Staff has only account/security; Owner sections still require MFA. Summary distinguishes unsaved/loaded state; errors never show a confirmed success. Small source comps are visual authority, not proof of API/mobile behavior. Previous login engine responsive/finish limit remains separately recorded.

## Implementation review — 2026-10-04

T01 implements the approved shell/profile. The human requests “görseller için çok çabalama iyi böyle görsel” and “bunlarla vakit kaybetmeyelim” end additional raster iterations and native review ceremonies. This does not waive functional, role or responsive checks. Native spec and plates closed; the engine remains at hero, with no claim of native finish approval or pixel-perfect reproduction. The native font classifier's Merienda suggestion is rejected in favor of PRODUCT's pinned system sans and Unna identity.

Actual Chrome/API acceptance: 196 frontend tests, typecheck/lint/build, successful normalized profile save, invalid 400, stale-version 409, anonymous 401, Staff GET/POST 403, reload/draft/logout protection. Screenshots at 320/390/768/1280 and additional 1536 reviewed. Cream/petrol hierarchy and small photograph match the approved structure; narrow desktop summary falls below the form, mobile actions stack. No horizontal overflow or button targets below 44px. Full WCAG certification is not claimed. Later section compositions remain T02–T08.

## Kullanıcı düzeltmesi — 06.10.2026

Seçili İşletme/Ekip/Hesap bağlantısı alt menüyü açıp kapatır; sağ üstte erişilebilir sola ok kapanışı vardır. Ayrı metinli kapanış satırı kaldırılır. Kullanıcının mevcut ekran üzerinde doğrudan istediği dar düzenlemedir; yeni ekran/taslak veya başka aşama başlatılmaz. Kabul kanıtı P02 planındaki mevcut yüzey düzeltmeleri kaydına eklenir.

İşletme sahibi başlığı mevcut profil yanıtından onaylı işletme adını rolün solunda gösterir; taslak/başarısız kayıt yansıtılmaz. Kayıtlardan gruba ilk geçişte alt menü açılır; hedef rota durumu kaynak rotadan ayrılır.

## Onaylı sade menü — 06.10.2026

Kullanıcı gösterilen [geniş menü](../mocks/minimal-navigation/preview-1280.png), [dar menü](../mocks/minimal-navigation/preview-collapsed.png) ve [mobil](../mocks/minimal-navigation/preview-390.png) taslağına “onaylıyorum” dedi. Bu karar önceki büyük koyu grup kutuları/ayrı alt menü sütunu yerine aynı açık paletten beyaz yüzey, ikon+metin satırları, sakin seçili dolgu ve isteğe bağlı dar ikon şeridini yetkilendirir. Alt sayfalar geniş menüde aynı sütunda, dar menüde küçük açılır yüzeydedir. Tekrar tıklama, sola ok, dar menüde Escape/odak dönüşü ve mobil Menü korunur. Logo/dark mode/gelecek modül eklenmez.

Personel listesi dönüşünün hover zemin/metin önceliği düzeltilir; personel hizmet seçimleri yeterli alanda 2–3 sütun, dar alanda tek sütundur. Klavye sırası görsel sıra ile aynı ve etiket hedefleri en az 44 px. Gerçek kabul ve teslim [P02 kaydında](../../docs/plans/P02.md#sade-menü-ve-ortak-sayfalama--06102026) tutulur; taslak entegrasyon kanıtı değildir.

## Gerçek kabul — 06.10.2026

Onaylı sade menü ve hizmet ızgarası gerçek Chrome/API ile 44 durum ve 320/390/768/1280 genişlikte doğrulandı; 241 web testi geçti. İlk kontroldeki üst üste gelen grup işaretleri giderildi. Açık/dar/mobil düzen, etiket/Escape/odak, hover ve sayfalama okunur; üç sütun hizmet seçimi sağ alanı kullanır. Ana 8080 kabul imajıyla, mevcut 32 tablo/hesap/MFA/anahtar/SMTP korunarak teslim edildi. Ayrıntı ve sınırlar P02 kabul kaydındadır; native finish veya tam WCAG sertifikası iddia edilmez.

## Hesap ve güvenlik kararı — 13.10.2026

Kullanıcı bu mevcut yüzey için önizleme/taslak üretmeden doğrudan kod uygulanmasını açıkça istedi. Mevcut açık palet, ortak form/hata bileşenleri, masaüstünde görev bazlı iki sütun ve mobilde tek sütun korunur; yeni ekran veya görsel dünya eklenmez. Parola eşleşme hatası alan yanında tek erişilebilir uyarı olur ve düzenlenince temizlenir. MFA kodları yanıt gövdesi tamamlanana kadar mevcut gezinme kilidine katılır; kod sayısı okuma hatasında formu koruyan yeniden deneme yalnız hata halinde görünür. API/DTO/rol/CSRF/oturum sözleşmesi değişmez. Gerçek kabul ve teslim P02 planındaki T05 kaydındadır.

## Çalışan erişimleri kararı — 13.10.2026

T06 mevcut yüzey bakımı kullanıcı tarafından doğrudan kodla onaylandı; önizleme/taslak üretilmez. Mevcut hesap/davet/parola sıfırlama görev grupları, açık palet, iki sütun masaüstü ve tek sütun mobil korunur. Sağlıklı listelerde yenileme düğmeleri yerine sayfaya dönüşte kontrollü GET kullanılır; hata halinde yeniden deneme görünür. Kod yanıtı tamamlanana kadar gezinme ve diğer erişim işlemleri kilitlenir. Maskeli göster/seç/gizle/temizle ve süre bilgisi korunur; ortak kod bölümü formdan mevcut boşluk tokenıyla ayrılır. Gerçek kabul/teslim P02 planındaki T06 kaydındadır.

## Değişiklik kayıtları kararı — 14.10.2026

T07 mevcut yüzey bakımı doğrudan kodla onaylandı; önizleme üretilmez. Açık palet, masaüstünde zaman/işlem/hesap/hedef sütunları ve mobilde tek sütun korunur. Sunucunun yeniden etkinleştirme kaydı (kaynak 13) mevcut listeye dahil edilir. Yenileme hatasında önceki kayıtların sayfa/aralık bilgisi korunur ve önceki sonuç oldukları belirtilir. Cursor ile okunan liste yeni olaylar geldikçe kaydırılmaz; yeni kayıtlar için gerekli yenileme işlemi boş listede de bulunur ve amacı açıklanır. Kabul ve teslim P02 planındaki T07 kaydındadır.
