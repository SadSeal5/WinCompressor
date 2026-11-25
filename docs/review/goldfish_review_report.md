# Goldfish Annotated Review Report

**Status: resolved (auto accept — 18 PROPOSE FIX uygulandı, 4 DECLINE kayıtlı, 0 CONTEST, 0 ESCALATE) — 2026-11-25**
**Doğrulama:** `dotnet build` 0 uyarı (Release `-warnaserror` dahil), `dotnet test` 127/127 yeşil (116 + 11 yeni regresyon: `ReviewFixRegressionTests`)**

**Target:** `docs/GELISTIRME-PLANI.md` (`full file`, satır 1-243) — kod tabanıyla karşılaştırmalı denetim
**Tarih:** 2026-11-25 • **Tur:** 1 • **Denetçiler:** codebase-details, implementation-readiness, comprehension, structural, style

## Genel Sonuç

Dokümanın 5 faza dair somut referansları (tipler, dosyalar, sabitler, sayılar) kodta birebir doğrulandı: `FSCTL 0x90310`, çarpanlar (LZX 1.00…NTFS 0.60), batch=500, kapasite 500, çıkış kodları 0/1/2, Pazartesi 03:00, `Math.Clamp(ProcessorCount,2,8)`, atomik yazım, admin koruması, 8.3 fallback, CI/manifest/pubxml içerikleri — hepsi koda sadık. 17 sorun ↔ faz eşlemesi eksiksiz. Ana uyuşmazlıklar: **bir Blocker** (Faz 1.2 iddiası), **4 gerçek kod hatası Major** (boş tarama çökmesi, dil ayarı kalıcılığı, O(n²) progress, geri alma ağı boşlukları) ve doküman tarafında başlık–gövde senkronizasyonu + iddia kalibrasyonu.

---

## 🔴 BLOCKER

### [K1] Faz 1.2 "compact hata çıktısı simülasyonu" testi yok; iddia edilen adım-1 kontrolü kodda yok `[implementation-readiness + codebase-details]`
- **Kapsam:** Doküman satır 101-102 • `CompressionService.cs:95-103` • `tests/` (tüm dosyalar tarandı)
- **Açıklama:** Doküman "`[x]` ilk `/u /exe` adımının exit code/stdout kontrolünü ekle … **Test: compact hata çıktısı simülasyonu**" diyor. Kodda adım-1 sonucu `(exitCode1, stdOut1, stdErr1)` hiç kullanılmıyor (yorum: non-WOF dosyada compact `/u /exe` zaten hata basar → non-fatal). İddia edilen simülasyon testi hiçbir test dosyasında yok; `CompactProcess` static ve seam'siz olduğundan mevcut tasarımda yazılamaz da.
- **AI Önerisi:** **PROPOSE FIX** (kod + doküman)
- **Gerekçe:** "Test edilmiş" iddiası test dosyasında yoksa Blocker. İki yol var: (a) doküman metnini fiilî karara göre düzelt, (b) kodu iddiaya uygun hale getir (seam + testler). (b) daha değerli: arka planda gerçek hata sınırlarını da test etmiş oluruz.
- **Önerilen düzeltme:**
  - `CompactProcess`'i enjekte edilebilir kıl: `CompressionService`'e `Func<string, string, Task<(int, string, string)>>? runner` seam'i (üretim: gerçek `CompactProcess`).
  - Regresyon testleri: adım-2 compact hatası senaryosu (`exitCode!=0` + `[ERR]` çıktısı → `Status="Failed"`), adım-1 non-fatal davranışı (adım-1 hata verse bile adım-2 başarılıysa başarı), WOF dosyada adım-1 başarısız + adım-2 başarılı → başarı.
  - Doküman 1.2 metni: kontrolün fiilî durumunu yaz ("adım-1 bilinçli non-fatal: gerekçe X; adım-2 `exitCode != 0 || [ERR]` kontrolü eklendi; hata simülasyon testleri CompactProcess seam'i ile eklendi").

## 🟡 MAJOR

