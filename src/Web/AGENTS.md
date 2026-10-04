# Web geliştirme sınırları

Bu dizinde kök [AGENTS.md](../../AGENTS.md) kuralları geçerlidir.

- Ekran, form, hook ve testleri `src/features/<özellik>` altında tut; `App.tsx` içine yeni özellik iş kuralları ekleme.
- Bileşen veya hook ikinci bir sorumluluk/akış aldığında sınırlarına göre ayır; yalnız satır sayısı için bölme.
- Somut tekrar varsa ortak bileşeni `src/components`, ortak API bağlantısını `src/app` altında tut; gereksiz soyutlama ekleme.
- Ürün/görsel yetki için [PRODUCT](../../docs/PRODUCT.md), onaylı ekran brief'i ve varsa DESIGN.md'yi kullan; önce görsel taslak, sonra ayrı kod onayı. Tokenları merkezileştir, mevcut oturum/CSRF akışını koru.
- İlgili web kapılarını [README](../../README.md) ve kök kurallara göre çalıştır; yeni UI akışını gerçek API ve gerekli ekran genişlikleriyle doğrula.
