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
