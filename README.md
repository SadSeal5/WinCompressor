# ⚡ WinCompressor

**Windows Yerel Sıkıştırma Yöneticisi** — dosyalarınızı ZIP'e dönüştürmeden, yerinde bırakarak diskte kapladıkları alanı küçültür. Windows'un şeffaf dosya sistemi sıkıştırmasını (NTFS LZNT1 + WOF/CompactOS XPRESS-LZX) `compact.exe` ve `FSCTL` üzerinden yöneten WPF (.NET 10) masaüstü uygulaması.

> Dosyalar diskte küçülür; **her program onları normal şekilde açmaya devam eder**. Sıkıştırmayı kaldırdığınızda orijinal dosya birebir geri gelir.

---

## Özellikler

| Özellik | Ayrıntı |
|---|---|
| 🔍 **Akıllı tarama** | Klasör/ağaç taraması (reparse-point döngü korumalı), arama + kategori filtreleri, hızlı sürücü seçimi, kurulu oyun kütüphaneleri tespiti (Steam/Epic/EA/GOG/Xbox/Ubisoft) |
| 🧠 **Gerçek format tespiti** | `FSCTL_GET_EXTERNAL_BACKING` (0x90310) sorgusuyla her dosyanın diskteki gerçek algoritması tespit edilir (NTFS LZNT1 / XPRESS4K-8K-16K / LZX) |
| ⚡ **4 algoritma** | `ntfs` (yönetici gerekmez) + `xpress4k/8k/16k` ve `lzx` (CompactOS — yönetici gerektirir); tekil/çoklu dosya işlemleri, iptal destekli |
| 🔮 **Dry-run simülasyon** | Tahmini tasarruf sütunu — dosyalara dokunmadan kazanç önizlemesi |
| ⤺ **Geri alma güvenlik ağı** | Son batch'i eski algoritmalara döndürür; toplu/tekil decompress (iptal yolu dahil) geçmiş girdilerini otomatik düşürür |
| 📅 **Haftalık zamanlanmış görev** | Görev Zamanlayıcı'ya Pazartesi 03:00 sıkıştırma görevi kurar (tek EXE passthrough modu ile) |
| 🖥️ **CLI** | `WinCompressor.exe cli scan / compress / decompress` — betiklenebilir, JSON çıktı, `--dry-run`, admin gerektiren algoritmada hızlı hata |
| 📋 **İşlem günlüğü** | Tüm işlemler günlük çekmecesinde + CSV/TXT dışa aktarma |
| 🌐 **TR/EN arayüz** | Dinamik dil sözcükleri (85 anahtar × TR/EN); ayarlarda `"Language": "en"` → İngilizce |
| 🎨 **Koyu tema** | Modern koyu WPF arayüzü, canlı depolama metrikleri |

**Kalite:** 155/155 test yeşil • `dotnet build` 0 uyarı (Release `-warnaserror` dahil) • 5 inceleme turunda 12 Major + 2 Blocker kapatıldı (raporlar `docs/review/`)

---

## Gereksinimler

- **İşletim sistemi:** Windows 10 1809+ / Windows 11 (CompactOS için gerekli; NTFS sıkıştırma daha eski sürümlerde de çalışır)
- **Yönetici hakları:** *Gerekmez* — `NTFS (LZNT1)` standart kullanıcıyla çalışır. `XPRESS`/`LZX` (CompactOS) algoritmaları yönetici ister; uygulama tek tıkla kendini yükseltilmiş yeniden başlatabilir.
- .NET çalışma zamanı **gerekmez**: dağıtım EXE'si self-contained (tek dosya, her şey gömülü).

## Kurulum

