# ADR-005 — Randevu ve çalışan zaman bloğu

Durum: **kabul edildi**, 14.10.2026. Kullanıcı önerilen model ve P03-02 kapsamıyla kod işini başlattı. [P03 yerel kabulü](../plans/P03.md#p03-02-yerel-geliştirme-kabulü--14102026) DB çakışma, yarış, idempotency ve migration kanıtını tutar; GitHub/ana teslim ayrı kaydedilir.

## İhtiyaç

MVP tek hizmet/personel; aynı kişinin dolu zamanı tampon dahil korunmalı, iptal bırakmalı ve geçmiş hizmet/fiyat/süre değişmemelidir. P10/P11'de çok hizmet/personel gündeme geldiğinde çakışma güvencesini kaldırıp istemciye taşımamak gerekir.

## Alternatifler ve öneri

| Model | Bedel |
| --- | --- |
| Tüm alanlar Appointments satırında | MVP en az tablo/join; çok hizmet/personelde dolu aralıkları yeni tabloya taşıma, constraint geçişi ve geçmiş kayıt dönüşümü gerekir. |
| Appointments + AppointmentTimeBlocks | MVP'de fazladan tablo/join; şimdiden mevcut çalışan zamanını somut olarak modeller, ileride çok blok eklerken aynı GiST güvencesi korunabilir. |
| Genel kaynak/takvim blok motoru | Henüz ihtiyaç olmayan kaynak türleri ve soyutlamalar; kapsam dışında. |

Karar ikinci seçenektir. Appointments müşteri/oluşturucu/işlem anahtarı/sürümü tutar; AppointmentTimeBlocks hizmet/personel snapshot'ı, UTC başlangıç/bitiş/tampon, bloke aralık ve durumu tutar. MVP randevu başına tek blok UNIQUE kuralıyla sınırlıdır. Üst tabloda ikinci durum kopyası yoktur; tek blok durumu API'nin randevu durumudur. Blok, üst kayıt ve audit aynı transaction'da yazılır. Çoklu hizmet/sıralama/şube tabloları veya API'si bugünden eklenmez.

Personel FK + tstzrange için GiST exclusion constraint; sadece Cancelled hariç, diğer dört durum kapsamda. `[başlangıç, bitiş)` tamponla genişletilir; `now()` partial predicate'i yerine açık süre sonu geçişi yapılır. Çoklu blok aşamasında MVP UNIQUE kuralını kaldırmak mümkündür; ancak aggregate durum/atomik çoklu işlem/yetki politikası o aşamada ayrıca tasarlanır. Bu öneri gelecekte migration gerekmeyeceği sözü değildir.

## Uygulama koşulu ve geri dönüş

DB doğrudan çakışma testi, paralel API/idempotency, saat/izin yarışları ve parent/blok/audit transaction testi geçmeden bu model kabul edilmiş sayılmaz. Kilit sırası mevcut personel → hizmet sırasına uyar; uygunluk kaynakları transaction altında yeniden okunur. Veri oluştuktan sonra önceki uygulama imajı randevu kurallarını bilmez; uyumsuz geri dönüş yerine yazmayı durdurup ileri düzeltme değerlendirilir. Down/restore veri kaybettirebilir; ana ortamda otomatik değildir.

## Resmi dayanaklar

[PostgreSQL 18 aralıklar ve exclusion constraint](https://www.postgresql.org/docs/18/rangetypes.html#RANGETYPES-CONSTRAINT), eşit personel ve kesişen aralığı birlikte engelleme mekanizmasını açıklar. [btree_gist](https://www.postgresql.org/docs/18/btree-gist.html), UUID gibi eşitlik alanını GiST aralık kontrolüyle kullanmayı destekler.

[PostgreSQL 18 explicit locking](https://www.postgresql.org/docs/18/explicit-locking.html#LOCKING-DEADLOCKS), kilitlerin ortak sırada alınmasını ve transaction sonuna kadar tutulmasını açıklar. Bunlar mekanizma dayanağıdır; yukarıdaki tablo ayrımı ve ürün davranışı bu projeye ait öneridir, belgenin buyurduğu bir mimari değildir. Kaynaklar 14.10.2026'da kontrol edildi.
