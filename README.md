# LyricsApp

Hafif, üyeliksiz bir masaüstü şarkı sözü uygulaması. Windows'un medya oturumundan (SMTC) çalan şarkıyı okur ve senkron sözleri satır satır takip eder. Ses kaydı, fare kancası ve telemetri yok.

## Çalıştırma

```bash
dotnet run --project src/LyricsApp
```

Teşhis aracı. Çalan şarkıyı ve çevrimiçi kaynakların sonucunu konsola yazar (söz metnini değil, yalnızca satır sayısını ve zamanlamayı):

```bash
dotnet run --project tools/LyricsApp.Probe
```

Belirli bir şarkıyı tek bir kaynakta denemek için (`lrclib`, `musixmatch`, `genius`, `lyricsovh`):

```bash
dotnet run --project tools/LyricsApp.Probe -- --provider musixmatch "Sanatçı" "Şarkı"
```

Microsoft Store paketi (MSIX, x64 + ARM64, `artifacts\msix\` altına). Ayrıntılar ve kurulum: [docs/store-packaging.md](docs/store-packaging.md)

```powershell
.\tools\Packaging\Build-Msix.ps1
```

Testler:

```bash
dotnet test
```

## Yapı

| Proje | İçerik |
|---|---|
| `src/LyricsApp.Core` | LRC ayrıştırıcı, başlık temizleyici, eşleştirici, sağlayıcılar (yerel klasör, LrcLib, Musixmatch, Genius, lyrics.ovh), `ProviderCatalog`, `LyricsService` |
| `src/LyricsApp.Media` | `MediaWatcher`: olay tabanlı SMTC dinleyicisi (yoklama yok) |
| `src/LyricsApp` | WPF arayüzü. Metinler `Localization/` altında (`Strings.resx` İngilizce, `Strings.tr.resx` Türkçe) |
| `tools/LyricsApp.Probe` | Canlı teşhis aracı |
| `tools/Packaging` | MSIX derleme, test imzası ve Store görselleri betikleri |
| `tests/LyricsApp.Core.Tests` | xUnit testleri |
| `tests/LyricsApp.Tests` | Arayüz metinlerinin testleri (iki dilde de aynı anahtarlar) |

## Davranış

- Sağlayıcı sırası: **yerel klasör** (`Müzik\Lyrics`) → **LrcLib** → **Musixmatch** → **Genius** → **lyrics.ovh**. Senkron söz bulunursa hemen kullanılır. Yalnızca düz metin bulunursa senkron sürüm aranmaya devam edilir. Hata veren kaynak atlanır.
- Musixmatch (resmi olmayan mobil API) ve Genius (sayfa kazıma) her an kırılabilir. Genius ve lyrics.ovh yalnızca düz metin verir. Musixmatch belirteci `%APPDATA%\LyricsApp\musixmatch-token.txt` dosyasında saklanır.
- Sıra ve açık/kapalı: `settings.json` içindeki `EnabledProviders` listesi (`local`, `lrclib`, `musixmatch`, `genius`, `lyricsovh`). Listeden çıkarılan kaynak kapanır. Bulunan sözler yine de yerel klasöre kaydedilir.
- İnternetten bulunan sözler `Müzik\Lyrics\Sanatçı - Şarkı.lrc` (veya `.txt`) olarak kaydedilir. Sonraki dinlemelerde ağa çıkılmaz. Kendi `.lrc` dosyalarınızı da bu klasöre koyabilirsiniz.
- Senkron takip yalnızca söz senkronken, müzik çalarken ve pencere simge durumunda değilken çalışır (100 ms).
- Zaman kaydırma: başlıktaki −/+ (0,5 sn adım). Sıfırlamak için değere tıklayın.
- **Görünüm** (başlıktaki ⚙ veya sağ tık → Görünüm ayarları):
  - **Her zaman üstte** (📌)
  - **Sadece sözler** (👁): pencere tamamen saydamlaşır, yalnızca sözler kalır. Fareyi yalnızca harfler yakalar: harflerin dışındaki boş alanlara yapılan tıklamalar arkadaki pencereye geçer. Bir harfe tıklayınca başlık ve arka plan görünür, fare pencereden çıktıktan 1,5 sn sonra yeniden gizlenir. Harften tutup sürüklemek pencereyi taşır, çift tıklamak modu kapatır.
  - **Karaoke odak** (☰): yalnızca aktif satır ile öncesindeki ve sonrasındaki N satır (varsayılan 2) görünür, uzaklaştıkça solar.
  - **Satır dolum efekti**: aktif satır söylendikçe soldan sağa dolar. Çalarken her karede yeniden çizim yaptığı için varsayılan olarak kapalıdır.
  - Yazı tipi, boyut, aktif ve diğer satır rengi, gölge (renk, yumuşaklık, uzaklık, yoğunluk), arka plan opaklığı ve hazır stiller (Klasik, Karaoke sarı, Neon, Sade).
- Pencere kenarlıksızdır: başlıktan veya boş bir alandan sürükleyerek taşıyın, kenarlarından boyutlandırın. Sağ tık menüsünde tüm seçenekler bulunur.
- **Dil:** arayüz Türkçe ve İngilizcedir, Windows'un görüntüleme dilini izler: Windows dili Türkçeyse (`tr*`) Türkçe, başka her dilde İngilizce. Sayılar (yüzde, zaman kaydırma) Windows'un bölge biçimiyle yazılır. Uygulamada ayrı bir dil seçeneği yoktur.
- Yeni bir arayüz metni hem `Strings.resx` hem `Strings.tr.resx` dosyasına eklenir (XAML'de `{local:Loc Anahtar}`, kodda `Strings.Anahtar`). İki dosyanın anahtarları aynı değilse `dotnet test` başarısız olur. Store açıklaması (manifest) `Localization/en-US` ve `Localization/tr-TR` altındaki `Resources.resw` dosyalarından gelir.
- Test için ayrı bir ayar klasörü kullanmak isterseniz `LYRICSAPP_SETTINGS_DIR` ortam değişkenini ayarlayın. Dili zorlamak için (test ve Store ekran görüntüleri) `LYRICSAPP_LANGUAGE=en` veya `LYRICSAPP_LANGUAGE=tr` kullanın. Bu değişkenler yalnızca geliştirme içindir; Store'dan kurulan uygulama Windows'un dilini kullanır.
- Ayarlar: `%APPDATA%\LyricsApp\settings.json`

