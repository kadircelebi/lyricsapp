# Store sürümleri: kurallar

## Nereye bakılır

- **`dist/store/`**: her zaman **gönderilecek son sürüm.** Partner Center'a gereken her şey burada: paketler, metinler, görseller. Neyin nereye gittiği `dist/store/README.txt` dosyasında yazar.
- **`dist/old_versions/<sürüm>/`**: daha önceki sürümler. Yeni sürüm hazırlanınca önceki `dist/store` buraya taşınır, hiçbir şey silinmez.
- `dist/` git'e girmez. Kaynaklar depoda: listeleme metinleri `docs/store-listing/`, sürüme özel metinler `docs/releases/<sürüm>/`, görsel ve paket betikleri `tools/Packaging/`.

`dist/store` tek komutla hazırlanır:

```powershell
.\tools\Packaging\Build-StoreRelease.ps1
```

Betik sürümü `Package.appxmanifest` dosyasından okur. `dist/store` başka bir sürümse önce onu `dist/old_versions/` klasörüne taşır. Sonra imzasız x64 + ARM64 paketleri derler, metinleri kopyalar, poster ve kutu resmini üretir ve ekran görüntülerini (tr + en, demo modunda) alır. Aynı sürümü yeniden derlemek `dist/store` klasörünü yeniler.

## Her sürümün metinleri: `docs/releases/<sürüm>/`

Sürüm, `Identity/@Version` değerinin ilk üç hanesidir (`1.0.0.0` → `1.0.0`).

| Dosya | Partner Center'daki yeri | Ne zaman | Sınır |
|---|---|---|---|
| `certification-notes.txt` | Submission options → *Notes for certification* | her sürüm | 2000 karakterin altında |
| `whats-new.tr.txt`, `whats-new.en.txt` | Store listeleme → *What's new in this version* | **yalnız güncellemelerde** (ilk sürümde yok) | 1500 karakter |
| `checklist.md` | — (bizim için) | her sürüm | — |

- `.txt` dosyaları Partner Center'a **olduğu gibi yapıştırılır**: düz metin, Markdown yok, madde işareti `•`.
- **What's New** kullanıcıya yazılır: ne değişti, ne kazandı. İç ayrıntı yok. İki dilde içerik aynıdır.
- **Notes for Certification** İngilizcedir ve inceleme ekibine yazılır. Güncellemelerde metnin **en üstünde** bir `Changes in this version:` paragrafı olur: yalnız inceleme ekibini ilgilendiren değişiklikler (yeni izin, ağ bağlantısı, dosya erişimi…). Altında sabit açıklama gelir: uygulama ne yapar, nasıl denenir, hesap gerekmez, hangi servise ne gider, `runFullTrust` gerekçesi. Bir önceki gönderim reddedildiyse ret nedenine nasıl çözüm bulunduğu da yazılır.

## Listeleme metinleri ve görseller

Kalıcı listeleme metinleri (açıklama, özellikler, anahtar kelimeler) ve gizlilik politikası `docs/store-listing/` altında durur; ayrıntı orada. Görseller her sürümde yeniden üretilir, depoya girmez.

## Her sürümde yapılacaklar

1. Sürümü artır: `Package.appxmanifest` → `Identity/@Version` (son hane her zaman `0`) ve `src/LyricsApp/LyricsApp.csproj` → `<Version>`. İkisi aynı olmalı.
2. `docs/releases/<sürüm>/` dosyalarını yaz, karakter sınırlarını kontrol et.
3. Gizlilik politikasını gözden geçir: yeni bir ağ çağrısı veya saklanan veri varsa güncelle ve yeniden yayımla.
4. `dotnet test` yeşil olmalı.
5. `.\tools\Packaging\Build-StoreRelease.ps1` → `dist/store`. Ekran görüntülerine göz at.
6. Paketli uygulamayı yerelde bir kez dene (bkz. `docs/store-packaging.md`).
7. Partner Center: paketleri yükle, metinleri ve görselleri `dist/store/README.txt` dosyasına göre yerleştir.
8. Gönderimden sonra sürüm commit'ini etiketle: `git tag v<sürüm>`.
