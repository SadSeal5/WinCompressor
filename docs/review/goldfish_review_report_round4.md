# Goldfish Annotated Review Report — Tur 4

**Status: resolved (auto accept — 1 Major + 9 Nit uygulandı: T4-M1 davranış düzeltmesi `RemoveEntries` + N1-N6 + 3 doküman nit; 4 yeni regresyon testi) — 2026-11-25**
**Doğrulama:** `dotnet build` 0 uyarı (Debug + Release `-warnaserror`), `dotnet test` 148/148 yeşil (Debug + Release; 144 + 4 yeni tur-4 testi)**
**Target:** `docs/GELISTIRME-PLANI.md` (tam dosya, satır 1-310) + kod tabanı
**Tarih:** 2026-11-25 • **Tur:** 4 (taze denetçi örnekleri — temiz tur doğrulaması) • **Denetçiler:** codebase-details, implementation-readiness, comprehension, structural, style
**Kayıtlı kararlar:** D9–D12 (tur 1), T2-X1/X2/X3 (tur 2), T3-X1 (tur 3, dokümanda kapatıldı) — hiçbiri yeniden raporlanmadı.

## Temiz Tur Doğrulaması

**4/5 denetçi TEMİZ döndü:**

| Denetçi | Sonuç |
|---|---|
| implementation-readiness | **"BULGU YOK (temiz tur)"** — 144 sayım dosya dosya birebir; tur-3 iddialarının tümü kod+test kanıtlı; tur 1-2-3 yarım uygulama yok; kod taramasında hata bulunamadı |
| structural | Temiz — 1 Nit; tur 3 yapısal düzeltmelerinin üçü birebir oturdu |
| style | Temiz — 1 Nit; tur 3 kalibrasyonlarının tamamı stabilize; absolüt tarama temiz |
| comprehension | Temiz — 2 Nit; kronoloji aritmetik + test envanteriyle birebir; belge-kod örtüşmesi tam |
| codebase-details | T3 doğrulamaları tam; **1 Major (CONTEST sonrası DOĞRULANDI) + 5 Nit** |

## Konsolide Bulgular

### 🔄 CONTEST doğrulaması (kod okumayla teyit)

| # | Sev | Bulgu | Konum |
|---|---|---|---|
| **T4-M1** | 🟡 Major | **Toplu "Sıkıştırmayı Kaldır" bayat geri-alma geçmişi bırakıyor**: decompress batch'i geçmiş servislerine hiç dokunmuyor; kullanıcı açtıktan sonra "⤺ Son İşlemi Geri Al" hâlâ aktif ve tıklanınca az önce açtığı dosyaları fark ettirmeden eski algoritmaya **yeniden sıkıştırıyor**. Dokümanda bu sınır kasıtlı tasarım olarak belirtilmiyor. Çözüm alternatifleri: (a) davranış — decompress batch'inde açılan dosyaları geçmiş girdilerinden düşür; (b) doküman — 5.4'e sınırı açıkça yaz | `MainViewModel.Compression.cs:182-266`, `MainViewModel.Automation.cs:139-153`; doküman satır 245-261 |

### Kod Nit'leri (codebase-details)

| # | Sev | Bulgu |
|---|---|---|
| T4-N1 | 🟢 | Pencere **maksimize durumda kapatılırsa** `RestoreBounds` yerine tam-ekran `Left/Width` kaydediliyor → sonraki oturumda "geri yükle" boyutu XAML varsayılanına düşer (`MainWindow.xaml.cs:129-133`) |
| T4-N2 | 🟢 | CLI'da admin gerektiren algoritma + non-admin için fast-fail yok; her dosya tek tek compact hatasıyla düşer (`CliDispatcher.cs:111-154`) |
| T4-N3 | 🟢 | Tekil dosya işlemlerinde İptal butonu görünür ama `_activeCts` yok → sessiz no-op (UX tutarsızlığı) |
| T4-N4 | 🟢 | Doküman "tek seferlik **yerinde** sıralama" diyor; uygulama `Files.Clear()` + yeniden Add yapıyor (tam rebuild) — ifade maliyet profilini yanıltıyor |
| T4-N5 | 🟢 | `AsyncRelayCommand.Execute` `async void` try/catch'siz — komut gövdeleri korumalı ama genel catch ucuz sigorta |

### Bakım/Doküman Nit'leri

| # | Sev | Bulgu | Kaynak |
|---|---|---|---|
| T4-N6 | 🟢 | `MainViewModel.Compression.cs:100` — T3-N1 düzenlemesinde `try` bloğu girinti hizası kaymış (derlemeyi etkilemez) | impl-readiness |
| T4-D1 | 🟢 | "under-recording" anglicism — "eksik kayıt" yeterli (satır ~300) | style |
| T4-D2 | 🟢 | "sıra.WaitForExit → Kill" kaynaşmış + backtiksiz — "beklenme sırası: `WaitForExit` → `Kill` → akış okuma" | structural + comprehension |
| T4-D3 | 🟢 | T2-K6 kapsam notunda ikinci servis (`JsonCompressionHistoryService`) adlandırılmamış | comprehension |

## Sayılar

| Kategori | Adet |
|---|---|
| 🔴 Blocker | **0** |
| 🟡 Major | **1 (T4-M1 — CONTEST doğrulandı)** |
| 🟢 Nit — kod | 5 (T4-N1…N5) |
| 🟢 Nit — bakım/doküman | 4 (T4-N6, T4-D1…D3) |
| CONTEST | 1 incelendi — DOĞRULANDI |

**Temiz tur durumu:** 4/5 denetçi temiz; yalnız codebase-details 1 Major buldu. Bulguların tur bazlı seyri: tur 1 (1 Blocker + 9 Major) → tur 2 (0 + 4) → tur 3 (1 + 1) → tur 4 (**0 + 1**) — yakınsıyor.
