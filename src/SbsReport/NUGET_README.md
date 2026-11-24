# SbsReport

Sıfır harici bağımlılıklı, saf C# **.NET 10** kurumsal raporlama motoru ve gömülü **Web Designer IDE**.  
Chromium, SkiaSharp, GDI+ veya ticari lisans DLL'leri olmadan **100.000 satırı 5.2 saniyede** PDF, gerçek OOXML Excel (`.xlsx`) ve Word (`.docx`) belgelerine dönüştürür.

- **GitHub:** https://github.com/iventhosting/SbsReport
- **Dokümantasyon:** https://github.com/iventhosting/SbsReport/tree/main/docs
- **Sürüm Geçmişi:** https://github.com/iventhosting/SbsReport/blob/main/CHANGELOG.md
- **Lisans:** MIT

---

## v1.0.10 & v1.0.9 Yenilikleri

- **NuGet & Visual Studio İyileştirmeleri:** Saf Markdown NuGet README ve Sobis paket ikonu eklendi.
- **Gerçek OOXML Word (`.docx`) & Gelişmiş Excel (`.xlsx`):** Saf C# `ZipArchive` ile uyarı vermeden açılan `.docx` ihracı; Excel'de gerçek sayısal hücre (`#,##0.00`), tarih/kimlik numarası koruması ve otomatik sütun genişliği.
- **Yeni Motor & Bileşen Özellikleri:** `GrowToHeight`, `ShrinkFontToFit`, `ProcessingDuplicates` (`Hide` / `Merge`), `PrintOn`, `PrintAtBottom`, `ResetPageNumber`, `KeepGroupTogether`, `HideZeros`, `MaxNumberOfLines`, `ImageFit`, `EmptyBand`, `OverlayBand`, çok sütunlu `DataBand`, **`TextInCells`** (TC/IBAN kutucukları), **Hesaplanan Sütunlar** ve XSS korumalı **`AllowHtmlTags`** (`Text` ve çok satırlı `Input` desteği).
- **Zengin İfade Motoru:** `ToCurrencyWordsTr`, `ToWordsTr`, `CountDistinct`, `SumDistinct`, `RunningSum`, `RunningCount`, `Median`, `First`, `Last`, `Left`, `Right`, `Substring`, `Replace`, `ProperCase`, `DateDiff`, `MonthName`, `AddDays`, `Switch`, `Choose`, `Ceiling`, `Floor`, `Pow`, `Sqrt` ve `PageNofM`, `IsFirstPage`, `IsLastPage`, `GroupLine`, `LineThrough` sistem değişkenleri.
- **Yenilenen Web Designer IDE:** Canlı **Rapor Denetleyici**, **Stil Tasarımcısı**, **Kenarlık Ayarları**, sabit boyutlu **Sayfa Yapısı** ve **İfade Düzenleyici**, kompakt 3 sekmeli Şeffaf destekli renk paleti (`Palet` / `Web` / `Özel`), canlı özellik arama/filtreleme ve alfabetik sıralama, 4 ayrı sağ tık menüsü (Obje/Bant/Sayfa/Sözlük), tıklayınca anında görünen 8 boyutlandırma tutamacı, `Shift+Ok` ızgara boyutlandırma / `Ctrl+Ok` taşıma ve tema uyumlu özel diyaloglar.

---

## Hızlı Başlangıç

### 1. Kurulum

```bash
dotnet add package SbsReport
```

### 2. ASP.NET Core `Program.cs` Entegrasyonu (2 Satır)

```csharp
using SbsReport;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSbsReport(options =>
{
    options.ReportsDirectory = Path.Combine(builder.Environment.ContentRootPath, "Reports");
    options.RoutePrefix = "/sbs-report";
    // Opsiyonel: Üretim ortamında yetkilendirme
    // options.RequireAuthorization = true;
    // options.AuthorizationPolicy = "ReportManagement";
});

var app = builder.Build();
app.UseSbsReport(); // Gömülü Web Designer IDE & Viewer hazır!
app.Run();
```

Tarayıcınızdan **`/sbs-report`** adresine gittiğinizde Web Designer IDE doğrudan açılır.

### 3. C# Kodundan Headless Rapor Üretimi (`DataSet` -> PDF, Excel, Word)

