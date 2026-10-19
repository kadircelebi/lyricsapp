# Microsoft Store paketi (MSIX)

Tarih: 2026-10-19 · Durum: yerel test paketi çalışıyor, Store gönderimi için kimlik ve listeleme bilgileri eksik.

## Nasıl kuruldu

- **Tek proje MSIX.** Ayrı bir paketleme projesi (`.wapproj`) yok. `src/LyricsApp/LyricsApp.csproj` içinde `EnableMsixTooling=true`, manifest `src/LyricsApp/Package.appxmanifest`. Paketleme araçları NuGet'ten gelir (`Microsoft.Windows.SDK.BuildTools` ve `Microsoft.Windows.SDK.BuildTools.MSIX`), Windows App SDK'ya bağımlılık yok.
- **Normal derleme değişmedi.** `dotnet build` / `dotnet run` paketsiz bir exe üretir (`WindowsPackageType=None`). `.msix` yalnızca `GenerateAppxPackageOnBuild=true` ve bir `RuntimeIdentifier` verilince oluşur; o zaman `WindowsPackageType=MSIX` olur.
- **Kendi içinde .NET.** Store'da .NET 10 çerçeve paketi yok, bu yüzden paket .NET çalışma zamanını içinde taşır (`SelfContained=true`). Boyut: x64 ≈ 73 MB, ARM64 ≈ 68 MB (sıkıştırılmış).
- **Platformlar:** x64 ve ARM64, her biri ayrı `.msix`. En düşük Windows sürümü 10.0.17763 (1809, SMTC için gereken sürüm).
- **Tam güven:** `runFullTrust` + `internetClient`. Başka yetenek yok.
- **Simgeler:** `src/LyricsApp/Assets/*.png` ve `src/LyricsApp/LyricsApp.ico`, `do_not_share/logo.png` dosyasından `tools/Packaging/make_store_assets.py` ile üretilir (Pillow gerekir). Küçük simgelerde (görev çubuğu, Başlat listesi, `.ico`, StoreLogo, SmallTile) logonun kendisi yerine betiğin çizdiği şeffaf, metal renkli bir nota kullanılır. Logodaki koyu gri nota siyah taşın üstünde koyu görev çubuğunda kayboluyordu ve gölgeleri taşa karıştığı için temiz kesilemiyordu. Kutucuklar ve açılış ekranı logonun tamamını kullanır. Geliştirme makinesinde görev çubuğu yine eski simgeyi gösterirse sorun Windows'un simge önbelleğindedir: `ie4uinit.exe -show` çalıştırıp uygulamayı yeniden açın. Logo değişirse betiği yeniden çalıştırın. Logonun kendisi depoya girmez.

## Dosyalar paketli uygulamada nereye gider

Yerel testte paketin içinden komut çalıştırılarak doğrulandı (`Invoke-CommandInDesktopPackage`, Windows 11 26300):

| Veri | Kodun gördüğü yol | Gerçek konum (paketli) | Kaldırınca |
|---|---|---|---|
| Ayarlar (`settings.json`), Musixmatch belirteci | `%APPDATA%\LyricsApp` | temiz makinede `%LOCALAPPDATA%\Packages\<PFN>\LocalCache\Roaming\LyricsApp` | silinir |
| Ayarlar, klasör paketten önce varsa | `%APPDATA%\LyricsApp` | gerçek `%APPDATA%\LyricsApp` (yerinde okunur ve yazılır) | kalır |
| Bulunan sözler (`.lrc`/`.txt`) | `Müzik\Lyrics` | aynı yer, gerçek Müzik klasörü | kalır |

