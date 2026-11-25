# WinCompressor — Analiz ve Geliştirme Planu

> Plan tarihi: 2026-11-25 • Orijinal analiz anlık görüntüsü: `840a2d7` (49/49 test yeşil).
>
> **Güncel durum:** 5/5 faz tamamlandı, `dotnet build` 0 uyarı, **155/155 test yeşil**
> (Goldfish kod incelemesi tur 1-5 düzeltmeleri + tam kapsam i18n dahil — bkz. Bölüm 3).
>
> Faz başlıklarındaki gün tahminleri plan anına aittir; güncel süreyi yansıtmaz.

---

## 1. Proje Analizi *(orijinal `840a2d7` anlık görüntüsü — güncel durum için Bölüm 3)*

### 1.1 Mimari

Katmanlı, test edilebilir bir yapı:

| Proje | Sorumluluk |
|---|---|
| `WinCompressor.Core` | İş mantığı: dosya tarama, sıkıştırma, admin işlemleri, Win32 interop |
| `WinCompressor.UI` | WPF (MVVM, code-behind'suz), koyu tema, Türkçe arayüz |
| `WinCompressor.Tests` | 49 xUnit testi (servisler + ViewModel'ler + converter'lar) |

Uygulamanın yaptığı iş: **şeffaf dosya sistemi sıkıştırması** (`compact.exe` üzerinden
NTFS LZNT1 ve CompactOS WOF — *Windows Overlay Filter*: dosya başına şeffaf
sıkıştırma sağlayan çekirdek sağlayıcısı, XPRESS/LZX formatları). Zip üretmez;
dosyalar yerinde kalır ve diskte kapladıkları alan küçülür.

Öne çıkan noktalar:
- `FileScannerService`: BFS ile tarama, reparse-point (symlink/junction) döngü koruması,
  `UnauthorizedAccess` tolerant listeleme, sonuçları boyuta göre azalan sıralı döner.
- `CompressionService`: `Parallel.ForEachAsync` ile paralel sıkıştırma, `IProgress<T>`
  raporlama, iptal desteği, process-tree kill, root-drive güvenlik invariant'ı.
- MVVM: ctor DI'si; test seam'leri (birim testlerde gerçek uygulamayı sahteyle
  değiştirmeye yarayan enjeksiyon noktaları): `FolderPicker`/`ClipboardHandler`;
  `AsyncRelayCommand` kullanımı.

### 1.2 Güçlü Yönler

- Core/UI ayrımı: Core hiçbir UI bağımlılığı içermiyor.
- Root-drive ve directory invariant'ları her işlem girişinde kontrol ediliyor.
- İptal, paralellik ve progress akışları `CancellationToken`, `Parallel.ForEachAsync`
  ve `IProgress<T>` ile kuruldu.
- Kullanıcıya görünen tüm metinler Türkçe; işlemler activity günlüğüne yazılıyor.

### 1.3 Bulunan Sorunlar ve Teknik Borç

**Fonksiyonel bug'lar (öncelik sırasıyla):**

1. **`DecompressFilesAsync` özet raporu bozuk** (`CompressionService.cs:201-258`):
   - `totalSavedBytes` hiç artırılmıyor → progress'te `TotalBytesSaved` hep 0 görünüyor.
   - `TotalAllocatedBytes = totalOriginalBytes` sabit atanıyor → özet yanıltıcı.
   - `DecompressFileAsync` içinde ise doğru hesap var; toplu sürüm `item.BytesSaved`
     değişimini yakalamıyor (decompress sonrası `BytesSaved = Size-Allocated` 0 olması beklenir,
     ancak özet, taramadan gelen değerlerle besleniyor).
2. **`DecompressFileAsync` ilk adım hatası yutuluyor** (`CompressionService.cs:99-116`):
   - `/u /exe` çağrısının (`exitCode1/stdOut1/stdErr1`) sonucu hiç kontrol edilmiyor.
   - İkinci kontrolde `exitCode2 != 0 && stdOut2.Contains("[ERR]")` — mantık `||` olmalı
     (`CompressFileAsync`'teki `||` ile tutarsız).
3. **Non-admin kullanıcı LZX/XPRESS seçebiliyor**: Kartlarda `RequiresAdmin` metadata'sı var
   ama UI'da kartlar devre dışı bırakılmıyor; admin olmayan kullanıcıda her dosya tek tek
   `compact.exe` hatası ile düşer. `AlgorithmOptions` listesi de XAML'da sabit
   kartlar olarak iki kez tanımlanmış (tek kaynak yok — senkron tutulmalı).
4. **Algoritma tespiti eksik**: Tarama, WOF (LZX/XPRESS) ile sıkıştırılmış dosyaları
   `CompressionAlgorithm.Ntfs` olarak işaretliyor (`FileScannerService.cs:218`).
   "Bu dosya neden XPRESS görünüyor?" sorusu cevaplanamıyor. `compact /q` çıktısı ya da
   `WofGetCompressionFormat`/`DeviceIoControl(FSCTL)` ile gerçek format okunabilir.
5. **Tarama progress'i hiç ilerlemiyor**: `ExecuteScanAsync`'te `ProgressPercentage`
   0'dan 100'e atlıyor; yüzde tarama süresince ölü gösteriyor (toplam boyut önceden
   bilinmediğinden "dosya/dizin sayısı" bazlı bir metrik kullanılmalı).

**Performans / UX borcu:**

6. `FilteredFilesCount` her çağrıda `FilesView.Cast<object>().Count()` ile tüm view'ı
   geziyor (`MainViewModel.cs:285`) — büyük listelerde filtre yazarken tekrar tekrar O(n).
7. Tarama sonucu tek parça bellekte toplanıyor; 200k+ dosyada hem bellek hem de
   `Files.Add` döngüsünde UI takılması riski. Sonuçların batch'ler halinde UI'a akıtılması
   (ekleme sırasında `VirtualizingPanel` açık) veya `AddRange`-benzeri toplu ekleme gerekir.
8. `maxDegreeOfParallelism: 4` VM'de sabit — `Environment.ProcessorCount` bazlı olmalı.
9. Kolon sıralaması VM'de `SortDescription` ile sabit; kullanıcı kolon başlığına tıklayınca
   görüntü sırası ile görsel sıralama ayrışıyor (DataGrid `CanUserSortColumns` devre dışı değil
   ama template kolonlarında sort üretilemiyor).
10. Çift tık / Enter ile dosyayı açmak yok (yalnızca context menu).
11. Long-path (>260 karakter) dosyalar `compact.exe` argümanında sorun çıkarabilir;
    `\\?\` öneki ile deneme/güvenli hale getirme planlanmalı.
12. `MainViewModel` 1147 satır — tek dosyada God-class. Scan, Compress, Selection,
    ActivityLog, Filtering sorumlulukları bölünebilir.

**Altyapı borcu:**

13. Ayar kalıcılığı yok (son klasör, algoritma, pencere boyutu, log görünürlüğü her
    açılışta sıfırlanıyor).
14. CI/CD yok (GitHub Actions: build + test matrisi ilk adım).
15. Tek commit'lik tarih; sürümleme `v2.1 GUI Pro` XAML etiketinde hardcode.
16. Türkçe string'ler kod içine gömülü — i18n (resx) istenirse büyük dokunuş gerekir.
17. `ScanOptions.ExcludedDirectories` "AppData" içeriyor; varsayılan tarama yolu
    `UserProfile` — kullanıcı fark etmeden AppData hariç taranıyor. Bilinçli bir karar
    olsa da UI'da görünür/gizlanabilir olmalı.

---

## 2. Geliştirme Yol Haritası

### Faz 1 — Doğruluk Düzeltmeleri (~1-2 gün) ✅ TAMAMLANDI

- [x] 1.1 `DecompressFilesAsync` özet + progress hesaplarını düzelt
      (başarılı dosyada `Size - post-process Allocated` farkını işle, `TotalAllocatedBytes`
      gerçek değerle hesaplansın). Regresyon testleri ekle.
- [x] 1.2 `DecompressFileAsync` ilk `/u /exe` adımının exit code/stdout sonucu artık
      AÇIKÇA değerlendiriliyor; compact.exe WOF-sıkıştırılmamış dosyada bu adımda hata
      bastığından başarısızlık bilinçli olarak **non-fatal**'dir (adım-2 yetkilidir);
      2. adımda `&&` → `||` tutarlılığı. Test: compact hata çıktısı simülasyonu —
      `CompressionService`'e eklenen enjekte edilebilir compact runner seam'i ile
      `ReviewFixRegressionTests` kapsamında (adım-2 exit code, `[ERR]` marker ve
      adım-1 non-fatal yolları kapsayan 5 test).
- [x] 1.3 Non-admin'de LZX/XPRESS kartlarını görsel olarak devre dışı bırak + tooltip;
      `AlgorithmOptions` tek kaynak olarak VM'e bağlan (XAML'deki tekrar eden kart
      template'i `ItemsControl` + `AlgorithmOptions` ile üretilsin).
- [x] 1.4 XPRESS 4K / XPRESS 16K seçeneklerini kart setine ekle (enum'da zaten var).
- [x] 1.5 Tarama progress'i: `ProgressPercentage` yerine taranan dosya/dizin sayısına
      dayalı canlı metrik (ör. her 1000 dosyada rapor + progress bar "belirsiz" modundan
      sayısal moda geçiş).
- [x] 1.6 `FilteredFilesCount` O(1) sayaç tipli yeniden hesaplama.
- [x] 1.7 `maxDegreeOfParallelism` = `Environment.ProcessorCount` (min 2, max 8 clamp).

> **Faz 1 notları:** admin koruması tek yerden yönetiliyor (`CompressionAlgorithmExtensions.RequiresAdmin()`
> + `MainViewModel.EnsureAlgorithmAllowed()`); kart seçimi `AlgorithmOptionViewModel.IsSelected`
> üzerinden iki yönlü senkron; 10 yeni regresyon testi eklendi (59/59 yeşil).

### Faz 2 — Doğru Algoritma Tespiti (~2-3 gün) ✅ TAMAMLANDI

- [x] 2.1 WOF format sorgusu — **yöntem değişikliği**: plan `compact /q` parse'ı
      öngörüyordu; bu makinede (2026-11-25 gözlemi) `compact /q` dosya tablosu
      basmadığı için yerine **dokümante `FSCTL_GET_EXTERNAL_BACKING`
      (0x90310)** kontrol kodu kullanıldı. Kanıt: mingw-w64/Windows SDK `winioctl.h`
      (`WOF_EXTERNAL_INFO`, `WOF_PROVIDER_FILE`, `FILE_PROVIDER_EXTERNAL_INFO_V1`,
      `FILE_PROVIDER_COMPRESSION_*=0..3`). Süreç spawn yok, admin gerekmez,
      in-process.
- [x] 2.2 `FileItemInfo.CurrentAlgorithm` gerçek değeri alıyor. Tarama akışı:
      NTFS Compressed attribute → `Ntfs`; allocated<size & attribute yok → WOF
      şüphelisi → tarama sonunda paralel FSCTL probe → LZX/XPRESS4K/8K/16K.
      Ek düzeltme: sparse dosyalar (allocated<size, WOF değil) artık
      "sıkıştırılmış" sanılmıyor; `None`/`Pending` olarak düzeltiliyor
      (`Pending`: taramada algoritma sorgusu henüz tamamlanmamış ara durum).
- [x] 2.3 DataGrid'e "Algoritma" kolonu + **"CompactOS" filtre chip'i**
      (`FileCategory.CompactOs`, WOF algoritmalarını filtreler).
- [x] 2.4 Dry-run modu: **"🔮 Simüle Et"** butonu (`ISavingsEstimationService`):
      dosyadan 512 KB'lık örnekler alıp Deflate ile sıkıştırır, algoritma bazlı
      çarpanla tahmini tasarruf üretir (çarpanlar: LZX 1.00, XPRESS 16K 0.85,
      XPRESS 8K 0.80, XPRESS 4K 0.75, NTFS 0.60); sonuç "Tahmini
      Tasarruf" kolonunda; gerçek (de)sıkıştırma sonrası tahminler temizlenir.

> **Faz 2 notları:** `CompressionAlgorithm.Unknown`, `IAlgorithmDetectionService` +
> `AlgorithmDetectionService` (FSCTL), `ISavingsEstimationService` + `SavingsEstimationService`,
> `Kernel32.GetWofExternalBacking`, `CompactProcess` (compact.exe plumbing, ortak kullanım).

### Faz 3 — UX ve Kalıcılık (~3-5 gün) ✅ TAMAMLANDI

- [x] 3.1 Ayar servisi: `ISettingsService` + `JsonSettingsService`
      (`%AppData%\WinCompressor\settings.json`, atomik yazım: temp + Replace; bozuk
      dosya → sessiz varsayılanlar). Kalıcı: son yol, algoritma, kategori, alt klasör/
      sadece sıkıştırılmamış/AppData seçenekleri, günlük görünürlüğü, pencere konumu/
      boyutu/maksimize. Standart kullanıcıya admin-only algoritma geri yüklenmez.
- [x] 3.2 DataGrid sıralama: tüm kolonlara `CanUserSort` + `SortMemberPath`
      (Dosya Adı, Yol, Boyut, Diskteki Boyut, Tasarruf, Algoritma, Tahmini, Durum);
      sıralama `FilesView` (ICollectionView) üzerinden — filtre ile uyumlu.
- [x] 3.3 Çift tık + Enter ile dosyayı açma: `OpenFileCommand`
      (`Process.Start` + `UseShellExecute`), eksik dosyada uyarı; satır çift tık ve
      `PreviewKeyDown` code-behind üzerinden bağlı.
- [x] 3.4 Streaming tarama: tarayıcı eşleşen dosyaları `ScanProgressReport`
      üzerinden `FileReportBatchSize` (varsayılan 500) batch'lerle raporlar; UI
      tarama sürerken dolar. UI bağlamı yoksa (testler) batch'ler `ConcurrentQueue`'ya
      alınır — `Files` yalnızca UI bağlamı (dispatcher) üzerinden güncellenir.
      Tarama sonunda tek seferlik yeniden sıralama (boyut azalan; `Files` yeniden doldurularak) korunur; iptalde
      kısmi sonuçlar kalır.
- [x] 3.5 Activity log seviyeleri: `ActivityLogEntry` (Info/⚠ Uyarı/✖ Hata),
      renk kodlaması (yeşil/turuncu/kırmızı), `📤 Dışa Aktar` ile .txt/.csv
      dışa aktarma (CSV kaçışlamalı), otomatik kaydırma mevcut.
- [x] 3.6 Kolon gizleme (başlık sağ tık → "Bu sütunu gizle") + "▦ Sütunlar"
      sıfırlama butonu; "AppData'yı dahil et" seçeneği (tarayıcı `IncludeAppData`,
      varsayılan hariç).
- [x] 3.7 Uzun yol: bu makinede gözlemlendi (2026-11-25) — `\\?\` öneki bile
      compact.exe'te çalışmıyor ("Out of memory"); çözüm 8.3 kısa yol
      (`GetShortPathNameW`): 468 karakterlik yol 163 karaktere düşüp sıkıştırma
      başarılı. `CompressionService.PrepareCompactPath` 260+ karakterde otomatik
      fallback; 8.3 kapalı birimlerde kısa yol bulunamayacağından fallback devreye
      girmez ve uzun yol compact.exe'te başarısız kalır (davranış öncesiyle aynı).
- [x] 3.8 `MainViewModel` bölümleme: partial dosyalar — `.Scan` (tarama/akış),
      `.Compression` (sıkıştırma/simülasyon), `.Filtering` (arama/kategori),
      `.FileItemCommands` (tekil dosya komutları), `.Activity` (günlük),
      `.Settings` (kalıcılık). Public API aynen korundu; Faz 3 kapanışında
      93/93 test yeşildi.

> **Faz 3 notları:** 3.5 kapanışında saptanan sınırlayıcı bağımlılık — `SaveFileDialog` STA
> dışı thread'de testi süresiz askıya alan kilitlenme yaratıyor; `ExportLogsPathPicker` seam'i
> **bağlayıcıdır**, null dönüş = iptal, dialog'a düşülmez. Bu hata tüm test
> paketini donduruyordu. (Faz 5'te `.Automation` partial'ı eklendi — envanter
> şimdi yedi partial dosya + çekirdek `MainViewModel.cs`; bkz. 5.1/5.4/5.5.)

### Faz 4 — Dağıtım ve Kalite (~1-2 gün) ✅ TAMAMLANDI

- [x] 4.1 GitHub Actions (`.github/workflows/ci.yml`): windows-latest, .NET 10 SDK,
      `dotnet build -c Release -warnaserror` + `dotnet test` (trx log) +
      single-file Release publish → artifact yükleme. İmza adımı yorumlu şablon.
- [x] 4.2 Sürüm yönetimi: csproj'ta `Version/AssemblyVersion/FileVersion` (2.1.0) +
      `AssemblyName WinCompressor`; XAML başlığındaki sürüm etiketi artık
      `AppVersionText` ile assembly sürümünden üretiliyor ("v2.1 GUI Pro") —
      etiket tek kaynaktır; XAML'e sabit sürüm metni gömülmediğinden bayatlamaz.
      Regresyon testi: `Phase4ReleaseTests`.
- [x] 4.3 Single-file publish profili (`Properties/PublishProfiles/win-x64-singlefile.pubxml`):
      win-x64, self-contained + sıkıştırmalı tek EXE. Yerel ölçüm (2026-11-25,
      o anki .NET 10 SDK sürümü): `WinCompressor.exe` 61,9 MB, FileVersion 2.1.0.0 —
      SDK/çalışma zamanı güncellemeleriyle boyut değişebilir.
- [x] 4.4 Kod imzalama değerlendirmesi: `app.manifest` eklendi — `asInvoker`
      (standart kullanıcı; yükseltme yalnızca kullanıcı isterse `runas` ile) +
      PerMonitorV2 DPI + Win10/11 supportedOS. Sonuç: self-signed sertifika
      yalnızca kendi makinesinde "Bilinmeyen Yayıncı" uyarısını etkiler; diğer
      kullanıcıların makinesinde SmartScreen / "Bilinmeyen Yayıncı" uyarısını
      kaldırmaz → Azure Trusted Signing (yoksa OV/EV) önerilir; CI'da hazır ama
      devre dışı imza adımı bırakıldı.

> **Faz 4 notları:** MSIX/winget bilinçli olarak ertelendi — MSIX paketi dağıtım
> sertifikası gerektirir; imza alınca değerlendirmeye alınacak (bkz. Bölüm 3
> kalan işler).

### Faz 5 — İleri Özellikler (uzun vade) ✅ TAMAMLANDI

- [x] 5.1 Zamanlanmış otomatik sıkıştırma görevleri: `ScheduledTaskService`
      (`schtasks /Create`, test seam'i ile izole) + VM "🗓 Haftalık Görev" butonu.
      Seçili klasör için **Pazartesi 03:00** görevi; görev bu EXE'nin `cli compress`
      modunu çağırır → GUI'siz periyodik sıkıştırma. Standart kullanıcı + NTFS
      kombinasyonu desteklenir; admin-only algoritmada kullanıcı uyarılır.
- [x] 5.2 CLI: `WinCompressor.Cli` projesi + `CliDispatcher`:
      `scan` (metin/JSON), `compress --algorithm <ad> [--dry-run]`,
      `decompress`; çıkış kodları 0/1/2; admin gerektiren algoritma +
      standart kullanıcı → tek bir açık hata iletisi + exit 1 (fast-fail, T4-N2).
      **Tek EXE passthrough**: `WinCompressor.exe
      cli ...` GUI EXE'inden de çalışır (App.OnStartup yakalar, konsola bağlanır,
      pencere açmaz) — zamanlanmış görev tek dağıtım dosyasıyla çalışır.
      Bu makinede uçtan uca doğrulandı (2026-11-25): gerçek compact.exe ile
      2×64KB dosya NTFS ile toplam 61.440 bayt kazandı (dosya başına 30.720), decompress geri açtı.
- [x] 5.3 i18n altyapısı: XAML dil sözlükleri (`Localization/Strings.tr|en.xaml`)
      + `App.ApplyLanguage` (DynamicResource tabanlı) + kalıcı `Language` ayarı.
      **Tam kapsam:** MainWindow.xaml'deki tüm görsel metinler (pencere başlığı,
      yönetici rozetleri, tarama/quick düğmeleri, algoritma kartları, arama +
      filtre çipleri, seçim araç çubuğu, boş durum, DataGrid sütun başlıkları +
      tooltip'leri, sağ-tık menüleri, günlük çekmecesi, depolama metrikleri,
      eylem düğmeleri) anahtar anahtar sözlüklere taşındı (85 anahtar × TR/EN).
      WPF kısıtları bilinçli çözümlerle aşıldı: DataGrid sütun başlıkları
      `Header` eleman olarak bağlanır (DataGridColumn ağaç üyesi değildir),
      "{0} kayıt" rozeti ve oyun kütüphanesi tooltip'i `Run` kompozisyonuyla
      kurulur (Binding.StringFormat sabittir). Parite `LocalizationParityTests`
      ile kilitlidir (anahtar kümeleri birebir + kopya yok + MainWindow'un
      başvurduğu her anahtar iki sözlükte de tanımlı). VM tarafındaki çalışma
      zamanı status/günlük mesajları bu kapsam dışındadır (kod tarafı Türkçe
      kalır).
- [x] 5.4 Geri alma güvenlik ağı: `JsonCompressionHistoryService`
      (`history.json`, kapasite 500, atomik yazım, bozuk dosya → boş) +
      `CompressionRevertService` (önce tamamen aç, önceki algoritma None değilse
      yeniden uygula) + VM "⤺ Son İşlemi Geri Al" (batch bazlı, iptal destekli).
      Regresyon testi `VM_CompressThenRestore_RoundTripsRealFiles`.
- [x] 5.5 Oyun kütüphanesi tespiti: `GameLibraryService` — Steam + Ubisoft
      (registry), Epic/EA/GOG/Xbox (bilinen varsayılan yollar), hepsi
      `Directory.Exists` ile doğrulanır; UI'da tek tıkla tarama yolu olur
      ("🎮 Steam" vb. çipleri). Bu makinede canlı doğrulandı (2026-11-25):
      EA App + Xbox Game Pass klasörleri bulundu.

> **Faz 5 notları:** revert yeni `FileItemInfo` örnekleri üzerinde çalışır — VM'in
> sahip olduğu modeller senkronize edilmeli, yoksa `IsCompressed` bayatlar;
> `VM_CompressThenRestore_RoundTripsRealFiles` bu senkronu kilitler. Kod
> incelemesi turlarında kapanan ek boşluklar: geçmişe yalnız fiilen sıkışan
> dosyalar yazılır, iptalde kısmi başarılar geçmişe kaydedilir, kısmi revert'te
> başarısız girdilerin kurtarma kaydı korunur; toplu/tekil decompress (iptal
> yolu dahil), açılan dosyaların geçmiş girdilerini düşürür — "⤺ Son İşlemi
> Geri Al" bayat batch'i uygulayıp az önce açılan dosyaları yeniden
> sıkıştırmaz (T4-M1 + T5-M2; testleri: `ReviewFixRegressionTests`).

---

## 3. Durum ve Kalan İşler

Beş fazın tümü kapandı (Faz 1 doğruluk düzeltmeleri → Faz 5 ileri özellikler).
Doğrulama: `dotnet build` 0 uyarı, `dotnet test` **155/155 yeşil** (2026-11-25).
Test kronolojisi: 49 (analiz) → 59 (Faz 1: +10) → 93 (Faz 2: +16, Faz 3: +18)
→ 116 (Faz 4: +2, Faz 5: +21) → 127 (tur 1: +11) → 136 (tur 2: +9) →
144 (tur 3: +8) → 148 (tur 4: +4) → 152 (tur 5: +4) → 155 (tam kapsam
i18n: +3). Ara sayılar fazların eklediği yeni test dosyalarından oluşur.

Kod incelemesi "Goldfish" turlarında (taze örneklenmiş denetçi sürüleriyle
belge-kod çapraz denetimi; raporlar: `docs/review/goldfish_review_report.md`,
`docs/review/goldfish_review_report_round2.md`,
`docs/review/goldfish_review_report_round3.md`,
`docs/review/goldfish_review_report_round4.md`,
`docs/review/goldfish_review_report_round5.md`) kapananlar:

- **Tur 1:** boş tarama sonucu çökmesi, dil ayarının pencere kapanışında
  ezilmesi, toplu (de)sıkıştırmada dosya başına tam-liste rescan, compact hata
  çıktısı simülasyonu için `CompressionService` test seam'i, geri alma
  geçmişindeki iptal/başarısız/kısmi boşluklar, simülasyon progress'in thread
  marshal'ı ve schtask/compact process sağlamlığı. Bu düzeltmelerden K1/K2/K3/K5
  `ReviewFixRegressionTests` ile regresyona bağlandı; K4/K6–K10 kodda uygulandı
  (thread marshal ve süreç içi davranışlar için birim kilit test pratik değildi).
- **Tur 2:** boş-durum yer tutucusunun int sayaçla bool converter'a bağlanması
  (`CountToVisibilityConverter`), iptal filtresinin batch öncesi sıkışmış
  dosyaları sahte geçmişe yazması, tarama/toplu seçimde madde başına O(n²)
  metrik rescan (bastırma + tek güncelleme), revert'te `PreviousAlgorithm="Unknown"`
  dosyalarının açılı sonrası başarılı sayılması, schtasks timeout dalında kill,
  atomik yazımda `.tmp` temizliği. T2-K1/K2/K3/K4/K6 `ReviewFixRegressionTests`
  (T2-K… testleri) ile kilitlendi; T2-K5 süreç içi davranış olduğundan test
  kapsamı dışında bırakıldı. (T2-K6 düzeni hem `JsonSettingsService` hem
  `JsonCompressionHistoryService`'te uygulanmıştır; test yalnız
  `JsonSettingsService` tarafını kapsar.)
- **Tur 3:** üretimde ayar kalıcılığının hiç çalışmaması — `MainWindow` üretim
  ctor'u ayar servisini VM ile paylaşmıyordu ve `PersistSettings` çağrılmıyordu;
  artık tek örnek `CreateProductionDependencies` ile paylaşılıyor (T3-B1,
  `T3B1_ProductionDependencies_ShareSingleSettingsServiceInstance` kilidi).
  Ayrıca schtasks kill korumasının ölü dal olması (akış okumaları önce
  bekleniyordu; beklenme sırası `WaitForExit` → `Kill` → akış okuma olarak
  düzeltildi), iptal filtresinde algoritması değişen dosyaların eksik kaydı,
  CSV satır sonu kaçışlaması, eksik dosyada sessiz kalmak yerine uyarı.
  T3-B1/N1/N2/N3 testlerle kilitlendi; M1 (schtasks sırası) süreç içi davranış
  olduğundan test kapsamı dışında bırakıldı.
- **Tur 4:** toplu "Sıkıştırmayı Kaldır" bayat geri-alma geçmişi bırakıyordu —
  açılan dosyaların geçmiş girdileri artık düşürülüyor (`RemoveEntries`; tekil
  decompress dahil), "⤺ Son İşlemi Geri Al" az önce açılan dosyaları yeniden
  sıkıştırmıyor (T4-M1, 3 test kilidi). Ayrıca: pencere maksimize kapatılınca
  `RestoreBounds` kaydı, CLI'da admin algoritma fast-fail'i, tekil dosya
  işlemlerinde İptal'in gerçek iptali, `AsyncRelayCommand` genel exception
  sigortası ve kod/doküman ufak taneleri. T4-M1/N2 testlerle kilitlendi;
  N1 (STA) kapsam dışı; N3'ün iptal sonucu ve N5'in catch sigortası tur-5
  testleriyle kilitlendi (aşağıda).
- **Tur 5:** tekil dosya işlemlerinde İptal'in sahte hata olarak raporlanması —
  OCE artık ayrıcalıklı `catch (OperationCanceledException)` dalında "iptal
  edildi" iletisiyle sonuçlanır, günlüğe hata yazılmaz (T5-M1) — ve toplu
  "Sıkıştırmayı Kaldır" İPTAL yolunda bayat geri-alma geçmişi: iptal anına
  kadar açılan dosyaların girdileri de düşürülür (T4-M1 filtresinin iptal
  dalına genelleşmesi, T5-M2). Ayrıca `AsyncRelayCommand` catch sigortasının
  birim testi (T5-N4; tur-4'ün "kapsam dışı" gerekçesi kalktı), rapor
  listesi/girinti/üslup kalibrasyonları. T5-M1/M2/N4 testlerle kilitlendi (+4).
- **Tam kapsam i18n (5.3'ün kapanışı):** "Kalan opsiyonel işler"den alınan
  taşınma tamamlandı — MainWindow.xaml'in tüm görsel metinleri (85 anahtar ×
  TR/EN) sözlüklere bağlandı; parite testleri eklendi (LocalizationParityTests,
  +3).

**Kalan opsiyonel işler (dış ön koşullara bağlı):**
- MSIX/winget paketleme: kod imzalama sertifikası alındığında değerlendirilecek.
- Kod imzalama: Azure Trusted Signing (veya OV/EV) hesabı açıldığında CI'daki
  yorumlu imza adımı etkinleştirilecek.
