# Goldfish Annotated Review Report — Tur 2

**Status: resolved (auto accept — 16 PROPOSE FIX uygulandı, 3 DECLINE kayıtlı, 0 CONTEST, 0 ESCALATE) — 2026-11-25**
**Doğrulama:** `dotnet build` 0 uyarı (Release `-warnaserror` dahil), `dotnet test` 136/136 yeşil (127 + 9 yeni tur-2 regresyon testi)
**Tur 1 doğrulaması:** 2 denetçi bağımsız onayladı — K1–K10 eksiksiz uygulandı, hiçbiri işlevi bozmadı.
**Target:** `docs/GELISTIRME-PLANI.md` (tam dosya, satır 1-281) + kod tabanı
**Tarih:** 2026-11-25 • **Tur:** 2 (taze denetçi örnekleri) • **Denetçiler:** codebase-details, implementation-readiness, comprehension, structural, style
**Önceki tur:** tur 1 (18 PROPOSE FIX uygulandı, 4 DECLINE kayıtlı — bkz. `goldfish_review_report.md`). Kayıtlı kararlar D9–D12 denetçilere bildirildi; hiçbisi yeniden raporlanmadı.

## Tur 1 Düzeltme Doğrulaması (2 denetçi bağımsız onayladı)

- **K1–K10'un tamamı gerçekten uygulanmış**; hiçbiri önceki işlevi bozmuyor (K4 throttle iptal/tamamlanma yollarında revalidasyon yapıyor; K5 filtresi tekil dosya yolundan bağımsız).
- **127/127 test sayısı birebir doğru** (15 dosyada elle sayıldı); Faz 1–5 gövdesindeki somut sayı/sabit/davranış iddiaları koda sadık (FSCTL 0x90310, çarpanlar LZX 1.00…NTFS 0.60, Pazartesi 03:00 görevi, CSV kaçışlama, CI içeriği vb.).
- Tur 1 doküman kalibrasyonları (tarih etiketleri, kapsam daraltmaları) tutuyor.

## Konsolide Bulgular

### Kod bulguları (yeni)

| # | Sev | Bulgu | Kaynak | Konum |
|---|---|---|---|---|
| T2-K1 | 🟡 Major | **Boş-durum yer tutucusu hep görünür**: `Files.Count` (int) `BoolToVisibilityConverter`'a bağlanıyor; converter yalnız `value is bool` tanıyor → `boolVal=false` + `Inverse=true` → her zaman Visible. Liste doluyken de "Henüz dosya taranmadı" gösterilir (opak DataGrid altında hayalet metin). **[CONTEST sonrası doğrulandı]** | codebase-details B1 | `MainWindow.xaml:717-718`, `BoolToVisibilityConverter.cs:12-18` |
| T2-K2 | 🟡 Major | **K5 iptal filtresi batch kapsamlı değil**: `Status=="Compressed"` kalıcı durum olduğundan, önceki batch'ten zaten sıkışmış bir dosya yeni batch'e seçilip batch ona ulaşmadan İPTAL edilirse, `FilterCompressedEntries` ona hiç dokunulmadığı hâlde geri alma kaydı üretir → sahte geçmiş + yanıltıcı "N dosya sıkışmıştı" günlüğü. **[CONTEST sonrası doğrulandı]** | codebase-details B2 | `MainViewModel.Compression.cs:129-136, 453-463` |
| T2-K3 | 🟡 Major | **Tarama akışı O(n²)**: `AddScannedFiles` nesne başlatıcısındaki `IsSelected = !model.IsCompressed` her uncompressed dosya için `_onSelectionChanged?.Invoke` → `UpdateSelectionMetrics()` tam liste taraması; akışta bastırma yok. 100k dosyada tarama kilidi. (`_isUpdatingSelectionAll` bu yolda kullanılmıyor.) **[CONTEST sonrası doğrulandı]** | codebase-details B3 | `MainViewModel.Scan.cs:299-303`, `FileItemViewModel.cs:27`, `MainViewModel.cs:515-518` |
| T2-K4 | 🟢 Nit | **Revert'te "Unknown" önceki algoritma kilitli kalıyor**: `PreviousAlgorithm="Unknown"` girdisinde adım-1 (decompress) başarılı olsa bile adım-2 `CompressFileAsync(Unknown)` `ArgumentOutOfRangeException` → dosya her denemede "Failed"; fiilen açılmış dosya sonsuza dek "geri alınamaz" görünür. | codebase-details B5 | `CompressionRevertService.cs:58-64`, `CompressionService.cs:287-297` |
| T2-K5 | 🟢 Nit | **schtasks timeout dalı sınırsız bloklayabilir**: `WaitForExit(10_000)` false dönerse `WaitForExit()` (süresiz) çağrılıyor — asılı schtasks, arayan thread'i sonsuza dek kilitler. Kill + hata dönüşü daha güvenli. | impl-readiness B3 | `ScheduledTaskService.cs:88-91` |
| T2-K6 | 🟢 Nit | **Kalıcı yazımda temp kalıntısı/TOCTOU**: `File.Replace`/`Move` exception fırlatırsa `.tmp` temizlenmiyor; `File.Exists`+`Move` arasında yarış. `File.Move(temp, dest, overwrite: true)` + finally temizliği daha sağlam. | codebase-details B7 | `JsonSettingsService.cs:60-71`, `JsonCompressionHistoryService.cs:101-111` |

