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

Testler:

```bash
dotnet test
```

## Yapı

| Proje | İçerik |
|---|---|
| `src/LyricsApp.Core` | LRC ayrıştırıcı, başlık temizleyici, eşleştirici, sağlayıcılar (yerel klasör, LrcLib, Musixmatch, Genius, lyrics.ovh), `ProviderCatalog`, `LyricsService` |
| `src/LyricsApp.Media` | `MediaWatcher`: olay tabanlı SMTC dinleyicisi (yoklama yok) |
| `src/LyricsApp` | WPF arayüzü |
| `tools/LyricsApp.Probe` | Canlı teşhis aracı |
| `tests/LyricsApp.Core.Tests` | xUnit testleri |

## Davranış

- Sağlayıcı sırası: **yerel klasör** (`Müzik\Lyrics`) → **LrcLib** → **Musixmatch** → **Genius** → **lyrics.ovh**. Senkron söz bulunursa hemen kullanılır. Yalnızca düz metin bulunursa senkron sürüm aranmaya devam edilir. Hata veren kaynak atlanır.
- Musixmatch (resmi olmayan mobil API) ve Genius (sayfa kazıma) her an kırılabilir. Genius ve lyrics.ovh yalnızca düz metin verir. Musixmatch belirteci `%APPDATA%\LyricsApp\musixmatch-token.txt` dosyasında saklanır.
- Sıra ve açık/kapalı: `settings.json` içindeki `EnabledProviders` listesi (`local`, `lrclib`, `musixmatch`, `genius`, `lyricsovh`). Listeden çıkarılan kaynak kapanır. Bulunan sözler yine de yerel klasöre kaydedilir.
- İnternetten bulunan sözler `Müzik\Lyrics\Sanatçı - Şarkı.lrc` (veya `.txt`) olarak kaydedilir. Sonraki dinlemelerde ağa çıkılmaz. Kendi `.lrc` dosyalarınızı da bu klasöre koyabilirsiniz.
- Senkron takip yalnızca söz senkronken, müzik çalarken ve pencere simge durumunda değilken çalışır (100 ms).
- Zaman kaydırma: başlıktaki −/+ (0,5 sn adım). Sıfırlamak için değere tıklayın.
- Ayarlar: `%APPDATA%\LyricsApp\settings.json`

