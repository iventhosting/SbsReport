# SbsReport

[![NuGet Version](https://img.shields.io/nuget/v/SbsReport?style=flat-square&logo=nuget)](https://www.nuget.org/packages/SbsReport)
[![NuGet Downloads](https://img.shields.io/nuget/dt/SbsReport?style=flat-square&logo=nuget)](https://www.nuget.org/packages/SbsReport)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg?style=flat-square)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Zero Dependencies](https://img.shields.io/badge/Dependencies-Zero-brightgreen?style=flat-square)](#)

**SbsReport**, .NET 10 ve ASP.NET Core uygulamaları için geliştirilmiş; **sıfır harici bağımlılıklı**, **açık kaynak (MIT)**, yüksek performanslı web raporlama motoru, **gömülü Web Designer IDE** ve **headless PDF/Excel/Word üretim** kütüphanesidir.

Harici ücretli lisans veya üçüncü parti native kütüphane gerektirmeden; tek bir NuGet paketiyle projenize profesyonel bir sürükle-bırak rapor tasarımcısı (`.sbs` formatı) ve saf C# PDF 1.4 baskı motoru kazandırır.

---

## İçindekiler

- [Öne Çıkan Özellikler](#-öne-çıkan-özellikler)
- [Adım 1: NuGet Kurulumu](#-adım-1-nuget-kurulumu)
- [Adım 2: ASP.NET Core Entegrasyonu](#-adım-2-aspnet-core-entegrasyonu)
- [Adım 3: Web Designer IDE & Viewer Kullanımı](#-adım-3-web-designer-ide--viewer-kullanımı)
- [Adım 4: C# Kodundan Rapor Basma (PDF, Excel, Word ve Diğerleri)](#-adım-4-c-kodundan-rapor-basma-pdf-excel-word-ve-diğerleri)
- [Desteklenen Dışa Aktarma Formatları](#-desteklenen-dışa-aktarma-formatları)
- [İfade Motoru ve Sistem Değişkenleri](#-i̇fade-motoru-ve-sistem-değişkenleri)
- [Detaylı Dokümantasyon](#-detaylı-dokümantasyon)
- [Kısayol Tuşları](#-kısayol-tuşları)
- [Katkıda Bulunma ve Lisans](#-katkıda-bulunma-ve-lisans)

---

## Öne Çıkan Özellikler

- **Sıfır Harici Bağımlılık:** SkiaSharp, Chromium, wkhtmltopdf veya ücretli SDK gerektirmez; saf C# (.NET 10) ile çalışır.
- **Saf C# Streaming PDF 1.4 Motoru (`SbsPdfWriter`):**
  - Gerçek TrueType font alt kümeleme (`glyf`/`loca` subsetting) ile ultra küçük PDF çıktıları.
  - Tam Türkçe karakter desteği (`İ, ı, Ş, ş, Ğ, ğ, Ü, ü, Ö, ö, Ç, ç, ₺`) — 1-byte Windows-1254 hızlı yolu ve otomatik 2-byte Unicode `CIDFontType2` yedeği.
  - `ArrayPool<byte>` + `ZLibStream` ile düşük bellek tüketimi.
- **Gömülü 3 Bölmeli Web Designer IDE & Viewer:**
  - Tek satır `app.UseSbsReport()` ile ekstra statik dosya ayarı yapmadan yayına alınır.
  - 16 bileşen (Text, Input, CheckBox, Radio, ComboBox, DatePicker, Signature, Button, Image, Shape, Line, Barcode/QR, Chart, Table/CrossTab, Panel), cetveller, akıllı hizalama çizgileri, özellik denetçisi, form ağacı ve veri sözlüğü.
  - Açık / Koyu tema ve canlı Türkçe (TR) / İngilizce (EN) dil geçişi.
- **11 Dışa Aktarma Formatı & İkili Snapshot (`.sbsdoc`):**
  - Raporu veri tabanından bir kez üretip snapshot olarak saklayın; SQL'i tekrar çalıştırmadan anında PDF, Excel, Word, HTML, CSV, RTF, TXT, XML, JSON veya SVG çıktısı alın.
- **Master Form Desteği:** Kurumsal üstbilgi ve altbilgileri tek bir Master şablonda tanımlayıp tüm raporlara otomatik miras aktarın.
- **Türkçe Duyarsız Veri Eşleştirme:** `{Data.Ünvan}` ifadesi `DataTable` içindeki `UNVAN` kolonuyla otomatik eşleşir.

---

## Adım 1: NuGet Kurulumu

### .NET CLI
```bash
dotnet add package SbsReport
```

### Visual Studio Package Manager Console
```powershell
Install-Package SbsReport
```

### `.csproj` Dosyasına Doğrudan Ekleme
```xml
<ItemGroup>
  <PackageReference Include="SbsReport" Version="1.0.7" />
</ItemGroup>
```

> **Gereksinim:** `.NET 10.0` (`net10.0`)

---

## Adım 2: ASP.NET Core Entegrasyonu

`Program.cs` dosyanıza `AddSbsReport()` ve `UseSbsReport()` çağrılarını ekleyin:

```csharp
using SbsReport;

var builder = WebApplication.CreateBuilder(args);

// 1. SbsReport servislerini kaydet
builder.Services.AddSbsReport(options =>
{
    // .sbs şablon dosyalarının saklanacağı klasör (varsayılan: ContentRoot/Reports)
    options.ReportsDirectory = Path.Combine(builder.Environment.ContentRootPath, "Reports");

    // Web Designer ve Viewer URL öneki (varsayılan: /sbs-report)
    options.RoutePrefix = "/sbs-report";

    // Opsiyonel: Canlı ortamda ASP.NET Core Authorization ile koruma
    // options.RequireAuthorization = true;
    // options.AuthorizationPolicy = "RaporTasarimYetkisi";
});

var app = builder.Build();

// Yetkilendirme kullanıyorsanız UseSbsReport'tan önce ekleyin
// app.UseAuthentication();
// app.UseAuthorization();

// 2. Gömülü Web Designer IDE, Viewer ve REST API uçlarını aktif et
app.UseSbsReport();

app.Run();
```

### Yapılandırma Seçenekleri (`SbsReportOptions`)

| Özellik | Varsayılan | Açıklama |
| --- | --- | --- |
| `ReportsDirectory` | `ContentRoot/Reports` | `.sbs` rapor ve Master Form dosyalarının okunup kaydedileceği dizin. |
| `RoutePrefix` | `"/sbs-report"` | Web Designer UI ve `/api/...` uçlarının yayınlanacağı adres öneki. |
| `RequireAuthorization` | `false` | `true` yapıldığında tüm tasarımcı ve API uçları ASP.NET Core Authorization ile korunur. |
| `AuthorizationPolicy` | `null` | `RequireAuthorization = true` iken uygulanacak özel Authorization Policy adı. |

---

## Adım 3: Web Designer IDE & Viewer Kullanımı

Uygulamanızı çalıştırdıktan sonra tarayıcınızdan şu adrese gidin:

```text
https://localhost:<PORT>/sbs-report/
```

Belirli bir raporu, temayı veya dili doğrudan URL parametreleriyle açabilirsiniz:

```text
https://localhost:<PORT>/sbs-report/?report=SatisFaturasi&theme=dark&lang=tr
```

Web Designer içinde:
1. **Yeni şablon oluşturun** veya hazır şablon galerisinden başlayın.
2. Sağ paneldeki **Sözlük (Dictionary)** sekmesinden veri kaynağınızı (örn. `Data`) ve kolonlarınızı tanımlayın.
3. Sol araç çubuğundan bileşenleri sürükleyip tuvale bırakın ve `{Data.KOLON_ADI}` ifadelerini yazın.
4. **Önizleme / PDF Önizleme** sekmesinde anlık çıktıyı görün, **Kaydet (`Ctrl + S`)** ile `.sbs` dosyasını `ReportsDirectory` klasörüne kaydedin.

---

## Adım 4: C# Kodundan Rapor Basma (PDF, Excel, Word ve Diğerleri)

Kaydettiğiniz `.sbs` şablonunu sunucu tarafında (`WebAPI`, `MVC`, `Blazor`, `BackgroundService` veya `Console`) verilerinizle doldurup PDF veya başka bir formatta basmak için **`SbsReportEngine`** servisini kullanın.

### ASP.NET Core Controller Örneği

```csharp
using System.Data;
using Microsoft.AspNetCore.Mvc;
using SbsReport.Services;

[ApiController]
[Route("api/raporlar")]
public class RaporController : ControllerBase
{
    private readonly SbsReportEngine _reportEngine;
    private readonly IWebHostEnvironment _env;

    public RaporController(SbsReportEngine reportEngine, IWebHostEnvironment env)
    {
        _reportEngine = reportEngine;
        _env = env;
    }

    [HttpGet("personel-listesi")]
    public IActionResult PersonelListesiBas([FromQuery] string format = "pdf")
    {
        // 1. Veriyi DataTable / DataSet olarak hazırlayın (SQL, EF Core, Dapper vb.)
        var dt = new DataTable("Data");
        dt.Columns.Add("TCKIMLIKNO", typeof(string));
        dt.Columns.Add("AD_SOYAD", typeof(string));
        dt.Columns.Add("UNVAN", typeof(string));
        dt.Columns.Add("BIRIM", typeof(string));

        dt.Rows.Add("12345678901", "Ayşe Yılmaz", "Uzman Tabip", "Kardiyoloji Kliniği");
        dt.Rows.Add("23456789012", "Mehmet Şahin", "Hemşire", "Dahiliye Servisi");

        var dataSet = new DataSet();
        dataSet.Tables.Add(dt);

        // 2. Rapor parametrelerini tanımlayın (şablonda {KurumAd}, {TarihAraligi} olarak kullanılır)
        var parameters = new Dictionary<string, object>
        {
            ["KurumAd"] = "Örnek Sağlık Kurumu",
            ["TarihAraligi"] = "01.11.2026 - 22.11.2026"
        };

        // 3. Şablonu render edin -> Snapshot (.sbsdoc) ve PDF byte[] anında döner
        var templatePath = Path.Combine(_env.ContentRootPath, "Reports", "PersonelListesi.sbs");
        var (snapshotBytes, pdfBytes) = _reportEngine.RenderHeadless(templatePath, parameters, dataSet);

        if (format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
            return File(pdfBytes, "application/pdf", "PersonelListesi.pdf");

        // 4. Aynı snapshot üzerinden veriyi tekrar çekmeden başka formata dönüştürün
        var fileBytes = _reportEngine.ExportFromSnapshot(snapshotBytes, format);
        var (contentType, ext) = format.ToLowerInvariant() switch
        {
            "xlsx" or "xls" or "excel" => ("application/vnd.ms-excel", ".xls"),
            "docx" or "doc" or "word"  => ("application/msword", ".doc"),
            "html"                     => ("text/html; charset=utf-8", ".html"),
            "csv"                      => ("text/csv; charset=utf-8", ".csv"),
            "rtf"                      => ("application/rtf", ".rtf"),
            "txt"                      => ("text/plain; charset=utf-8", ".txt"),
            "xml"                      => ("application/xml; charset=utf-8", ".xml"),
            "json"                     => ("application/json; charset=utf-8", ".json"),
            "svg"                      => ("image/svg+xml; charset=utf-8", ".svg"),
            "sbsdoc" or "snapshot"     => ("application/octet-stream", ".sbsdoc"),
            _                          => ("application/pdf", ".pdf")
        };

        return File(fileBytes, contentType, "PersonelListesi" + ext);
    }
}
```

### Konsol veya Arka Plan Servisinde (DI Olmadan) Kullanım

```csharp
using System.Data;
using SbsReport.Services;

var engine = new SbsReportEngine(
    new ReportRenderEngine(new DemoDataService()),
    new ReportExportService());

var dataSet = new DataSet();
var table = new DataTable("Data");
table.Columns.Add("AD_SOYAD");
table.Rows.Add("Şeyma Çelik Öğüt");
dataSet.Tables.Add(table);

var (snapshot, pdfBytes) = engine.RenderHeadless("Reports/IzinFormu.sbs", null, dataSet);
File.WriteAllBytes("IzinFormu.pdf", pdfBytes);

// Aynı snapshot'tan Excel üret
var excelBytes = engine.ExportFromSnapshot(snapshot, "xlsx");
File.WriteAllBytes("IzinFormu.xls", excelBytes);
```

---

## Desteklenen Dışa Aktarma Formatları

`ExportFromSnapshot(snapshotBytes, format)` ve `POST /sbs-report/api/reports/export/{format}` üzerinden desteklenen formatlar:

| Format Anahtarı | Çıktı Uzantısı | MIME Türü |
| --- | --- | --- |
| `pdf` | `.pdf` | `application/pdf` |
| `xlsx`, `xls`, `excel` | `.xls` | `application/vnd.ms-excel` |
| `docx`, `doc`, `word` | `.doc` | `application/msword` |
| `html`, `htm` | `.html` | `text/html; charset=utf-8` |
| `csv` | `.csv` | `text/csv; charset=utf-8` |
| `rtf` | `.rtf` | `application/rtf` |
| `txt` | `.txt` | `text/plain; charset=utf-8` |
| `xml` | `.xml` | `application/xml; charset=utf-8` |
| `json` | `.json` | `application/json; charset=utf-8` |
| `svg` | `.svg` | `image/svg+xml; charset=utf-8` |
| `sbsdoc`, `snapshot` | `.sbsdoc` | `application/octet-stream` |

---

## İfade Motoru ve Sistem Değişkenleri

Şablon metinlerinde `{...}` içinde önbellekli AST ifade motoru kullanılır:

- **Sistem Değişkenleri:** `{PageNumber}`, `{TotalPageCount}`, `{Line}`, `{Today}`, `{Now}`, `{ReportName}`
- **Parametreler & Kolonlar:** `{KurumAd}`, `{Data.AD_SOYAD}` (Türkçe karakter ve büyük/küçük harf duyarsız)
- **Toplama Fonksiyonları:** `Sum(...)`, `Avg(...)`, `Min(...)`, `Max(...)`, `Count()`
- **Yardımcı Fonksiyonlar:** `Iif(kosul, dogru, yanlis)`, `Round(sayi, basamak)`, `Abs(sayi)`, `Upper(metin)`, `Lower(metin)`, `Coalesce(a, b, ...)`, `Format(deger, "N2")`
- **Operatörler:** `+`, `-`, `*`, `/`, `%`, `==`, `!=`, `<`, `<=`, `>`, `>=`, `&&`, `||`

---

## Detaylı Dokümantasyon

GitHub deposundaki adım adım kılavuzlar:

- [Kurulum ve İlk Rapor Kılavuzu](https://github.com/iventhosting/SbsReport/blob/main/docs/getting-started.md)
- [C# Kodundan Rapor Basma, Snapshot ve Performans](https://github.com/iventhosting/SbsReport/blob/main/docs/headless-rendering.md)
- [Web Designer IDE, Master Form ve Şablon Rehberi](https://github.com/iventhosting/SbsReport/blob/main/docs/web-designer.md)
- [API ve REST Endpoint Referansı](https://github.com/iventhosting/SbsReport/blob/main/docs/api-reference.md)
- [Değişiklik Günlüğü (CHANGELOG)](https://github.com/iventhosting/SbsReport/blob/main/CHANGELOG.md)

---

## Kısayol Tuşları

| Kısayol | İşlev |
| --- | --- |
| `Ctrl + S` | Raporu Kaydet |
| `Ctrl + Z` / `Ctrl + Y` | Geri Al / Yinele |
| `Ctrl + C` / `Ctrl + V` | Kopyala / Yapıştır |
| `Ctrl + D` | Seçili Bileşenleri Çoğalt |
| `Delete` / `Backspace` | Seçili Bileşenleri Sil |
| `Ok Tuşları` / `Shift + Ok` | 1 mm / 5 mm Hassas Hizalama |
| `Ctrl + A` | Band İçindeki Tüm Bileşenleri Seç |

---

## Katkıda Bulunma ve Lisans

Katkıda bulunmak için [CONTRIBUTING.md](https://github.com/iventhosting/SbsReport/blob/main/CONTRIBUTING.md) dosyasına göz atabilirsiniz.

Bu proje [MIT Lisansı](https://github.com/iventhosting/SbsReport/blob/main/LICENSE) ile açık kaynak olarak lisanslanmıştır.
