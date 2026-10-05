---
version: 1
slug: "c-web-src-features-auth-authenticationscreens-tsx"
primary_target: "src/Web/src/features/auth/AuthenticationScreens.tsx"
related_targets: ["src/Web/src/features/auth/AuthenticationScreens.module.css","src/Web/src/features/auth/OwnerEmailConfirmation.tsx"]
---

# Authentication surface

Mode: operate. Audience: existing Owner and Staff accounts. Scope: login and existing anonymous identity screens; preserve API, DTO, cookie, CSRF, role and busy/error behavior. No management screen redesign, new capability or dark mode.

Approved desktop comp: .impeccable/mocks/decision/login-cream-photo-desktop-v2.png
Approved mobile comp: .impeccable/mocks/decision/login-cream-photo-mobile-v2.png
Approval: human message “onaylıyorum”, 2026-10-04, after the revised cream/photo comps and implementation scope were presented.

## Direction contract

THESIS: İlk fotoğraflı girişin geniş yapısını açık krem temaya taşır; sade standart kontroller ana işi görünür tutar.

OWN-WORLD: Kullanıcı paleti 001524/15616D/FFECD1/FF7D00/78290F; krem geniş zemin, petrol bağlantı, lacivert yazı, turuncu düz eylem ve sıcak kahverengi vurgu. Fotoğraf ayrı raster; metin/alan/düğme semantik kod.

STORY: Kullanıcı hesabıyla girer; gerektiğinde MFA tamamlar. Parola kurtarma ve çalışan daveti açık ikincil yollardır. Başarı yalnız sunucu onayından sonra.

FIRST VIEWPORT: Solda yaklaşık üçte bir krem form, sağda tam yüksekliğe yayılan salon arka planı/karşılama. Metin altı krem katman. Mobilde kısa fotoğraflı üst alan ve tek sütun form; kurtarma ayrı dokunma satırında.

FORM: Kullanıcının sabitlediği ilk giriş yerleşimi; önceki motor araştırması ab68271f/aday 3, kullanıcının renk/yapı tarifi bu atamayı geçersiz kıldı. Gönderim aynı formda görünür durum değişimiyle kalır; süslü hareket yok.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

Constraints: 320/390/768/1280px, 44px targets, keyboard/focus/reduced-motion, WCAG AA, Turkish, strict TS/CSS Modules and centrally recorded tokens. Exact user palette wins over sampled mock colors. No fundamental UI text/control baked into images. Photo proportion on mobile may adapt for short viewport/keyboard, as stated in the approved brief.

## Kullanıcı düzeltmesi — 06.10.2026

Yalnız ilk girişte önceki 18 px ana işlem, 16 px yardımcı metin ve genişlikler geri alınır. MFA ve diğer kimlik ekranlarında kompakt butonlar korunur. Kullanıcının mevcut ekran üzerinde doğrudan istediği dar düzenlemedir; yeni ekran/taslak veya başka aşama başlatılmaz. Kabul kanıtı P02 planındaki mevcut yüzey düzeltmeleri kaydına eklenir.
