# Ekran adresleri ve tarayıcı gezinmesi

Durum: kabul edildi, 05.10.2026. Kullanıcı ayrı ekran URL'lerine geçiş planını ve uygulamasını onayladı.

İhtiyaç: Yönetim bölümleri ve personel/hizmet alt ekranları yalnız React state ile seçiliyordu. Yenileme, doğrudan bağlantı, yeni sekme ve tarayıcı geçmişi seçili ekranı temsil etmiyordu.

Alternatifler: Yerel state ile devam etmek davranışı çözmez. History API ile kendi yönlendiricimizi yazmak; engellenmiş geri/ileri, iptal ve oturum geçişlerinde ek bakım getirir. Hash tabanlı adresler mevcut e-posta kanıt fragmentleriyle aynı alanı paylaşır.

Karar: React Router 8.4.0 (MIT), mevcut React 19.3 ve Node 24 LTS ile uyumlu kararlı sürüm olarak sabitlenir. Browser history kullanan istemci yönlendirmesi ve tek merkezi ekran adresi haritası kullanılır. Mevcut API istemcileri ve ASP.NET aynı-origin statik sunumu devam eder; SSR/Node sunucusu veya yeni veri katmanı eklenmez. Vite asset tabanı `/` olur. Sunucunun index.html fallback'i ve bilinmeyen `/api/` için 404 davranışı korunur.

Adres ekranın tek yetki kaynağı değildir: mevcut oturum/MFA/Owner/Staff denetimi ve sunucu nesne yetkisi uygulanır. Giriş dönüşü yalnız tanınan yönetim adreslerini ve sınırlı sayfa parametresini kabul eder. URL'lerde ad, e-posta, parola, davet/MFA kodu veya yeni kalıcı sıfırlama kanıtı tutulmaz. Eski e-posta fragmentleri yönlendirici kurulmadan temizlenir, kanıt yalnız bellekte kalır ve ilgili ekrandan ayrılınca silinir.

Bedel: Bir yönlendirme bağımlılığı ve kilitlenmiş transitif bağımlılıklar eklenir. Form/busy koruması menü yanında geri/ileri için de test edilmelidir. Profil taslağı mevcut onaylı davranışla yönetim bölümlerinde bellekte tutulur; kaybolacak diğer taslaklarda geçiş onayı gerekir. F5/sekme kapatma uyarısı tarayıcı desteğine bağlıdır; taslağı kalıcı depolama ile geri getirme yoktur. DB/migration değişmez; eski imaj geri dönüşü veritabanı işlemi istemez, yeni bağlantıların eski imajda karşılığı olmayabilir.