### [K2] Boş tarama sonucu her zaman exception fırlatıyor `[codebase-details]`
- **Kapsam:** `MainViewModel.Scan.cs:321-339` (`ReorderFilesBySizeDescending`), çağrı: `:192`
- **Açıklama:** `ordered[0]` boş listede `ArgumentOutOfRangeException` fırlatır. Hiç dosya eşleşmeyen tarama (boş klasör, <4KB, filtreli) deterministik olarak `ExecuteScanAsync`'in jenerik catch'ine düşer; kullanıcı "Tarama hatası: Index was outside…" görür, tamamlanma durumu kurulmaz. Testler hep ≥1 dosyalı olduğundan gizli.
- **AI Önerisi:** **PROPOSE FIX**
- **Önerilen düzeltme:** `if (Files.Count == 0) return;` guard + boş-sonuç taraması regresyon testi.

### [K3] "Kalıcı Language ayarı" fiilen kalıcı değil `[codebase-details]`
- **Kapsam:** Doküman satır 216-217 • `MainViewModel.Settings.cs:57-69` (`CaptureSettings`) • `MainWindow.xaml.cs:94-115`
- **Açıklama:** `CaptureSettings()` `Language` alanını taşımıyor → kapanışta kaydedilen ayarlar dosyadaki mevcut `Language` değerini **her kapanışta "tr" ile ezer**. Dil ayarı hiçbir oturumda gidiş-dönüş yapmaz.
- **AI Önerisi:** **PROPOSE FIX**
- **Önerilen düzeltme:** `CaptureSettings`'i mevcut ayarların `Language` değerini koruyacak şekilde güncelle (kaynak: `App.ApplyLanguage`'in uyguladığı değer) + gidiş-dönüş regresyon testi.

### [K4] Toplu (de)sıkıştırmada dosya-başına O(n) rescan → O(n²) UI kilidi `[codebase-details]`
- **Kapsam:** `MainViewModel.Compression.cs:53-67`, `:154-168` • doküman satır 68-70/155-160
- **Açıklama:** Servis dosya başına rapor basar; VM callback'i her raporda `TotalBytesSaved = Files.Sum(...)` + `UpdateSelectionMetrics()` tam döngüsü (≈5 O(n) geçiş/dosya). 100k seçili dosyada UI thread'de donma — Faz 3.4'ün taramada çözdüğü sorun sıkıştırmada geri geliyor.
- **AI Önerisi:** **PROPOSE FIX**
- **Önerilen düzeltme:** Progress callback'inde tam rescan yerine artımlı/işlenmiş-sayı bazlı güncelleme (toplamı `report.TotalBytesSaved`'dan al, `Files.Sum`'ı yalnızca işlem sonunda bir kez yap); simülasyonda aynı desen.

### [K5] Faz 5.4 geri alma ağında üç güvenlik boşluğu `[implementation-readiness + codebase-details]`
- **Kapsam:** `MainViewModel.Compression.cs:75, 93-96` • `MainViewModel.Automation.cs:195-196` • doküman satır 221-227
- **Açıklama:** (a) **İptalde kayıt yok:** geçmiş yalnız başarı yolunda yazılıyor; iptalde diske sıkıştırılmış dosyalar hiçbir geri alma kaydı olmadan kalıyor. (b) **Başarısızlar da geçmişe:** `BuildHistoryEntries` tüm seçili dosyaları alıyor; tek başarılı yeter (SuccessfulCount>0) — sıkıştırılamayan dosyalar da kayda girip revert'te "başarılı" görünür. (c) **Kısmi revert'te batch silinir:** `RemoveBatch` koşulsuz; geri alınamayan dosyaların son kurtarma kaydı da düşer.
- **AI Önerisi:** **PROPOSE FIX**
- **Önerilen düzeltme:**
  - Kaydı işlem-öncesi yaz, iptalde/başarısızda **yalnız durum suntmayanı** filtreleyip başarılı/kısmi kaydı düşür.
  - Daha basit güvenli form: taşıma öncesi kaydı oluştur; `CompressionFilesAsync` sonrası `Status=="Compressed"` olmayan girdileri kayıttan çıkar; iptal dalında bugüne kadarki başarıları işle.
  - Revert'te `RemoveBatch`'i yalnız `FailedCount == 0`'da çağır; aksi hâlde başarılı girdileri düşür, başarısızları batch'te bırak.
  - Regresyon testleri: kısmi başarılı sıkıştırma geçmişi, kısmi başarısız revert'te batch'in korunması.

