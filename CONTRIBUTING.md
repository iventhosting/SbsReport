# SbsReport'a Katkıda Bulunma

**SbsReport** açık kaynak (MIT) bir projedir ve her türlü katkıya açıktır. Hata bildirimi, özellik önerisi, dokümantasyon iyileştirmesi veya kod katkısı gönderebilirsiniz.

## Geliştirme Ortamını Hazırlama

### Gereksinimler
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git

### Projeyi Yerelde Çalıştırma

```bash
git clone https://github.com/iventhosting/SbsReport.git
cd SbsReport

# Çözümü derleyin
dotnet build SbsReport.slnx

# Demo uygulamasını başlatın
dotnet run --project samples/SbsReport.Demo
```

Uygulama başladıktan sonra tarayıcınızdan `/sbs-report/` adresine giderek Web Designer IDE'yi test edebilirsiniz.

## Pull Request Süreci

1. Depoyu fork'layın ve kendi branch'inizi oluşturun (`git checkout -b feature/yeni-ozellik` veya `fix/hata-duzeltmesi`).
2. Değişikliklerinizi temiz ve anlaşılır commit'lerle yapın.
3. Kütüphanenin ve demo projesinin hatasız derlendiğinden emin olun:
   ```bash
   dotnet build SbsReport.slnx -c Release
   ```
4. NuGet paketinin sorunsuz oluştuğunu doğrulayın:
   ```bash
   dotnet pack src/SbsReport/SbsReport.csproj -c Release -o ./nupkg
   ```
5. Değişikliklerinizi açıklayan bir Pull Request açın.

## Hata Bildirimi ve Özellik İsteği

- Hata bildirirken mümkünse örnek `.sbs` şablonunu, kullandığınız .NET sürümünü ve yeniden üretim adımlarını ekleyin.
- Yeni özellik önerilerinde kullanım senaryosunu kısaca açıklayın.
