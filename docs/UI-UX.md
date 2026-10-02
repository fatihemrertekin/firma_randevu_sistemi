# docs/UI-UX.md — Arayüz ve deneyim tek kaynağı

Bu dosya kalıcı UI/UX kararlarının tek kaynağıdır. Görsel yön `docs/PRODUCT.md`'de
onaylıdır; burada o yönün somut değerleri vardır. Değer değiştirmek ürün sahibinin
onayını gerektirir. Aşama kapsamı ve kabul kanıtı `docs/ROADMAP.md`'de kalır.

> Not: Aşağıdaki hex değerleri onaylı petrol/lacivert + turkuaz yönünün başlangıç
> sabitleridir. P00'daki ilk ekran onaylanınca değerler burada güncellenir ve
> `tokens.css` ile birebir eşleşir.

---

## 1. Karakter

- Sakin, güvenilir, hızlı. "Kontrol paneli" değil, günlük çalışma aracı.
- Veri ve işlem önce gelir; dekorasyon ikinci plandadır ama göze güzel gözüken sayfalar şarttır.
- UI/UX üzerine kullanıcı deneyimi çok önemlidir. Mantıklı ve özenle insana göre kurgulanmış bileşen yerleşimleri önemlidir.
- Konuyla alakalı ilgili yerlerde gerektiği şekilde alakalı yapay zeka görselleri üretip gereken yerlerde kullanabilirsin.
- Hedef kullanıcı: telefonda ve ayakta çalışan işletme sahibi/personel. Mobil önce düşün.

## 2. Token tablosu (`tokens.css` tek kaynak)

### Renk — yüzey katmanları
| Token | Değer | Kullanım |
|---|---|---|
| `--bg` | `#0B1F2A` | Sayfa zemini |
| `--surface-1` | `#102B39` | Kart, panel, tablo |
| `--surface-2` | `#16384A` | Yükseltilmiş: menü, popover, seçili satır |
| `--surface-3` | `#1D4559` | Hover, aktif girdi zemini |
| `--border` | `#2A5368` | Ayırıcı, girdi kenarı |
| `--border-strong` | `#3C6A82` | Vurgulu kenar, odak dışı seçili |

### Renk — metin
| Token | Değer | Kullanım |
|---|---|---|
| `--text` | `#E8F1F4` | Ana metin |
| `--text-muted` | `#9DB4BD` | Yardımcı metin, etiket |
| `--text-faint` | `#7F98A2` | Yalnız büyük/dekoratif ikincil metin (küçük metinde kullanma) |

### Renk — vurgu ve durum
| Token | Değer | Kullanım |
|---|---|---|
| `--accent` | `#2DD4BF` | Ana işlem, seçim, odak halkası |
| `--accent-hover` | `#5EEAD4` | Ana işlem hover |
| `--on-accent` | `#04202A` | Turkuaz zemin üstü metin |
| `--success` | `#4ADE80` | Onaylı, başarılı |
| `--warning` | `#FBBF24` | Bekliyor, dikkat |
| `--danger` | `#F87171` | Hata, iptal, silme |
| `--info` | `#60A5FA` | Bilgi, gelmedi dışı nötr bildirim |

Kurallar:
- Turkuaz yalnız: ana işlem düğmesi, seçili öğe, odak halkası, aktif menü. Bir ekranda
  en fazla bir "ana işlem" düğmesi dolu turkuaz olur.
- Durum rengi tek başına anlam taşımaz; her zaman metin etiketi + ikon eşlik eder.
- Metin/zemin çiftleri en az 4.5:1 (büyük metin 3:1, UI sınırı 3:1). Yeni çift
  eklemeden önce kontrast ölç.

### Tipografi
- Font: **Inter** (yerel sunulan, `woff2`, `font-display: swap`; Türkçe karakter desteği
  doğrulanır). Yedek: `system-ui, sans-serif`. Sayısal sütunlarda
  `font-variant-numeric: tabular-nums`.

