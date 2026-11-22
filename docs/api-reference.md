# API ve REST Endpoint Referansı

Bu doküman, **SbsReport** paketinin C# servislerini, yapılandırma seçeneklerini ve gömülü REST API uçlarını özetler.

---

## 1. ASP.NET Core Kayıt ve Middleware

Namespace: `SbsReport`

### `AddSbsReport`
```csharp
builder.Services.AddSbsReport(Action<SbsReportOptions>? configure = null);
```
Aşağıdaki servisleri `Singleton` olarak DI konteynerine kaydeder:
- `SbsReportOptions`
- `DemoDataService`
- `ReportRenderEngine`
- `ReportExportService`
- `SbsReportEngine`
- `ReportStorageService`

### `UseSbsReport`
```csharp
app.UseSbsReport(string? routePrefix = null);
```
Statik dosyaları etkinleştirir ve `RoutePrefix` (varsayılan `/sbs-report`) altında gömülü UI dosyalarını ve REST API uçlarını yayınlar.

### `SbsReportOptions`
| Özellik | Tür | Varsayılan | Açıklama |
| --- | --- | --- | --- |
| `ReportsDirectory` | `string?` | `ContentRoot/Reports` | `.sbs` dosyalarının saklandığı dizin. |
| `RoutePrefix` | `string` | `"/sbs-report"` | UI ve API URL öneki. |
| `RequireAuthorization` | `bool` | `false` | Tüm UI ve API uçlarını Authorization ile korur. |
| `AuthorizationPolicy` | `string?` | `null` | Özel ASP.NET Core Authorization Policy adı. |

---

## 2. C# Servisleri (`SbsReport.Services`)

### `SbsReportEngine`
Headless rapor üretimi ve snapshot dışa aktarımı için ana giriş noktasıdır.

```csharp
// Şablonu diskten (önbellekli) yükler, DataSet ve parametrelerle render eder,
// ikili Snapshot (.sbsdoc) ve PDF byte[] döndürür.
(byte[] SnapshotBytes, byte[] PdfBytes) RenderHeadless(
    string reportTemplatePath,
    Dictionary<string, object>? parameters,
    DataSet? dataSet);

// Önceden üretilmiş snapshot'ı belirtilen formata dönüştürür.
byte[] ExportFromSnapshot(byte[] snapshotBytes, string format);
```

### `ReportExportService`
`RenderedReport` nesnesini 11 formattan birine dönüştürür:
```csharp
byte[] CreateSnapshot(RenderedReport rendered);
RenderedReport ReadSnapshot(byte[] snapshotBytes);
(byte[] Bytes, string ContentType, string Extension) Export(RenderedReport rendered, string format);
byte[] ExportFromSnapshot(byte[] snapshotBytes, string format);
byte[] ExportPdf(RenderedReport rendered);
byte[] ExportHtml(RenderedReport rendered);
```

---

## 3. Gömülü REST API Uçları

Varsayılan önek: `/sbs-report/api` (`options.RoutePrefix` değerine göre değişir).

| Metot | Yol | Açıklama |
| --- | --- | --- |
| `GET` | `/api/reports` | `ReportsDirectory` içindeki kayıtlı `.sbs` raporlarını listeler. |
| `GET` | `/api/reports/{name}` | Belirtilen `.sbs` raporunun JSON tanımını döndürür. |
| `POST` | `/api/reports/{name}` | Gönderilen `SbsReportDocument` gövdesini `.sbs` olarak kaydeder. |
| `DELETE` | `/api/reports/{name}` | Belirtilen raporu siler. |
| `POST` | `/api/reports/render` | `RenderRequest` (`Report` + opsiyonel `Data`) alır ve sayfalanmış `RenderedReport` JSON döndürür. |
| `POST` | `/api/reports/export/{format}` | `RenderRequest` alır ve belirtilen formatta (`pdf`, `xlsx`, `docx`, `html`, `csv`, `rtf`, `txt`, `xml`, `json`, `svg`, `sbsdoc`) dosya akışı döndürür. |
| `GET` | `/api/datasources/demo` | Tasarımcı için yerleşik demo veri kaynaklarını listeler. |
| `GET` | `/api/datasources/demo/{name}/rows` | Belirtilen demo veri kaynağının örnek satırlarını döndürür. |

---

## 4. İfade Motoru (`ExpressionEvaluator`)

### Yerleşik Sistem Değişkenleri
| Değişken | Açıklama |
| --- | --- |
| `{PageNumber}` | Geçerli sayfa numarası |
| `{TotalPageCount}` | Toplam sayfa sayısı |
| `{Line}` | Veri bandı içindeki satır numarası (1'den başlar) |
| `{Today}` | Güncel tarih (`dd.MM.yyyy`, `tr-TR`) |
| `{Now}` | Güncel tarih ve saat (`dd.MM.yyyy HH:mm`, `tr-TR`) |
| `{ReportName}` | Rapor adı |

### Desteklenen Fonksiyonlar
| Fonksiyon | Örnek | Açıklama |
| --- | --- | --- |
| `Count()` | `{Count()}` | Veri satır sayısını döndürür |
| `Sum(expr)` | `{Sum(Data.TUTAR)}` | Toplam alır |
| `Avg(expr)` | `{Avg(Data.FIYAT)}` | Ortalama alır |
| `Min(expr)` | `{Min(Data.STOK)}` | En küçük değeri döndürür |
| `Max(expr)` | `{Max(Data.STOK)}` | En büyük değeri döndürür |
| `Iif(cond, a, b)` | `{Iif(Data.STOK < 5, "Kritik", "Normal")}` | Koşullu değer döndürür |
| `Round(val, d)` | `{Round(Data.TUTAR, 2)}` | Ondalık yuvarlama yapar |
| `Abs(val)` | `{Abs(Data.FARK)}` | Mutlak değer alır |
| `Upper(str)` | `{Upper(Data.AD)}` | Türkçe kurallarına göre büyük harfe çevirir |
| `Lower(str)` | `{Lower(Data.AD)}` | Türkçe kurallarına göre küçük harfe çevirir |
| `Coalesce(a, b)` | `{Coalesce(Data.EK_UNVAN, "-")}` | İlk boş olmayan değeri döndürür |
| `Format(val, fmt)` | `{Format(Data.TUTAR, "N2")}` | `tr-TR` kültürüyle biçimlendirir |
