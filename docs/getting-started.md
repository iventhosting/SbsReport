# Kurulum ve İlk Rapor Kılavuzu

Bu rehber, **SbsReport** kütüphanesini sıfırdan bir ASP.NET Core projesine kurmayı, Web Designer IDE'yi açmayı ve ilk raporunuzu oluşturup PDF olarak almayı adım adım anlatır.

---

## 1. Gereksinimler

- **.NET 10 SDK** (`net10.0`)
- Herhangi bir ASP.NET Core projesi (Web API, Minimal API, MVC veya Blazor Server)

---

## 2. NuGet Paketini Yükleme

Projenizin kök dizininde aşağıdaki komutu çalıştırın:

```bash
dotnet add package SbsReport
```

Alternatif olarak Visual Studio'da **NuGet Package Manager** üzerinden `SbsReport` aratarak yükleyebilirsiniz.

---

## 3. `Program.cs` Entegrasyonu

`Program.cs` dosyanızı açın ve SbsReport servislerini + ara yazılımını ekleyin:

```csharp
using SbsReport;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSbsReport(options =>
{
    // Rapor şablonlarının (.sbs) tutulacağı klasör
    options.ReportsDirectory = Path.Combine(builder.Environment.ContentRootPath, "Reports");

    // Web Designer ve Viewer URL öneki
    options.RoutePrefix = "/sbs-report";
});

var app = builder.Build();

// Gömülü Web Designer UI ve REST API uçlarını etkinleştir
app.UseSbsReport();

// Opsiyonel: Ana sayfadan doğrudan tasarımcıya yönlendir
app.MapGet("/", () => Results.Redirect("/sbs-report/"));

app.Run();
```

### Yetkilendirme (Authorization) Ekleme

Canlı (production) ortamda rapor tasarımcısını yalnızca yetkili kullanıcılara açmak için:

```csharp
builder.Services.AddSbsReport(options =>
{
    options.RoutePrefix = "/sbs-report";
    options.RequireAuthorization = true;
    options.AuthorizationPolicy = "RaporYoneticisi"; // Opsiyonel: Boş bırakılırsa varsayılan policy kullanılır
});

// Middleware sırası önemlidir:
app.UseAuthentication();
app.UseAuthorization();
app.UseSbsReport();
```

---

## 4. Demo Projesini Yerelde Çalıştırma

SbsReport deposunu klonlayıp hazır örnek projeyi hemen deneyebilirsiniz:

```bash
git clone https://github.com/iventhosting/SbsReport.git
cd SbsReport
dotnet run --project samples/SbsReport.Demo
```

Uygulama başladıktan sonra tarayıcınızdan `/sbs-report/` adresine gidin.

---

## 5. İlk Raporunuzu Tasarlayın

1. `/sbs-report/` sayfasında **Dosya → Yeni** menüsünden **Boş Rapor** veya hazır şablonlardan birini seçin.
2. Sağ paneldeki **Sözlük (Dictionary)** sekmesinden `+ Veri Kaynağı` butonuna tıklayın ve veri kaynağınızın adını (ör. `Data`) ve kolonlarını tanımlayın.
3. Tuvale metin veya tablo bileşenleri ekleyin ve içeriklerine `{Data.AD}`, `{Data.TUTAR}` gibi ifadeler yazın.
4. Üst şeritten **Önizleme** veya **PDF Önizleme** sekmesine geçerek çıktıyı doğrulayın.
5. `Ctrl + S` ile raporunuzu kaydedin. Dosya, `ReportsDirectory` klasörüne `<RaporAdi>.sbs` olarak kaydedilir.

Sonraki adım: [C# Kodundan Rapor Basma ve Dışa Aktarma →](headless-rendering.md)
