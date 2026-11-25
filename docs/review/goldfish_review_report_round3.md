# Goldfish Annotated Review Report — Tur 3

**Status: resolved (auto accept — 15 PROPOSE FIX uygulandı, 1 DECLINE kayıtlı, 2 CONTEST doğrulaması — 0 hükümsüz) — 2026-11-25**
**Doğrulama:** `dotnet build` 0 uyarı (Release `-warnaserror` dahil), `dotnet test` 144/144 yeşil (136 + 8 yeni tur-3 regresyon testi)
**Target:** `docs/GELISTIRME-PLANI.md` (tam dosya, satır 1-296) + kod tabanı
**Tarih:** 2026-11-25 • **Tur:** 3 (taze denetçi örnekleri — temiz tur doğrulaması) • **Denetçiler:** codebase-details, implementation-readiness, comprehension, structural, style
**Önceki turlar:** tur 1 (18 PROPOSE FIX uygulandı, 4 DECLINE), tur 2 (16 PROPOSE FIX uygulandı, 3 DECLINE). 7 kayıtlı karar (D9–D12, T2-X1/X2/X3) denetçilere bildirildi; hiçbiri yeniden raporlanmadı.

## Tur 1 + Tur 2 Düzeltme Doğrulaması (4 denetçi bağımsız onayladı)

- **K1–K10 eksiksiz; T2-K1, K2, K3, K4, K6 eksiksiz ve işlev bozmayan** (K2 tamamlanma yolu, K3 tekil seçim, K4 normal revert kontrolleri yapıldı).
- Bölüm 3 kimlik-kilidi iddiaları **10/10 (tur 1) + 6/6 (tur 2) birebir doğru**; 136/136 statik sayım + kronoloji zinciri (49→59→93→116→127→136) tam tutarlı.
- Tur 2 doküman kalibrasyonları (üslup + yapı + kavram) tamamının oturduğu doğrulandı; absolüt tarama temiz.

## Konsolide Bulgular

### 🔄 CONTEST doğrulamasından geçenler (asılIMEZ tarafından kod okumayla teyit)

| # | Sev | Bulgu | Konum |
|---|---|---|---|
| **T3-B1** | 🔴 Blocker | **Üretimde ayar kalıcılığı yazma tarafı hiç çalışmıyor**: `MainWindow()` üretim ctor'u VM'e `JsonSettingsService` veriyor (yükleme çalışır) ama pencere konumu için kendi `_settingsService` alanı null kalıyor; `MainViewModel.PersistSettings()` hiçbir yerden çağrılmıyor. Sonuç: kapanışta hiçbir tercih (yol/algoritma/kategori/dil/pencere konumu) kaydedilmez, açılışta pencere konumu yüklenmez. Tur-1 K3 (dil ezilmesi) düzeltmesi üretimde etkisiz — ezilecek kayıt yoktur. Testler `vm.PersistSettings()`'i doğrudan çağırdığından wiring boşluğu test kapsam dışı kaldı. | `MainWindow.xaml.cs:19-30, 50, 109`, `MainViewModel.Settings.cs:89` |
| **T3-M1** | 🟡 Major | **T2-K5 kill dalı ölü kod**: akış Task'ları `stdoutTask.Result` ile senkron bekleniyor; asılı schtasks pipe'ları açık tuttuğundan `.Result` süresiz bloklar ve `WaitForExit(10_000)` hiç değerlendirilemez. Kill dalı, korumaya çalıştığı senaryoya erişemez; senkron UI çağrısı olduğundan asılma = GUI donması. | `ScheduledTaskService.cs:84-105` |

### Kod Nit'leri

| # | Sev | Bulgu | Kaynak |
|---|---|---|---|
| T3-N1 | 🟢 | İptal dalında batch'in yeniden sıkıştırdığı "önceden sıkışmış" dosyanın kurtarma kaydı düşmez (under-recording; veri kaybı yok, dosya sıkışık kalır) — T2-K2'nin bilinçli muhafazakar sadeleştirmesi, dipnot kaydı uygun | codebase-details N1 + impl-readiness N1 (örtüşüyor) |
| T3-N2 | 🟢 | CSV `EscapeCsv` `\r`/`\n` kaçışlamıyor — çok satırlı hata mesajları dışa aktarılan CSV'yi bozar | codebase-details |
| T3-N3 | 🟢 | "Dosya Konumunu Aç" eksik dosyada sessiz no-op ("Dosya bulunamadı" deseninden sapma) | codebase-details |
| T3-N4 | 🟢 | 2 kod yorumunda yazım bozukluğu (`MainViewModel.Scan.cs` satır 189, 237) | codebase-details |
| T3-N5 | 🟢 | T2-K6 düzeltmesi her iki serviste de uygulanmış ama test yalnız `JsonSettingsService`'i kilitliyor — Bölüm 3'e tek cümlelik kapsam notu | impl-readiness N3 |

### Doküman Nit'leri

| # | Sev | Bulgu | Kaynak |
|---|---|---|---|
| T3-D1 | 🟢 | Faz not bloğu kalıbı iki varyanta ayrılıyor (Faz 1-2 tanımlayıcı ekli, Faz 3-5 yalın) — tekleştirme yarım | structural |
| T3-D2 | 🟢 | Başlık bloğunda gün tahmini cümlesi "güncel durum" paragrafıyla aynı blokta — üçüncü `>` ayracı | structural |
| T3-D3 | 🟢 | "**Kalan opsiyonel işler:**" etiketi kalın değil (dikkat kalıbı tutarsızlığı) | structural |
| T3-D4 | 🟢 | Kronoloji 116/127 ara sayıları faz gövdelerinde yok — "+9 test (127→136)" tarzı ara not | comprehension |
| T3-D5 | 🟢 | Üslup: satır 53 "zaten 0 olmalı", satır 7 "tahminleri…tahminleridir", 3.4 "chunk/batch" terim ayrımı, Faz 5 "yazılır/kayda geçer", satır 238 "bayt/kayıt" birim belirsizliği, satır 266 "tamamı…tamamlandı" yankısı | style (6 küçük kalibrasyon) |

### DECLINE önerileri

| # | Bulgu | Gerekçe |
|---|---|---|
| T3-X1 | Satır 266 "Beş fazın tamamı tamamlandı" — içerik doğru, ses yankısı estetik | Bilgi doğru; dokunuş opsiyonel (style da "tercihen" dedi) |

## Sayılar

| Kategori | Adet |
|---|---|
| 🔴 Blocker | 1 (T3-B1 — CONTEST doğrulandı) |
| 🟡 Major | 1 (T3-M1 — CONTEST doğrulandı) |
| 🟢 Nit (kod) | 5 (T3-N1…N5) |
| 🟢 Nit (doküman) | 9 (T3-D1…D5 grubu) |
| CONTEST | 2 bulgu incelendi, 0 reddedildi — **ikisi de DOĞRULANDI** |
| DECLINE önerisi | 1 (T3-X1) |

**PROPOSE FIX toplamı:** 15 (T3-B1, T3-M1, T3-N1…N5, T3-D1…D5) • 0 dokunulmaz nit — hepsi tek geçişte kapatılabilir büyüklükte
**Temiz tur durumu:** SAĞLANAMADI — tur 4 gerekmez ama B1/M1 düzeltilmeden döngü kapatılamaz.
