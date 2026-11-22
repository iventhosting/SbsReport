# Web Designer IDE, Master Form ve Şablon Rehberi

**SbsReport**, ASP.NET Core uygulamanızın içine gömülü olarak gelen 3 bölmeli bir Web Designer IDE ve Viewer arayüzü sunar. Ekstra npm kurulumu, build adımı veya statik dosya kopyalama gerektirmez; tüm UI dosyaları NuGet paketinin içinde gömülü (`EmbeddedResource`) olarak gelir.

---

## 1. Web Designer'ı Açma ve URL Parametreleri

`app.UseSbsReport()` eklendikten sonra tasarımcı varsayılan olarak şu adreste yayındadır:

```text
https://<sunucu>/sbs-report/
```

Desteklenen sorgu parametreleri:
- `report`: Açılışta doğrudan yüklenecek rapor adı (örn. `?report=SatisFaturasi`)
- `theme`: Arayüz teması (`dark` veya `light`)
- `lang`: Arayüz dili (`tr` veya `en`)

Örnek:
```text
https://<sunucu>/sbs-report/?report=AktifPersonelListesi&theme=dark&lang=tr
```

---

## 2. Arayüz Düzeni

1. **Üst Şerit Menü (Ribbon):**
   - `Dosya (File)`: Yeni rapor, şablon galerisi, kaydet, `.sbs` dışa aktar.
   - `Giriş (Home)`: Yazı tipi, boyut, kalın/italik/altı çizili, hizalama, renkler, kenarlıklar.
   - `Ekle (Insert)`: Bantlar ve görsel bileşenler.
   - `Yerleşim (Layout)`: Hizalama, dağıtma, öne/arkaya getir.
   - `Sayfa (Page)`: A4/A5/Letter, Dikey/Yatay, kenar boşlukları, filigran (Watermark).
   - `Önizleme (Preview)` & `PDF Önizleme (PDF Preview)`: Canlı HTML ve gerçek PDF önizleme, indirme ve yazdırma.
2. **Sol Araç Çubuğu (16 Bileşen):**
   - Seçim (Pointer), Metin (Text), Giriş Kutusu (Input), Onay Kutusu (CheckBox), Seçenek Butonu (Radio), Açılır Liste (ComboBox), Tarih Seçici (DatePicker), Dijital İmza (Signature), İşlem Butonu (Button), Görsel (Image), Şekil (Shape), Çizgi (Line), Barkod & Karekod (Code128, QR, EAN13), Grafik (Chart), Tablo & Matris (Table / CrossTab), Panel (Container).
3. **Merkez Çizim Alanı (WYSIWYG Canvas):**
   - Milimetrik cetveller, 4 köşe kesim işaretleri, yapışkan ızgara (Snap to Grid), 8 boyutlandırma tutamacı ve akıllı hizalama kılavuz çizgileri.
4. **Sağ Panel (Inspector, Form Ağacı, Sözlük):**
   - **Özellikler:** Kategorili / Alfabetik görünüm ve anlık filtreleme.
   - **Form Ağacı:** Sayfa, bant ve bileşen hiyerarşisi.
   - **Sözlük (Dictionary):** Veri kaynakları, kolon şemaları ve rapor değişkenleri/parametreleri.

---

## 3. Verisiz Şema Tanımlama (C# `DataSet` ile Uyum)

Tasarım aşamasında canlı veritabanı bağlantısı kurmadan şablon oluşturabilirsiniz:

1. Sağ panelde **Sözlük → `+ Veri Kaynağı` → Şema Tanımla** sekmesine geçin.
2. Veri kaynağı adını girin (örn. `Data`) ve C# `DataTable` kolonlarınızı ekleyin (`TCKIMLIKNO, AD, SOYAD, UNVAN`).
3. Veri kaynağını tuvale sürükleyip bırakarak veya bileşen metnine `{Data.AD}` yazarak bağlayın.
4. Çalışma zamanında `SbsReportEngine.RenderHeadless(...)` çağrısında aynı tablo adıyla gönderdiğiniz `DataSet` otomatik olarak bağlanır.

---

## 4. Master Form (Sabit Üstbilgi ve Altbilgi)

Tüm kurumsal raporlarınızda aynı logo, kurum başlığı ve sayfa altbilgisini tekrar tekrar çizmek yerine **Master Form** kullanabilirsiniz:

- Yeni bir raporu Master Form olarak işaretleyin veya mevcut raporun üst/alt bilgisini **`Master Yap`** ile tek tıkla Master şablona dönüştürün.
- Diğer raporlarda `MasterReportName` özelliğini bu Master Form'a bağlayın.
- Hem Web Designer önizlemesinde hem de C# `RenderHeadless` çağrısında Master Form aynı klasörden otomatik yüklenerek tüm sayfalara uygulanır.

---

## 5. Çift Tıklama Hızlı Editörleri

Tuvaldeki bileşenlere çift tıklayarak özel düzenleme pencerelerini açabilirsiniz:
- **`Text`**: Metin ve İfade Editörü (alanlar, sistem değişkenleri, fonksiyonlar)
- **`Data` / `Table` Band**: Veri kaynağı, sıralama, filtreleme, satır sayısı
- **`GroupHeader` Band**: Gruplama kolonu / ifadesi
- **`Image` / `Barcode` / `Chart`**: Görsel, barkod/QR ve grafik ayarları