### [D1] Başlık anlık görüntüsü gövdeyle çelişiyor (49 → 59 → ? test) `[structural + style + comprehension]`
- **Kapsam:** Doküman satır 3, 115, 178, 238
- **Açıklama:** "Mevcut durum" etiketi tarihî anlık görüntü olduğunu belirtmiyor; okuyucu güncel test sayısını belirleyemiyor.
- **AI Önerisi:** **PROPOSE FIX**
- **Önerilen düzeltme:** Satır 3'ü "Orijinal analiz anlık görüntüsü (840a2d7, 49/49) — **güncel durum: 5/5 faz tamamlandı, 116/116 test yeşil**" olarak güncelle; satır 115 tarihî not olarak kalsın ("Faz 1 sonrası 59/59").

### [D2] Bölüm 3 "Önerilen Başlangıç" bayat `[structural + style]`
- **Kapsam:** Doküman satır 236-243 (+ satır 96 "(önerilen ilk adım)")
- **Açıklama:** Tüm fazlar bitmişken "Faz 1 ile başlayın" önerisi ve "mevcut 49 test" referansı geçersiz.
- **AI Önerisi:** **PROPOSE FIX**
- **Önerilen düzeltme:** Bölümü "Durum ve Kalan İşler"e çevir: 5/5 faz + 116/116 test; kalan opsiyonel işler (i18n pilot kapsamın kalan görsel metinleri, MSIX/winget ertelemesi, imza adımı). Faz 1 başlığındaki "(önerilen ilk adım)" nitelemesini düşür.

### [D3] Faz 1.2–5 öznel/abartı iddiaların kalibrasyonu `[style]`
- **Kapsam:** Satır 2.1 ("ampirik olarak çürütüldü" — tek makine gözlemi), 3.7 ("kanıtlandı" — tek makine, tarih yok), 4.3 ("61,9 MB" — SDK bağımlı nokta ölçüm), 5.2 ("canlı doğrulandı" — ortam/tarih yok), 1.2 ("Güçlü Yönler" öznel sıfatları), 4.4 ("değeri yoktur" — fazla geniş), 4.2 ("asla bayatlamaz"), 5.5 ("gerçekten"), 2.2 ("Bonus"), 2.1 ("hızlı")
- **AI Önerisi:** **PROPOSE FIX** (calibrate)
- **Önerilen düzeltme:** Her iddiayı gözlem kapsamına daralt: "bu makinede, 2026-11-25'te gözlemlendi"; ölçümlere tarih ekle; öznel sıfatları (temiz/sağlam/doğru/kullanıcı dostu) ölçülebilir ifadelere indir; absolütleri ("asla", "hiç") mekanizma ifadesiyle değiştir; "Bonus" ve "hızlı" gibi dolguları düşür.

### [D4] Faz kapanış notu yerleşimi tutarsız `[structural]`
- **Kapsam:** Satır 113-115/138-140 (blockquote not) vs 164-166/192-194/225-227 (gömülü ders notları)
- **Açıklama:** Faz 1-2 ayrı `>` not bloğu ile kapanırken Faz 3-5'inkiler madde gövdesine gömülü; ders taraması için tek tip yok.
- **AI Önerisi:** **PROPOSE FIX** (hafif)
- **Önerilen düzeltme:** Faz 3-5'e de kısa `> **Faz N notları:**` bloğu ekle; kritik dersleri oraya taşı (madde gövdesini sadeleştir). Yerleşim tek tipe oturur.

## 🟢 NİT — Kod

### [K6] Simülasyon progress'i doğrudan worker thread'den yazıyor `[implementation-readiness]`
- **Kapsam:** `MainViewModel.Compression.cs:267-270` — **PROPOSE FIX** (progress alanlarını `Progress<T>` üzerinden geçir).