- `%APPDATA%` ve `%LOCALAPPDATA%` altında **yeni** oluşturulan klasörler pakete özel klasöre yönlendirilir. Kod değişmeden çalışır, kaldırınca temizlenir. Store kullanıcısının makinesinde `LyricsApp` klasörü olmadığı için bu durum geçerlidir (Store için istenen davranış).
- **Klasör zaten varsa** (paketsiz sürüm daha önce çalıştırıldıysa) paketli uygulama gerçek klasörü kullanır: aynı ayarları görür, yeni dosyaları da oraya yazar ve kaldırınca klasör kalır. Geliştirme makinesinde paketli ve paketsiz sürüm bu yüzden ayarları paylaşır.
- Doğrulamayı paketli bir uygulamanın içinden açılmış bir terminalden yapıyorsanız (ör. kendisi MSIX ile kurulmuş bir uygulamanın terminali) dikkat edin: o terminalin kendi `%APPDATA%` görünümü de sanallaştırılmış olabilir. Gerçek klasörü görmek için Explorer'ı veya `Invoke-CommandInDesktopPackage` ile paketin içinden çalışan bir komutu kullanın.
- **Müzik klasörü sanallaştırılmaz.** Tam güvenli uygulama normal dosya API'leriyle yazabildiği için `musicLibrary` yeteneği gerekmez ve eklenmedi. Sözler kullanıcıya ait, görünür dosyalar olarak kalır; kullanıcı kendi `.lrc` dosyalarını da buraya koyabilir. Bu yüzden konum `LocalAppData`'ya taşınmadı. Gizlilik politikasında belirtilmeli.
- Kullanıcı söz klasörünü `%APPDATA%` altına bir yere değiştirirse, "Söz klasörünü aç" Explorer'da gerçek (boş) klasörü gösterir. Varsayılan Müzik klasöründe bu sorun yok.

## Paketli çalışma doğrulaması

x64 paketinin içeriği Geliştirici Modu ile kaydedilip (`Add-AppxPackage -Register`) Başlat menüsü yolundan (`shell:AppsFolder\<PFN>!App`) açıldı:

- Süreç paket kimliğiyle çalıştı (`TODO.MrWLyrics_0.2.0.0_x64__46x8tv3x9xtnj`).
- SMTC çalan şarkıyı buldu, sözler internetten geldi ve pencerede satırlar göründü (`internetClient` yeterli).
- Paket kaldırıldı, test dosyaları silindi.

ARM64 paketi derleniyor ve manifesti doğru (`ProcessorArchitecture="arm64"`), ama ARM64 cihazda denenmedi.

## Yerel test paketi

Gerekenler: .NET 10 SDK, Python + Pillow (yalnızca simgeleri yeniden üretmek için). Visual Studio gerekmez.

