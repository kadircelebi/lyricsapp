# Store sürümleri: kurallar

## Nereye bakılır

- **`dist/store/`**: her zaman **gönderilecek son sürüm.** Partner Center'a gereken her şey burada: paketler, metinler, görseller. Neyin nereye gittiği `dist/store/README.txt` dosyasında yazar.
- **`dist/old_versions/<sürüm>/`**: daha önceki sürümler. Yeni sürüm hazırlanınca önceki `dist/store` buraya taşınır, hiçbir şey silinmez.
- `dist/` git'e girmez. Kaynaklar depoda: listeleme metinleri `docs/store-listing/`, sürüme özel metinler `docs/releases/<sürüm>.md`, görsel ve paket betikleri `tools/Packaging/`.

`dist/store` tek komutla hazırlanır:

```powershell
.\tools\Packaging\Build-StoreRelease.ps1
```

Betik sürümü `Package.appxmanifest` dosyasından okur. `dist/store` başka bir sürümse önce onu `dist/old_versions/` klasörüne taşır. Sonra imzasız x64 + ARM64 paketleri derler, metinleri kopyalar (sürüm metinlerini `docs/releases/<sürüm>.md` içinden çıkarıp karakter sınırlarını denetler), poster ve kutu resmini üretir ve ekran görüntülerini (tr + en, demo modunda) alır. Aynı sürümü yeniden derlemek `dist/store` klasörünü yeniler.

## Her sürümün metinleri: `docs/releases/<sürüm>.md`

Her sürüm **tek bir Markdown dosyasıdır.** Sürüm, `Identity/@Version` değerinin ilk üç hanesidir (`1.0.0.0` → `1.0.0` → `docs/releases/1.0.0.md`). Yapısı:

```
# <sürüm> — <kısa başlık>
Durum: …  (hazırlanıyor / sertifikasyonda / Store'da yayında, tarih, etiket)
- İçerik, paketler: kısa notlar

# Microsoft Store
## What's new
### EN
```text      ← Partner Center'a olduğu gibi yapıştırılır
### TR
```text
## Notes for certification (<n> karakter)
```text

# Site       ← sitede yapılacaklar (gizlilik sayfası vb.), yoksa bölüm yok
# GitHub release
```markdown  ← GitHub release metni (İngilizce, Markdown); iş akışı bunu kullanır
# Checklist  ← bizim için: bu sürümde, gönderimden önce, testler, gönderimde, bilinen riskler
```

| Blok | Partner Center'daki yeri | Ne zaman | Sınır |
|---|---|---|---|
| `## What's new` → `### EN` / `### TR` | Store listeleme → *What's new in this version* | **yalnız güncellemelerde** (ilk sürümde blok yok) | 1500 karakter |
| `## Notes for certification` | Submission options → *Notes for certification* | her sürüm | 2000 karakterin altında |

- `Build-StoreRelease.ps1` bu blokları başlıklarından bulur, sınırları aşan metinde durur ve `dist/store` altına `.txt` olarak yazar. Başlık adlarını değiştirmeyin; her başlığın altındaki ilk ` ```text ` bloğu alınır.
- ` ```text ` blokları Partner Center'a **olduğu gibi yapıştırılır**: düz metin, Markdown yok, madde işareti `•`. Başlıktaki karakter sayısı bilgi içindir; betik gerçek uzunluğu yazdırır.
- **What's New** kullanıcıya yazılır: ne değişti, ne kazandı. İç ayrıntı yok. İki dilde içerik aynıdır.
- **Notes for Certification** İngilizcedir ve inceleme ekibine yazılır. Güncellemelerde metnin **en üstünde** bir `Changes in this version:` paragrafı olur: yalnız inceleme ekibini ilgilendiren değişiklikler (yeni izin, ağ bağlantısı, dosya erişimi…). Altında sabit açıklama gelir: uygulama ne yapar, nasıl denenir, hesap gerekmez, hangi servise ne gider, `runFullTrust` gerekçesi. Bir önceki gönderim reddedildiyse ret nedenine nasıl çözüm bulunduğu da yazılır.

## Listeleme metinleri ve görseller

Kalıcı listeleme metinleri (açıklama, özellikler, anahtar kelimeler) ve gizlilik politikası `docs/store-listing/` altında durur; ayrıntı orada. Görseller her sürümde yeniden üretilir, depoya girmez.

## Her sürümde yapılacaklar

1. Sürümü artır: `Package.appxmanifest` → `Identity/@Version` (son hane her zaman `0`) ve `src/LyricsApp/LyricsApp.csproj` → `<Version>`. İkisi aynı olmalı.
2. `docs/releases/<sürüm>.md` dosyasını yaz (bir öncekini örnek al).
3. Gizlilik politikasını gözden geçir: yeni bir ağ çağrısı veya saklanan veri varsa güncelle ve yeniden yayımla.
4. `dotnet test` yeşil olmalı.
5. `.\tools\Packaging\Build-StoreRelease.ps1` → `dist/store`. Ekran görüntülerine göz at.
6. Paketli uygulamayı yerelde bir kez dene (bkz. `docs/store-packaging.md`).
7. Partner Center: paketleri yükle, metinleri ve görselleri `dist/store/README.txt` dosyasına göre yerleştir.
8. Sürüm commit'ini etiketleyip etiketi push et (sürüm çıkarılırken, Store onayını beklemeden): `git tag v<sürüm>` ve `git push origin main v<sürüm>`. `.github/workflows/release.yml` testleri çalıştırır, etiketin manifestteki sürümle (`<sürüm>.0`) aynı olduğunu denetler ve `# GitHub release` bölümündeki ` ```markdown ` bloğuyla GitHub release'i açar (varsa metnini günceller). Release'e dosya eklenmez; uygulama Store'dan dağıtılır. Yeniden çalıştırmak için: Actions → release → Run workflow, `tag` = `v<sürüm>`.

## GitHub release

- Metin İngilizcedir ve kullanıcıya yazılır: kısa giriş, Store rozeti, `## New in <sürüm>` maddeleri, `## Install`. Örnek: `docs/releases/1.1.0.md`.
- İçinde `## What's new` ya da `## Notes for certification` başlığı kullanmayın; `Build-StoreRelease.ps1` bu başlıkları Store metinleri için arar.
