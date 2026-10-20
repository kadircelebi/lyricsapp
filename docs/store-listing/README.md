# Store listeleme metinleri

Store ID `9N5X2VHWXJWQ` · PFN `MrWProject.MrWLyrics_1jmj08nda0ave` · Yayıncı `MrWProject` · Store sayfası: `https://apps.microsoft.com/detail/9n5x2vhwxjwq` (2026-10-20'den beri yayında)

Bu klasör Store listelemesinin **kaynağıdır** ve sürümden sürüme değişmez. Her sürümde `tools/Packaging/Build-StoreRelease.ps1` bu dosyaları `dist/store/listing/<dil>/` klasörüne kopyalar. Partner Center'a oradan yapıştırılır.

## Dosya → Partner Center alanı

Store listings → (Türkçe / English) listeleme:

| Dosya | Alan | Sınır |
|---|---|---|
| `<dil>/description.txt` | Description | 10.000 karakter |
| `<dil>/features.txt` | Product features (her satır bir madde) | en fazla 20 madde, her biri 200 karakter |
| `<dil>/short-description.txt` | Short description | 1000 karakter |
| `<dil>/keywords.txt` | Search terms / keywords (her satır bir kelime) | en fazla 7 |
| (Release'e özel) `docs/releases/<sürüm>.md` → What's new → EN / TR | What's new in this version | 1500 karakter, ilk sürümde yok |

Diğer alanlar (dosyası yok, elle doldurulur):

- **Copyright and trademark info:** `© 2026 <Partner Center'daki yayıncı adı>`
- **Developed by:** yayıncı adı
- **Privacy policy URL:** `https://lyrics.mrwproject.com/privacy/`
- **Support contact / Website:** `https://lyrics.mrwproject.com/contact/` ve `https://lyrics.mrwproject.com/`
- **Category:** Music · **Subcategory:** boş

## Gizlilik politikası

`privacy-policy.md` Türkçe ve İngilizce tek sayfadır ve https://lyrics.mrwproject.com/privacy/ adresinde yayımlanır. Uygulama yeni bir ağ çağrısı yaparsa veya yeni bir veri saklarsa önce bu metni, sonra sitedeki sayfayı güncelleyin.

## Görseller

Görseller depoda değil, her sürümde yeniden üretilir: `dist/store/listing/<dil>/screenshots/` ve `.../logos/`. Kaynaklar:

- Ekran görüntüleri: uygulama demo modunda (`LYRICSAPP_DEMO`) uydurma sözlerle çalıştırılıp yakalanır. Telifli söz görünmez.
- Poster ve kutu resmi: `do_not_share/logo.png` dosyasından `tools/Packaging/make_listing_images.py` ile.