1. Test sertifikası (bir kez). `CurrentUser\My` içinde kendinden imzalı bir kod imzalama sertifikası oluşturur. Konu adı manifestteki `Publisher` ile aynıdır. Açık anahtar `do_not_share\signing\LyricsApp-dev.cer` dosyasına çıkar (git'e girmez):

   ```powershell
   .\tools\Packaging\New-DevCertificate.ps1
   ```

2. Paketleri derleyip imzalayın (x64 + ARM64, çıktı `artifacts\msix\`):

   ```powershell
   .\tools\Packaging\Build-Msix.ps1
   ```

   Tek platform için `-Platforms x64` ekleyin. Elle derlemek için:

   ```powershell
   dotnet publish src/LyricsApp -c Release -r win-x64 -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true
   ```

3. Kurulum, iki yol:

   - **`.msix` dosyasını kurmak:** Windows yalnızca güvenilen bir köke zincirlenen paketleri kurar (yoksa hata `0x800B0109`). Sertifikaya bir kez, **yönetici** PowerShell'de güvenin, sonra kurun:

     ```powershell
     Import-Certificate -FilePath do_not_share\signing\LyricsApp-dev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
     ```

     ```powershell
     Add-AppxPackage artifacts\msix\LyricsApp_0.2.0.0_x64_Test\LyricsApp_0.2.0.0_x64.msix
     ```

   - **Sertifikasız (Geliştirici Modu):** `.msix` bir zip dosyasıdır. Bir klasöre açın, `AppxSignature.p7x`, `AppxBlockMap.xml` ve `[Content_Types].xml` dosyalarını silin, sonra:

     ```powershell
     Add-AppxPackage -Register <klasör>\AppxManifest.xml
     ```

4. Başlat menüsünden "MrW Lyrics" ile açın. Kaldırmak için:

   ```powershell
   Get-AppxPackage MrWProject.MrWLyrics | Remove-AppxPackage
   ```

Test sertifikası yalnızca bu makine içindir. Store'a imzalı paket gönderilmez; Store paketi kendisi imzalar.

## Store gönderimi için eksikler

1. **Partner Center hesabı** (bireysel geliştirici hesabı) ve uygulama adı rezervasyonu. "MrW Lyrics" adı alınamazsa manifestteki `DisplayName` (iki yerde) değişir.
2. **Kimlik (yapıldı, 2026-10-19).** Manifestte Partner Center değerleri var: `MrWProject.MrWLyrics`, `CN=BE7956F6-3D06-4F0C-B8B9-FAA5D1653D9F`, `MrWProject`. Store ID `9N5X2VHWXJWQ`. Yerel test sertifikası daha önce yer tutucu yayıncıyla oluşturulduysa `New-DevCertificate.ps1` yenisini kendiliğinden oluşturur (konu adı manifestteki Publisher'dır).
3. **Store paketi:** `.\tools\Packaging\Build-StoreRelease.ps1` x64 ve ARM64 `.msix` dosyalarını imzasız derleyip `dist\store\packages\` klasörüne koyar. İkisini de aynı gönderime yükleyin. Sürümün son hanesi `0` olmalı (`1.0.0.0`); her gönderimde artırın. Gönderimin tamamı için bkz. `docs/releases/README.md`.
4. **Gizlilik politikası URL'si** (zorunlu, uygulama internete çıkıyor). İçermesi gerekenler: çalan şarkının adı, sanatçısı, albümü ve süresi seçili söz kaynaklarına gönderilir (LrcLib; açıksa Musixmatch, Genius, lyrics.ovh/Deezer); hesap, telemetri, ses kaydı yok; ayarlar ve Musixmatch belirteci yerelde (paket klasöründe) tutulur; bulunan sözler `Müzik\Lyrics` klasörüne kaydedilir ve kaldırınca kalır.
5. **Yaş derecelendirmesi** (IARC anketi). Şarkı sözleri kullanıcının çaldığı müzikten gelir ve filtrelenmez; ankette "kullanıcıya bağlı / filtrelenmemiş içerik" sorularını buna göre yanıtlayın.
6. **Ekran görüntüleri** (en az bir tane, 1366×768 veya daha büyük; önerilen 1920×1080). Telifli şarkı sözleri görünmemeli: kendi yazdığınız sözlerle yerel bir `.lrc` dosyası kullanın. Arayüz Windows'un dilini izler; Türkçe Windows'ta İngilizce görüntüler için uygulamayı `LYRICSAPP_LANGUAGE=en` ortam değişkeniyle açın (`dotnet run`, paketli sürüm bu değişkeni almaz), ayarlarınıza dokunmamak için `LYRICSAPP_SETTINGS_DIR` ile birlikte.
7. **Listeleme metni** (Türkçe ve isteğe bağlı İngilizce açıklama, anahtar kelimeler), kategori (Müzik), destek iletişim bilgisi. İsteğe bağlı Store logoları (1:1 kutu resmi 1080×1080 / 2160×2160, 2:3 poster 720×1080) logodan üretilebilir.
8. **`runFullTrust` gerekçesi.** Kısıtlı yetenek olduğu için gönderimde kısa bir açıklama istenir, örneğin: "Win32/WPF masaüstü uygulaması; Windows medya oturumunu (SMTC) okur, sözleri yerel klasöre kaydeder."

## Politika riski: Musixmatch ve Genius

- **Musixmatch** sağlayıcısı Musixmatch'in iOS uygulamasını taklit eden resmi olmayan bir API kullanıyor. **Genius** sağlayıcısı sayfa kazıyor. İkisi de bu servislerin kullanım koşullarına aykırı olabilir ve Store'un fikri mülkiyet ve içerik politikalarına takılabilir. Ayrıca her an bozulabilirler.
- Şu an `AppSettings.EnabledProviders` varsayılanı **tüm** kaynakları açık başlatıyor. Store sürümü için önerilen: varsayılanı yerel klasör + LrcLib yapmak, Musixmatch/Genius/lyrics.ovh'yi ya tamamen çıkarmak ya da kullanıcının bilerek açacağı bir ayara bırakmak. Bu değişiklik bu paketleme işinde yapılmadı.
- lyrics.ovh resmi bir API ama arama için Deezer'ı kullanıyor; gizlilik politikasında anılmalı.