### Yayın paketinden (önerilen)
1. [**Releases**](https://github.com/SadSeal5/WinCompressor/releases) sayfasından son sürümün `WinCompressor.exe` dosyasını indirin.
2. İndirdiğiniz dosyayı istediğiniz klasöre koyun — **kurulum gerekmez**, taşınabilir tek EXE.
3. Çift tıklayarak çalıştırın.
   - *İlk çalıştırmada SmartScreen uyarısı çıkarsa ("Windows bilinmeyen bir uygulamayı engelledi") → **Daha fazla bilgi** → **Yine de çalıştır**. EXE henüz kod imzalı değil (imzalama yol haritasında).*

### Kaynaktan derleme
.NET 10 SDK kurulu olmalı:

```powershell
git clone https://github.com/SadSeal5/WinCompressor.git
cd WinCompressor

# geliştirme derlemesi
dotnet build WinCompressor.sln
dotnet test tests/WinCompressor.Tests/WinCompressor.Tests.csproj

# tek dosyalık dağıtım EXE'si (self-contained, win-x64)
dotnet publish src/WinCompressor.UI/WinCompressor.UI.csproj -p:PublishProfile=win-x64-singlefile
# çıktı: src/WinCompressor.UI/bin/Release/net10.0-windows/win-x64/publish/WinCompressor.exe
```

---

## Kullanım

### Grafik arayüz

1. **Klasör seçin** — üstteki kutuya yazın, `📁 Gözat...` ile seçin veya `C: / D: / Kullanıcı Klasörü` hızlı düğmelerini kullanın (kuruluysa 🎮 oyun kütüphanesi çipleri de belirir).
2. **🔍 TARA** — dosya listesi boyuta göre akış halinde dolar; arama kutusu ve kategori çipleriyle (Programlar / Büyük >50MB / Metin & Kod / Veri & Oyun / CompactOS) filtreleyin.
3. **Algoritma seçin** — kartlardan birini seçin. `ÖNERİLEN` etiketi hedefinize en uygun algoritmayı işaret eder. XPRESS/LZX kartları yönetici modunda aktifleşir.
4. **İşlem yapın:**
   - `🔮 Simüle Et` — dosyalara dokunmadan tahmini tasarrufu hesaplar
   - `⚡ Seçilenleri Sıkıştır` — sıkıştırır
   - `📂 Sıkıştırmayı Kaldır` — orijinal haline döndürür
   - `⤺ Son İşlemi Geri Al` — son batch'i eski algoritmalarına döndürür
   - `✕ İptal` — işlem ortasında durdurur (kısmi sonuçlar geçmişe doğru kaydedilir)
   - `🗓 Haftalık Görev` — seçili klasör için Pazartesi 03:00 otomatik görev kurar
5. Sağ tık menüsüyle tekil dosyaları sıkıştırabilir/açabilir, konumunu açabilir, listeden çıkarabilirsiniz.

### Komut satırı (CLI)

```
WinCompressor.exe cli <komut> <yol> [seçenekler]

Komutlar:
  scan       <yol>                    tarama (--json ile makine okunur çıktı)
  compress   <yol> --algorithm <ad>   sıkıştırma (ntfs | lzx | xpress4k | xpress8k | xpress16k)
  decompress <yol>                    açma (yalnız sıkışmış dosyalar)

Seçenekler:  --no-subdirs  --include-compressed  --appdata  --min-size <bayt>  --json  --dry-run
Çıkış kodu:  0 = başarı, 1 = kullanım hatası, 2 = kısmi/tam hata
```

Örnekler:
```powershell
.\WinCompressor.exe cli scan "D:\Oyunlar" --min-size 1048576
.\WinCompressor.exe cli compress "D:\Oyunlar" --algorithm xpress4k --dry-run
.\WinCompressor.exe cli compress "C:\Kaynaklar" --algorithm ntfs
```

> CLI, GUI EXE'sinin içine gömülüdür — ayrı ikili dosya yok. Zamanlanmış görev de bu modu kullanır.

### Ayarlar ve veri

| Ne | Nerede |
|---|---|
| Ayarlar (yol, algoritma, dil, pencere konumu...) | `%AppData%\WinCompressor\settings.json` |
| Geri alma geçmişi | `%AppData%\WinCompressor\history.json` (kapasite 500 kayıt, atomik yazım) |

---

## Mimari

```
src/
├── WinCompressor.Core   # iş mantığı: tarama, sıkıştırma, tespit, Win32 interop, CLI — UI bağımlılığı yok
├── WinCompressor.Cli    # ayrı konsol başlatıcısı (opsiyonel; GUI EXE'si de cli moduna sahiptir)
└── WinCompressor.UI     # WPF/MVVM arayüz, yerelleştirme (Strings.tr|en.xaml)
tests/                   # xUnit — 155 test (49 analiz + fazlar + inceleme turu regresyonları)
docs/                    # geliştirme planı + inceleme raporları
```

## Yol haritası

Beş geliştirme fazı ve 5 kod inceleme turu tamamlandı. Kalan opsiyonel işler dış ön koşullara bağlı:
- **MSIX/winget paketleme** — kod imzalama sertifikası alındığında
- **Kod imzalama** — Azure Trusted Signing (veya OV/EV) hesabı açıldığında (CI'da imza adımı hazır, yorumlu)