| Token | Boyut / satır | Ağırlık | Kullanım |
|---|---|---|---|
| `--fs-display` | 28 / 36 | 600 | Sayfa başlığı |
| `--fs-title` | 20 / 28 | 600 | Bölüm / kart başlığı |
| `--fs-body` | 16 / 24 | 400 | Gövde (mobilde alt sınır 16) |
| `--fs-label` | 14 / 20 | 500 | Etiket, düğme, tablo başlığı |
| `--fs-help` | 13 / 18 | 400 | Yardımcı metin, durum notu |

### Boşluk, köşe, gölge, hareket
- Boşluk (4/8 tabanlı): `--sp-1:4px --sp-2:8px --sp-3:12px --sp-4:16px --sp-5:24px --sp-6:32px --sp-7:48px`
- Köşe: `--r-sm:6px` (girdi, düğme) · `--r-md:10px` (kart) · `--r-lg:14px` (dialog). Daha yuvarlak yok; tam yuvarlak yalnız avatar/rozet.
- Gölge: `--shadow-1: 0 1px 2px rgb(0 0 0 / .25)` (kart) · `--shadow-2: 0 8px 24px rgb(0 0 0 / .35)` (popover, dialog). Başka gölge yok.
- Hareket: `--dur-fast:120ms --dur-base:200ms --ease: cubic-bezier(.2,0,0,1)`. Yalnız `opacity` ve `transform`. `prefers-reduced-motion: reduce` altında geçişler kapanır.
- Odak: `outline: 2px solid var(--accent); outline-offset: 2px` — hiçbir yerde kaldırılmaz.
- Dokunma hedefi: `--hit: 44px` (min-height ve min-width).

### Yerleşim
- İçerik max genişliği 1200 px; form sütunu max 560 px.
- Kırılımlar: 320 (taban) · 600 · 900 · 1200. Menü: <900 alt çubuk/çekmece, ≥900 sol kenar çubuğu.
- Güvenli alan: mobilde `env(safe-area-inset-*)` hesaba katılır.

## 3. Bileşen envanteri

Her bileşen CSS Modules + tipli props. Tekrarlanan gerçek ihtiyaç doğmadan yenisi eklenmez.

| Bileşen | Sözleşme |
|---|---|
| `Button` | Varyantlar: `primary` (turkuaz dolu), `secondary` (kenarlıklı), `ghost` (metin), `danger` (kırmızı çerçeve; onay adımında dolu). `loading` durumunda etkisiz + "Kaydediliyor…" metni. Yalnız ikonlu düğmede `aria-label` zorunlu; kritik işlem ikon+metin. |
| `Field` | Görünür etiket, zorunlu işareti + "zorunlu" metni (`aria-required`), yardım metni, alan altı hata (`aria-describedby`, `aria-invalid`). Placeholder etiket yerine geçmez. |
| `Select` / `DatePicker` / `TimePicker` | Klavye ile tam kullanım; işletme saat bölgesi; Türkçe biçim. |
| `Dialog` | Odak hapsi, `Esc` ile kapanış, kapanınca açan kontrole dönüş. Riskli işlemde neyin etkileneceğini somut yazar ("Ayşe Y.'nin 14:30 randevusu iptal edilecek"). |
| `Toast` | Yalnız düşük önemli başarı için. Kritik bildirim `Banner`/satır içi kalır; `role="status"`, kritikte `role="alert"`. |
| `Banner` | Sayfa/bölüm düzeyi kalıcı mesaj (hata, çakışma, oturum). Kapatılabilir ama kaybolmaz. |
| `StatusBadge` | Durum = renk + ikon + metin. Eşleşme tablosu aşağıda. |
| `DataTable` | Sunucu sayfalama/filtre/sıralama; mobilde satır → kart dönüşümü; sticky başlık; boş/yükleme/hata satırları. |
| `EmptyState` | İkon + tek cümle neden + tek eylem. Hata ile asla karışmaz. |
| `Skeleton` | Gerçek yerleşimin şekli; yerleşim sıçraması yapmaz. |
| `Calendar` | Bölüm 5.1. |
| `PageHeader` | Başlık, kısa açıklama, ana işlem (sağda; mobilde altta sabit olabilir). |
| `ConfirmDiscard` | Kaydedilmemiş değişiklik uyarısı (rota değişimi + sekme kapanışı). |