### [K7] `GetDiskAllocatedSize` başarılı çağrıda stale last-error kontrolü `[codebase-details]`
- **Kapsam:** `Kernel32.cs:127-145` — **PROPOSE FIX** (`Marshal.GetLastWin32Error()`'u yalnız başarısızlık dalında çağır).

### [K8] `ScheduledTaskService`: pipe sıralaması + `WaitForExit(timeout)` sonrası `ExitCode` riski + UI thread'de senkron donma `[codebase-details + implementation-readiness]`
- **Kapsam:** `ScheduledTaskService.cs:63-81` — **PROPOSE FIX** (stdout/stderr'i asenkri oku, `WaitForExit()`'ten sonra `ExitCode`'a güven; çağrıyı UI thread'inde bloklamaktan kaçınmak için kısa süreç için mevcut senkronluk kabul edilebilir — yalnız okuma sırası ve ExitCode güvenliği düzeltilir).

### [K9] `CompactProcess` iptalinde gözlemlenmeyen task exception'ları `[codebase-details]`
- **Kapsam:** `CompactProcess.cs:40-58` — **PROPOSE FIX** (task'leri gözlemle/sessizce yut).

### [K10] Revert'te 0 baytlık mevcut dosya "File not found" oluyor `[codebase-details]`
- **Kapsam:** `CompressionRevertService.cs:42-58` — **PROPOSE FIX** (`File.Exists` ile ayır).

## 🟢 NİT — Doküman

### [D5] Faz 3.8 partial envanterinde 7. dosya (`.Automation`) eksik `[codebase-details + implementation-readiness]`
- Doküman satır 175-178 — **PROPOSE FIX** (listeye `.Automation` (5.1/5.4/5.5 içerikleri) ekle).

### [D6] Faz 3.2 kolon adı "Tahsis" ↔ XAML başlığı "Diskteki Boyut" `[implementation-readiness]`
- Doküman satır 148-151 — **PROPOSE FIX** (dokümandaki kolon adını fiili UI başlığıyla hizala).

### [D7] Tanımsız kavramlar: WOF, "seam", `Pending` dry-run çarpan listesi, 8.3 kapalı birim cümlesi `[comprehension]`
- Doküman satır 20-21, 28-29, 129-130, 133-136, 170-174, 205-213 — **PROPOSE FIX** (tek cümlelik tanımlar/tam çarpan listesi ekle).

### [D8] Faz başlığında süresi bitmiş nitelemeler "(önerilen ilk adım)"; Faz 5 süre formatı ayrışıyor `[structural]`
- Doküman satır 96, 202 — **PROPOSE FIX** (tamamlanmış fazlarda niteleme düşür; format kalıbı `~N gün` kalıbına yaklaştır).

### [D9] 1.3 sorun numaralandırması bloklar arası akıyor `[structural]` — **DECLINE** (sürekli numara doküman içinde sorunsuz izlenebilir; yeniden numaralandırma churn).

### [D10] Standardın per-faz write-allowlist bölümü yok `[structural]` — **DECLINE** (tamamlanmış plan için retrospektif katma değeri düşük).

### [D11] Kalın vurgu yoğunluğu `[style]` — **DECLINE** (UI etiketleri zaten KEEP; vurgu azaltma churn getireceği kadar kazanç sağlamıyor; D3'teki kritik maddeler ayrıca ele alınıyor).

### [D12] Faz 5.1→5.2 ileriye referans sırası `[comprehension]` — **DECLINE** (aynı bölüm bloğunda; metin kendi kendini 2 madde içinde tamamlıyor).

---

## Sayılar ve Durum

| Kategori | Adet | Sonuç |
|---|---|---|
| 🔴 Blocker | 1 (K1) | Uygulandı (seam + 5 test + doküman 1.2) |
| 🟡 Major — kod | 4 (K2–K5) | Uygulandı, regresyon testli |
| 🟡 Major — doküman | 4 (D1–D4) | Uygulandı |
| 🟢 Nit — kod | 5 (K6–K10) | Uygulandı |
| 🟢 Nit — doküman | 4 (D5–D8) | Uygulandı |
| 🟢 Nit — reddedilen | 4 (D9–D12) | DECLINE — kayıt ama düzeltme yok |

**Toplam:** 22 bulgu • 18 PROPOSE FIX uygulandı • 4 DECLINE • 0 CONTEST • 0 ESCALATE
