# Personel ve hizmet kayıtlarını silme

Durum: kabul edildi, 05.10.2026. Kullanıcı personel ve hizmetlerde Sil istedi; çalışan giriş hesabını kapsam dışında bıraktı.

Bu kayıtlar mevcut saat, personel-hizmet ve append-only değişiklik kayıtlarının hedefidir. Fiziksel DELETE yabancı anahtarları veya geçmiş kimliği bozar. Yalnız pasifleştirme ise kaydı yönetim listesinden kaldırmaz.

Karar: `IsDeleted` ile mantıksal silme; işlem ayrıca `IsActive=false` yapar, sürümü değiştirir ve mevcut audit'e `Deleted` ekler. Liste, ayrıntı, saat ve hizmet seçim uçları silineni kullanıma açmaz. Eski oluşturma isteği aynı kimliği canlandıramaz. Saatler, bağlantılar ve audit korunur; audit sorgusunda hedef adı okunabilir kalır. Silinen hizmet bağlantıları sonraki seçim kaydında sessizce kaldırılmaz. Silme ve seçim değişikliği ortak hizmet kilitlerini kullanır.

Bedel: Sil kişisel veriyi fiziksel yok etmez; açıklama onayda görünür. Geri yükleme ekranı yoktur. KVKK saklama/silme süreci bu işlemden ayrıdır. Yeni sütunlar false varsayılanıyla mevcut veriyi korur; eski imaj yalnız henüz silme yapılmadan önce güvenli geri dönüş adayıdır. Silme sonrasında eski imaj silinen satırı tekrar gösterebilir; uygulama geri dönüşü bu davranışı dikkate almalı, şema Down/audit kaybı kullanılmamalıdır.