```csharp
using System.Data;
using SbsReport.Services;

// 1. Tek seferde ikili Snapshot + PDF üret (Zero-JSON DataSet hızlı yolu)
var (snapshotBytes, pdfBytes) = sbsEngine.RenderHeadless(
    reportTemplatePath: "Reports/personel_listesi.sbs",
    parameters: new Dictionary<string, object> { ["KurumAdi"] = "İl Sağlık Müdürlüğü" },
    dataSet: myDataSet);

// 2. Veritabanına tekrar gitmeden Snapshot'tan anında Excel (.xlsx), Word (.docx) veya HTML al
byte[] excelBytes = sbsEngine.ExportFromSnapshot(snapshotBytes, "xlsx");
byte[] wordBytes  = sbsEngine.ExportFromSnapshot(snapshotBytes, "docx");
byte[] htmlBytes  = sbsEngine.ExportFromSnapshot(snapshotBytes, "html");
```

---

## Performans Benchmark (`.NET 10 Release`)

| Senaryo | Cold-Start | Warm Baskı | Snapshot -> PDF | Snapshot -> Excel (`.xlsx`) | Snapshot Boyutu | PDF Boyutu |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: |
| **Tek Sayfa Form (1 Kayıt)** | ~120 ms | ~18 ms | ~10 ms | ~8 ms | 1.6 KB | 24.9 KB |
| **Orta/Büyük Liste (3.836 Satır / 226 Syf)** | 554 ms | 276 ms | 184 ms | 162 ms | 316 KB | 590.7 KB |
| **Ağır Stres Testi (100.000 Satır / 5.883 Syf)** | 5.20 sn | 5.15 sn | 3.95 sn | 2.09 sn | 7.98 MB | 14.04 MB |

---

## 12 Yerel Dışa Aktarma Formatı

| Format | Uzantı | Açıklama |
| :--- | :---: | :--- |
| **PDF 1.4** | `.pdf` | Saf C# `SbsPdfWriter`, TrueType `glyf/loca` font subsetting, tam Türkçe karakter ve arama desteği |
| **Excel** | `.xlsx` | Gerçek ECMA-376 OOXML ZIP paketi, sayısal hücreler, kimlik/tarih koruması, otomatik sütun genişliği |
| **Word** | `.docx` / `.doc` | Gerçek ECMA-376 OOXML Word (`.docx`) ve klasik `.doc` çıktısı |
| **HTML5** | `.html` | Birebir `mm` koordinatlı, güvenli inline HTML ve `@media print` destekli |
| **Snapshot** | `.sbsdoc` | `SBSS` ikili format + String Interning + GZip (28x daha küçük, byte-byte kayıpsız) |
| **CSV** | `.csv` | UTF-8 BOM + `;` ayraç (Türkçe Excel doğrudan bozulmadan açar) |
| **Diğerleri** | `.rtf` `.txt` `.xml` `.json` `.svg` | Entegrasyon, arşivleme ve vektör önizleme formatları |

---

## İfade Motoru Örnekleri

- **Veri Alanları:** `{Data.AD_SOYAD}` (Türkçe karakter ve büyük/küçük harf duyarsız)
- **Türkçe Yazıyla Tutar:** `{ToCurrencyWordsTr(Sum(Data.TUTAR))}` -> `Bin İki Yüz Elli TL Ellİ Kuruş`
- **Özet Fonksiyonları:** `Sum`, `Count`, `Avg`, `Min`, `Max`, `CountDistinct`, `SumDistinct`, `RunningSum`, `RunningCount`, `Median`, `First`, `Last`
- **Koşul & Formatlama:** `{IIF(Data.DURUM == 'E', 'Aktif', 'Pasif')}`, `{Switch(Data.TIP, 1, 'Memur', 2, 'Sözleşmeli', 'Diğer')}`
- **Sistem Değişkenleri:** `{PageNumber}`, `{TotalPageCount}`, `{PageNofM}`, `{Line}`, `{GroupLine}`, `{LineThrough}`, `{Today}`, `{Time}`, `{ReportName}`

---

## Dokümantasyon & Destek

- [Başlangıç Rehberi](https://github.com/iventhosting/SbsReport/blob/main/docs/getting-started.md)
- [Web Designer & Master Form Kullanımı](https://github.com/iventhosting/SbsReport/blob/main/docs/web-designer.md)
- [Headless Render & Snapshot Mimarisi](https://github.com/iventhosting/SbsReport/blob/main/docs/headless-rendering.md)
- [API & Endpoint Referansı](https://github.com/iventhosting/SbsReport/blob/main/docs/api-reference.md)
- [Değişiklik Günlüğü (CHANGELOG)](https://github.com/iventhosting/SbsReport/blob/main/CHANGELOG.md)
