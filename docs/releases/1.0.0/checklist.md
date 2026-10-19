# 1.0.0: ilk Store gönderimi

Durum: `dist/store` hazır (2026-10-19). Paketlerdeki kimlik Partner Center değerleri gelene kadar yer tutucu.

## Gönderimden önce

- [x] İngilizce arayüz (Windows diline göre tr/en), manifestte `en-US` + `tr-TR`.
- [x] Sürüm `1.0.0.0` (manifest + csproj).
- [x] Listeleme metinleri tr + en (`docs/store-listing/`), ekran görüntüleri, poster ve kutu resmi tr + en (`dist/store/listing/`).
- [x] İmzasız x64 + ARM64 paketleri (`dist/store/packages/`).
- [x] **Partner Center kimliği** (2026-10-19) manifestte: Name `MrWProject.MrWLyrics`, Publisher `CN=BE7956F6-3D06-4F0C-B8B9-FAA5D1653D9F`, PublisherDisplayName `MrWProject`. PFN `MrWProject.MrWLyrics_1jmj08nda0ave` (Publisher karması ile doğrulandı), Store ID `9N5X2VHWXJWQ`.
- [ ] Store ID sitenin panelinde: MrW Lyrics → Ana sayfa metinleri → Store alanı (`https://apps.microsoft.com/detail/9N5X2VHWXJWQ`). Bağlantı uygulama yayına girince çalışır.
- [x] Site yayında. Gizlilik politikası `https://lyrics.mrwproject.com/privacy/`, destek ve iletişim `https://lyrics.mrwproject.com/contact/`.
- [ ] **Site yayını:** panelde MrW Lyrics → Önizleme → **Yayınla** (o zamana kadar adresler 404). Cloudflare Turnstile'a `lyrics.mrwproject.com` eklenmeli, yoksa iletişim formu çalışmaz.
- [ ] Partner Center → Properties: Privacy policy URL ve Support/Website olarak yukarıdaki adresler.
- [ ] **Yaş derecelendirmesi** (IARC anketi). Şarkı sözleri kullanıcının çaldığı müzikten gelir ve filtrelenmez.
- [ ] Paketli uygulama (1.0.0) yerelde denenmeli.

## Gönderimde

- [ ] Packages: `dist/store/packages/` içindeki iki `.msix`.
- [ ] *Notes for certification*: `dist/store/certification-notes.txt`.
- [ ] `runFullTrust` gerekçesi: certification-notes'taki son paragraf.
- [ ] Listeleme (tr ve en): `dist/store/README.txt` dosyasındaki eşleşmeye göre. What's New ilk sürümde yok.
- [ ] Copyright: `© 2026 <yayıncı adı>`, kategori: Music.

## Bilinen risk ve sorunlar

- Musixmatch (resmi olmayan mobil API) ve Genius (sayfa kazıma) varsayılan olarak açık. Bu, 2026-10-19'da kullanıcının bilerek verdiği karar. Store'un fikri mülkiyet ve içerik politikaları ya da bu servislerin kullanım koşulları nedeniyle ret gelebilir. Ret gelirse ilk adım bu kaynakları varsayılanda kapatmak.
- Satır dolum efekti, iki satıra kayan uzun satırlarda doğru görünmüyor: renk geçişi her satırı ayrı değil, bloğu bir bütün olarak dolduruyor. Ekran görüntülerinde bu durum pencere genişletilerek önlendi. Uygulamada düzeltilmesi gerekiyor.
