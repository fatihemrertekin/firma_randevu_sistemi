# Sunucu testlerinin sınırları

Bu dizinde kök [AGENTS.md](../../AGENTS.md) kuralları geçerlidir.

- Yeni senaryoyu özelliğine ait bağımsız test sınıfına ekle; ilgisiz özellikleri tek partial sınıfta toplama.
- Paylaşılan PostgreSQL/oturum/MFA/CSRF yardımcıları `Support` altında, özelliğe özel yardımcılar kendi test dosyasında kalsın; genel test tabanı ekleme.
- Gerçek PostgreSQL ve test başına izolasyonu koru; transaction, yarış, rollback ve yetki senaryolarını birim testle değiştirme.
- Mevcut `AuthenticationTestCollection` sınırını koru; yeni ortam değişkenli kimlik testlerini bu koleksiyona al, senaryo içi paralelliği veya assembly paralelliğini kapatma.
- Test verilerini ve doğrulamaları sadeleştirme uğruna zayıflatma; ilgili sunucu kapılarını [README](../../README.md) ve kök kurallara göre çalıştır.
