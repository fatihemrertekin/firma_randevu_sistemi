# OPERATIONS — Kurulum, işletim ve büyüme

Hazırlanma: 2026-09-29. Bu dosya plandır; çalışan altyapının kanıtı değildir. Uygulandıkça gerçek komutlar, adresler ve ölçümlerle güncellenir (sırlar hariç). Kısa ilkeler AGENTS.md Bölüm 8'dedir.

## 1. Kurulum ve kapasite

- Yerelde geliştir; VPS'i pilotta aç. 4 GB deneme, Coolify+ayrı DB süreçleri için 8 GB sınıfı üretim adayıdır; kapasite garantisi değildir. Firma sayısını yük testiyle belirle: RAM/CPU/disk, DB bağlantısı, p95 gecikme, kuyruk.
- Tek Compose şablonu ve tekrar güvenli kurulum komutu: firma/domain/sırlar → servis/migration/sağlık sonucu. CPU/RAM/log/disk sınırı ve yanlış firma kaynağını silmeme kontrolü. Coolify ortak proxy ağı izolasyon kanıtı değildir.
- SSH anahtarı, firewall, güncelleme; yönetim paneline kısıtlı erişim/MFA. Docker/panel tüm hostu etkiler. Web portları açık, DB iç ağda; uptime alarmı host dışında.

## 2. Dağıtım ve yükseltme

- CI imajını digest ile test → pilot → diğerlerine sırayla dağıt. Migration başlangıçta otomatik değil; yedek sonrası tek kontrollü iş. Silici değişimde genişlet/taşı/daralt. İmaj rollback'i DB uyumu ister; restore son veriyi kaybettirebilir. Sunucuda elle kod yamalama yok.
- Filo yönetimi (P06): N firmaya toplu sürüm dağıtımı ve migration için runbook/script. Sırayla ilerler, her firmada önce yedek alır, firma başına sonuç (başarılı/başarısız/atlandı) raporlar, ilk hatada durur. Firma sayısı arttıkça elle yapılan iş burada toplanır.

## 3. Yedek ve kurtarma

- Şifreli host dışı yedek: DB, dosya, anahtar ve kurulum bilgisi; ayrı erişim anahtarı. Canlı volume kopyası tutarlı DB yedeği değildir; Coolify yedeği uygulama verisini kapsıyor varsayılmaz. İlk hedef RPO <=24 saat/RTO <=4 saat; ölçmeden sözleşmeyle garanti etme. Gerekiyorsa WAL/PITR.
- Günlük yedek/hata/disk/kuyruk kontrolü; aylık restore/güncelleme provası. Öneri 7 günlük+4 haftalık yedek; hukuki silme/saklamayla kesinleştir. Restore silinen kişiyi geri etkinleştirmesin; kurtarma anahtarı ayrıca korunmalı.

## 4. Büyüme ve taşıma

- Sürekli %70 RAM/disk ilk alarm eşiği; ölç → sorgu/indeks/iş yükü → VPS büyüt/firmayı taşı. Taşırken yazmayı durdur, son tutarlı yedeği aktar/doğrula, DNS değiştir; iki yazıcı açma.
- Tek host kesintisi tüm firmaları etkiler; ilk aşamada HA kümesi yok.