### Doküman bulguları

| # | Sev | Bulgu | Kaynak |
|---|---|---|---|
| T2-D1 | 🟡 Major | **"Tümü `ReviewFixRegressionTests` ile regresyona bağlandı" aşırı iddiası**: dosya fiilen yalnız K1/K2/K3/K5'i kapsıyor (11 test); K4/K6/K8/K9 için test yok. Üç denetçi bağımsız yakaladı. | comprehension #1, impl-readiness B1, codebase-details B4 |
| T2-D2 | 🟢 Nit | **Bölüm 1 güncel gibi okunuyor**: 1.1 tablosunda "49 xUnit testi", 1.2'de "tüm metinler Türkçe" — anlık görüntü işareti yalnız başlıkta; bölüm düzeyinde işaret yok. | style A2, comprehension #4, codebase-details B6, impl-readiness B4 |
| T2-D3 | 🟢 Nit | **`>` not kalıpları birleşmiyor**: Faz 1–2 notları (`> Uygulama notları:`, `> Yeni/özelleşen tipler:`) ile Faz 3–5'in `> **Faz N notları:**` kalıbı dört farklı kural. | structural B1 |
| T2-D4 | 🟢 Nit | **Başlık bloğu render'da tek paragraf**: satır 3–6 arası boş `>` ayracı yok. | structural B2 |
| T2-D5 | 🟢 Nit | **3.4'te kalan "asla" absolütü**: "Files koleksiyonuna asla yabancı thread'den dokunulmaz" — tur 1 kalibrasyonundan kaçmış. | style A1 |
| T2-D6 | 🟢 Nit | **"Yedi dosya" envanteri belirsiz** (fiziksel 8 dosya; "yedi" yalnız partial sayısı — çekirdek `MainViewModel.cs` sayılmıyor). | comprehension #2 |
| T2-D7 | 🟢 Nit | **Dolgu/tutarsız sözcükler**: 1.3'te "aslında"/"zaten"; "göçürülebilir" (5.3) vs. "taşınması" (Bölüm 3). | style A5, A6 |
| T2-D8 | 🟢 Nit | **Kalın/`EDİLMELİ` vurguları**: 3.7 "**8.3 kısa yol**", Faz 5 notu "senkronize **EDİLMELİ**" — D11 emsaliyle tutarsız. | style A3 |
| T2-D9 | 🟢 Nit | **Test kronolojisi 93→127 sıçraması plan içinde açıklanmıyor** ("116" yalnız raporda). | comprehension #3 |
| T2-D10 | 🟢 Nit | **"Goldfish turu" terimi tanımsız** (taktiksel olarak rapor linki çözüyor; tek cümlelik açıklama yeterli). | comprehension #5 |
| T2-D11 | 🟢 Nit | **Faz 3 notu üslup**: "kritik ders" + "sonsuz ASILMA" editoryal çerçeveleme. | style A4 |

### DECLINE önerileri

| # | Bulgu | Gerekçe |
|---|---|---|
| T2-X1 | K4 residual maliyeti (her 25 raporda hâlâ ~5 O(n) geçiş) | 25× iyileşme fiilen kanıtlı; `TotalBytesSaved` kümülatif refactor'u yüksek churn/düşük kazanç. Tur 1'in kabul edilmiş tasarım kararı. |
| T2-X2 | Bölüm 1 numaraları (1.1–1.3) ile faz madde numaraları (1.1–1.7) çakışması | Kalıtsal şema; denetçi de "dokunulmayabilir" dedi. D9 ruhunda. |
| T2-X3 | "code-behind'suz (MVVM)" nitelemesi (1.1) | Olay bağlama code-behind'ları doğası gereği var; anlık görüntü çerçevesi (T2-D2) bunu da kapatıyor. |

## Sayılar

| Kategori | Adet |
|---|---|
| 🔴 Blocker | 0 |
| 🟡 Major (kod) | 3 (T2-K1, T2-K2, T2-K3 — hepsi CONTEST doğrulamalı) |
| 🟡 Major (doküman) | 1 (T2-D1) |
| 🟢 Nit (kod) | 3 (T2-K4, T2-K5, T2-K6) |
| 🟢 Nit (doküman) | 11 (T2-D2 … T2-D11'in parçası; D6/D7 tarihsel gözle doğrudan) |
| DECLINE önerisi | 3 (T2-X1, T2-X2, T2-X3) |
| CONTEST / ESCALATE | 0 (3 bulgu CONTEST sonrası DOĞRULANDI — reddedilen yok) |

**PROPOSE FIX toplamı:** 16 (T2-K1…K6 + T2-D1…D11, D-grubu 11 madde 10 düzeltmede birleşebilir) • önerilen DECLINE: 3