### Randevu durum eşleşmesi
| Durum | Renk | İkon | Etiket |
|---|---|---|---|
| Bekliyor | `--warning` | saat | Bekliyor |
| Onaylı | `--success` | onay işareti | Onaylı |
| Tamamlandı | `--info` | çift onay | Tamamlandı |
| Gelmedi | `--danger` | çarpı daire | Gelmedi |
| İptal | `--text-muted` | üstü çizili daire | İptal |

(Gerçek durum kümesi backend sözleşmesinden alınır; bu tablo yalnız görsel eşleşmedir. Backend'de olmayan durum eklenmez.)

## 4. Genel desenler

- **Ekran iskeleti:** PageHeader → (filtre çubuğu) → içerik → geri bildirim alanı. Her ekranda aynı sıra.
- **Yükleme:** ilk yüklemede Skeleton; yeniden yüklemede eski veri + ince ilerleme göstergesi. Eski yanıt yeni durumu ezmez (istek iptali/sıra numarası).
- **Hata eşleşmesi:**

| Kod | Kullanıcıya gösterim |
|---|---|
| 400 | Alan yanında hata; ilk hatalı alana odak |
| 401 | "Oturumun sona erdi." + yeniden giriş; girdi korunur (hassas olmayanlar) |
| 403 | "Bu işlem için yetkin yok." + geri dön; ilgili kontrol zaten gizli olmalı |
| 409 | Çakışma banner'ı: neyin çakıştığı + "Güncel kaydı gör" / "Farklı saat seç"; kullanıcı girdisi korunur |
| 429 | "Çok sık denendi. N saniye sonra tekrar dene." + geri sayım |
| 5xx / ağ | "Şu an kaydedemedik. Durumu kontrol et." + güvenli tekrar/durum sorgusu; başarı iddia edilmez |

- **Mikro metin tonu:** kısa, iş dili, "sen" hitabı, ünlem yok. Örnekler:
  - Boş liste: "Bugün için randevu yok. Yeni randevu ekleyebilirsin."
  - Başarı: "Randevu kaydedildi." (yalnız sunucu onayından sonra)
  - Risk: "Bu randevu iptal edilecek. Müşteriye bildirim gönderilmez." (gerçek davranışa göre yazılır)
- **Tarih/saat/para:** yalnız ortak yardımcılar; `Europe/Istanbul` veya işletmenin bölgesi; `1.250,00 ₺`, `14:30`, `12 Eki Pzt`.

## 5. Ekran tarifleri

Her yeni ekran öncesinde şu şablon doldurulur (3–6 satır):
**Amaç · Ana işlem · Bilgi sırası · Mobil düzen · API bağlantıları · Boş/hata/yükleme durumları.**

### 5.1 Takvim (günlük çalışma alanı)
- **Amaç:** Bugünün/haftanın randevularını görmek ve hızla yeni randevu eklemek.
- **Düzen (masaüstü):** üstte tarih gezgini (önceki/bugün/sonraki) + görünüm anahtarı (Gün/Hafta) + ana işlem "Yeni randevu"; solda saat ekseni, sütunlar personel; blok = randevu (renk çubuğu = durum, metin = müşteri + hizmet + saat).
- **Boş slot:** noktalı kenarlı, hover'da "+ Ekle"; tıklama saati önceden dolu formu açar.
- **Çakışma:** çakışan blok `--danger` kenarlıklı + banner; backend 409 verirse neden gösterilir.
- **Mobil:** varsayılan Gün görünümü, personel sekmeleri; Hafta yalnız yatay kaydırmalı iki boyutlu alan olarak (izinli istisna). "Yeni randevu" sabit alt eylem.
- **Alternatif:** her sürükle-bırak işleminin klavye ve form karşılığı (randevuyu seç → "Taşı" formu).
- **Durumlar:** yükleme iskeleti; boş gün için EmptyState; hata banner'ı.

### 5.2 Randevu listesi
- Filtre çubuğu (tarih aralığı, durum, personel — API destekliyorsa), arama, sunucu sayfalama.
- Satır: saat, müşteri, hizmet, personel, StatusBadge, tek satır işlem menüsü.
- Ayrıntıdan dönüşte filtre/sayfa korunur (URL sorgu parametresi).
- Mobil: kart listesi; ana bilgi saat + müşteri.

### 5.3 Randevu formu / ayrıntı
- Sıra: Müşteri → Hizmet → Personel → Tarih/Saat → Not. Uygun slotlar backend'den gelir; frontend slot uydurmaz.
- Süre/fiyat backend hesabıdır; form yalnız gösterir.
- Kaydet ana işlem; İptal ikincil; "Randevuyu iptal et" tehlikeli ve ayrı bölümde.
- Kaydetme sırasında düğme kilitli; 409'da girdi korunur, çözüm önerilir; çıkışta ConfirmDiscard.

### 5.4 Giriş
- Onaylı görselli düzen korunur; form sütunu dar ve önde; mobilde görsel formun arkasına/altına geçer.
- Parola yöneticisi ve yapıştırma serbest; 401/429 mesajları bölüm 4'e göre.

### 5.5 Müşteri akışı
- Tek amaç: en kısa adımda randevu almak. Adım göstergesi (Hizmet → Zaman → Bilgi → Onay), her adımda tek karar, mobil önce.

(Owner/Staff menüsü yetkiye göre render edilir; yetki yalnız gizleme değil, backend'de de zorunludur.)

## 6. Yasaklar (jenerik görünümü önler)

- Her şeyi karta sarma; kart içinde kart.
- Gradient'i süs için kullanma; yalnız anlam taşıyan yerde ve ölçülü.
- Emoji'yi ikon yerine kullanma; karışık ikon setleri.
- Aynı ağırlıkta çok sayıda dolu düğme; bir ekranda ikinci turkuaz dolu düğme.
- Cam/bulanıklık efekti, neon parlama, aşırı yuvarlak köşe, gereksiz animasyon.
- Sahte grafik, sahte istatistik, çalışmayan menü/buton; uygulanmamış modül.
- Rastgele hex/px; tokensız renk ve ölçü.
- Yalnız renkle durum; yalnız ikonla kritik işlem; placeholder'ı etiket yapmak.

## 7. API ↔ ekran ↔ yetki matrisi

Mevcut P02 istemcisi `src/Web/src/app/api.ts` ve ekranların açık DTO'larıdır; OpenAPI'den
üretim henüz uygulanmadı. Bu tablo gerçek endpoint/yetkileri kaydeder. Matris her
aşamada ilgili satırlar eklenerek güncellenir; kodda olmayan endpoint için satır yazılmaz.

| Ekran / işlem | Endpoint | Yöntem | Owner | Staff | Müşteri | Hata durumları | Not (idempotency / sürüm) |
|---|---|---|---|---|---|---|---|
| Oturum bilgisi | /api/auth/me | GET | Evet | Evet | Hayır | 401/403 | MFA öncesi Owner erişimi false |
| İstek doğrulaması | /api/auth/csrf | GET | Evet | Evet | Anonim | 429 | no-store, token bellekte |
| Giriş | /api/auth/login | POST | Evet | Evet | Anonim | 400/401/429 | Owner için 202 → MFA; çift gönderim engeli |
| Çıkış / MFA'dan geri dönüş | /api/auth/logout | POST | Evet | Evet | Anonim | 400/429 | CSRF; tekrar güvenli, geçici cookie de kapanır |
| MFA kurulum anahtarı | /api/auth/mfa/setup | POST | OwnerSetup | Hayır | Hayır | 400/401/403/409/429 | Mevcut parola; hesap kilidi + güncel stamp |
| MFA etkinleştirme | /api/auth/mfa/enable | POST | OwnerSetup | Hayır | Hayır | 400/401/403/409/429 | Parola + TOTP; kodlar bir kez, oturumlar iptal |
| MFA uygulama kodu | /api/auth/mfa/login | POST | Bekleyen giriş | Bekleyen giriş | Hayır | 400/401/429 | Önce parola; 5 dakika geçici cookie |
| MFA yedek kodu | /api/auth/mfa/recovery-login | POST | Bekleyen giriş | Bekleyen giriş | Hayır | 400/401/429 | Parola sonrası; kod tek kullanımlı |
| Kalan MFA kodları | /api/auth/mfa/recovery-codes | GET | MFA | Hayır | Hayır | 401/403/429 | Yalnız sayı; kodlar tekrar gösterilmez |
| MFA kodlarını yenile | /api/auth/mfa/recovery-codes | POST | MFA | Hayır | Hayır | 400/401/403/409/429 | Parola, CSRF, kilit; eski kod/oturum iptali |
| Parola değiştir | /api/auth/change-password | POST | MFA | Evet | Hayır | 400/401/403/409/429 | Mevcut parola; bütün oturumlar iptal |
| Kurtarma e-postası durumu | /api/auth/recovery-email/ | GET | MFA | Hayır | Hayır | 401/403/429 | Hesap adresi değiştirilemez |
| Doğrulama gönder | /api/auth/recovery-email/request | POST | MFA | Hayır | Hayır | 400/401/403/409/429/503 | Kalıcı SMTP; uygulama 60 saniye yeniden istek sınırı |
| E-posta doğrula | /api/auth/recovery-email/confirm | POST | Bağlantı | Bağlantı | Anonim | 400/409/429 | GET değiştirmez; açık POST tek kullanımlı |
| Parola yenileme kullanılabilirliği | /api/auth/password-reset-options | GET | Evet | Evet | Anonim | 429 | Gerçek gönderim ayarı; no-store |
| Owner sıfırlama bağlantısı iste | /api/auth/password-reset-request | POST | Hesap e-postası | Genel yanıt | Anonim | 400/429/503 | Hesap varlığı açılmaz; doğrulanmış Owner + outbox |
| Owner parolayı sıfırla | /api/auth/reset-password | POST | Bağlantı/kod | Başka hesap açıksa hayır | Anonim | 400/409/429 | 30 dakika/tek kullanım; MFA korunur |
| Çalışan davetleri | /api/staff-invitations/ | GET/POST | MFA | Hayır | Hayır | 400/401/403/409/429 | Kod yalnız oluşturma yanıtında, 24 saat |
| Çalışan hesapları | /api/staff-accounts/ | GET | MFA | Hayır | Hayır | 400/401/403/429 | Aktif/pasif, sayfalama; Owner/çift rol hariç |
| Çalışan hesabını pasifleştir | /api/staff-accounts/{id}/deactivate | POST | MFA | Hayır | Hayır | 400/401/403/404/409/429 | CSRF/sürüm; tekrar güvenli, oturum/kod iptali ve audit atomik |
| Personel listesi | /api/staff-members/ | GET | MFA | Hayır | Hayır | 400/401/403/429 | Ad/aktiflik, sunucu sayfalama; giriş hesaplarından bağımsız |
| Güncel personel | /api/staff-members/{id} | GET | MFA | Hayır | Hayır | 401/403/404/429 | Form çakışmasında güncel sürümü yükle |
| Personel ekle | /api/staff-members/ | POST | MFA | Hayır | Hayır | 400/401/403/409/429 | CSRF; aynı istek ID'si çift kayıt yaratmaz; audit atomik |
| Personel adını düzenle | /api/staff-members/{id} | POST | MFA | Hayır | Hayır | 400/401/403/404/409/429 | CSRF/sürüm; ad alanı hatası; kaydedilmemiş değişiklik koruması |
| Personel aktif/pasif | /api/staff-members/{id}/status | POST | MFA | Hayır | Hayır | 400/401/403/404/409/429 | CSRF/sürüm/onay; silme ve giriş hesabı değişikliği yok; audit atomik |
| Daveti iptal et | /api/staff-invitations/{id}/revoke | POST | MFA | Hayır | Hayır | 400/401/403/404/409/429 | Süre/tek kabul korunur |
| Daveti kabul et | /api/staff-invitations/accept | POST | Hayır | Davetli | Anonim | 400/409/429 | Davet e-postası + kod + parola; otomatik giriş yok |
| Çalışan sıfırlama kodu oluştur | /api/staff-password-resets/ | POST | MFA | Hayır | Hayır | 400/401/403/404/409/429 | Tek kullanımlı 30 dakika; Owner güvenli teslim eder |
| Çalışan parolayı sıfırla | /api/staff-password-resets/complete | POST | Hayır | Kod | Anonim | 400/409/429 | Açık oturumla yapılmaz; sonra normal giriş |
| İşletme profilini oku/kaydet | /api/business-profile/ | GET/POST | MFA | Hayır | Hayır | 400/401/403/409/429 | Açık DTO ve sürüm kontrolü; P02-09 mevcut iş |

Kural: ekranda görünen her işlem bu tabloda bir satıra bağlıdır. Satırı olmayan işlem
ekrana konmaz; eksik backend desteği aşama planında "eksik" olarak işaretlenir.

## 8. Görsel doğrulama (kabul)

1. Playwright ile şu genişliklerde ekran görüntüsü al: **320, 390, 768, 1280**.
2. Her görüntüde kontrol: yatay taşma yok, ana işlem belirgin, uzun metin taşmıyor,
   odak halkası görünür, boş/hata/yükleme durumları ayrı ayrı yakalanmış.
3. Ajan her görüntüyü bölüm 2, 3, 6'ya karşı eleştirir ve bulduğu sorunları düzeltip
   yeniden çeker. Eleştiri maddeleri aşama kabul kanıtına yazılır.
4. Klavye turu: Tab sırası mantıklı, dialog odağı dönüyor, sürükle-bırak alternatifi çalışıyor.
5. Gerçek API ile başarı, 401/403, 409, 429 akışı bir kez doğrulanır; mock entegrasyon kanıtı sayılmaz.
6. Görüntüler hassas veri içermez (sahte/anonim test verisi).

Örnek betik (`e2e/shots.spec.ts`):

```ts
import { test } from '@playwright/test';

const widths = [320, 390, 768, 1280];
const routes = ['/giris', '/takvim', '/randevular', '/randevular/yeni'];

for (const w of widths) {
  for (const r of routes) {
    test(`shot ${r} @${w}`, async ({ page }) => {
      await page.setViewportSize({ width: w, height: 900 });
      await page.goto(r);
      await page.waitForLoadState('networkidle');
      const overflow = await page.evaluate(
        () => document.documentElement.scrollWidth > window.innerWidth
      );
      if (overflow && r !== '/takvim') throw new Error(`Yatay taşma: ${r} @${w}`);
      await page.screenshot({
        path: `evidence/${r.replace(/\//g, '_')}_${w}.png`,
        fullPage: true,
      });
    });
  }
}
```

## 9. Referans ("altın") ekranlar

P00/ilk UI aşamasında ürün sahibi tarafından onaylanan şu ekranlar referanstır; yeni
ekranlar bunların desenini kopyalar:

- [ ] Giriş
- [ ] Takvim (gün)
- [ ] Randevu listesi
- [ ] Randevu formu

Onaylanınca yol ve ekran görüntüsü buraya eklenir: `docs/ui/golden/<ekran>.png`.
