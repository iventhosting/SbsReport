# Değişiklik Günlüğü (Changelog)

Bu projedeki tüm önemli değişiklikler bu dosyada belgelenir.

Format [Keep a Changelog](https://keepachangelog.com/tr/1.0.0/) standardına ve [Semantic Versioning](https://semver.org/) sürümleme kurallarına uygundur.

## [1.0.10] - 2026-11-24

### İyileştirilenler — NuGet Paketi & Dokümantasyon
- **Saf Markdown NuGet README (`NUGET_README.md`):** NuGet.org ve Visual Studio NuGet Paket Yöneticisi'nde ham HTML etiketleri (`<div>`, `<h3>`, `<details>`, `<table>`) ve Mermaid blokları yüzünden oluşan boş/bozuk görünüm giderildi; %100 saf CommonMark/GFM Markdown ile paketlendi.
- **Paket İkonu (`icon.png`):** NuGet.org ve Visual Studio paket listeleri için `128×128` PNG Sobis paket ikonu eklendi.
- **Güncel GitHub `README.md`:** `v1.0.9` ile gelen 12 dışa aktarım formatı (OOXML `.docx` dahil), yeni ifade fonksiyonları, bileşenler ve Web Designer kısayolları eklendi.

## [1.0.9] - 2026-11-24

### Eklenenler — Rapor Motoru & Bileşenler
- **Bant & Sayfa Davranışları:** `GrowToHeight`, `ShrinkFontToFit`, `ProcessingDuplicates` (`None` / `Hide` / `Merge`), `PrintOn` (`AllPages`, `ExceptFirstPage`, `ExceptLastPage`, `OnlyFirstPage`, `OnlyLastPage`), `PrintAtBottom`, `ResetPageNumber`, `KeepGroupTogether`, `Enabled`, `Printable`, `HideZeros` (Sıfırları Gizle), `MaxNumberOfLines` ve `ImageFit` (`Contain` / `Fill` / `Cover` / `None`).
- **Yeni Bant & Bileşen Yetenekleri:** `EmptyBand` (sayfa sonuna kadar otomatik boş satır doldurma), `OverlayBand` (arka plan katmanı), Çok Sütunlu `DataBand` (`Columns` 1–12, `ColumnGapsMm`), **`TextInCells`** (TC Kimlik No / IBAN / Vergi No karakter kutucukları), XSS korumalı **`AllowHtmlTags`** (`<b>`, `<i>`, `<u>`, `<br/>`, `<color=#hex>`) ve **Hesaplanan Sütunlar (`SbsColumn.IsCalculated` / `Expression`)**.
- **İfade Motoru (`ExpressionEvaluator`):** Türkçe yazıyla tutar/sayı (`ToCurrencyWordsTr`, `ToWordsTr`), yeni özet fonksiyonları (`CountDistinct`, `SumDistinct`, `RunningSum`, `RunningCount`, `Median`, `First`, `Last`), metin/tarih/mantık/matematik fonksiyonları (`Left`, `Right`, `Substring`, `Replace`, `ProperCase`, `DateDiff`, `MonthName`, `AddDays`, `Switch`, `Choose`, `Ceiling`, `Floor`, `Pow`, `Sqrt`) ve yeni sistem değişkenleri (`PageNofM`, `IsFirstPage`, `IsLastPage`, `GroupLine`, `LineThrough`, `Time`).

### Eklenenler — Dışa Aktarım (Export)
- **Gerçek OOXML Microsoft Word (`.docx`):** Saf C# `ZipArchive` ile ECMA-376 uyumlu, uyarı vermeden açılan Word belgesi ihracı.
- **Gelişmiş OOXML Excel (`.xlsx`):** Türkçe para/sayı değerleri için gerçek sayısal hücre (`t="n"`, `#,##0.00` / `#,##0`), tarih ve sıfırla başlayan kimlik numarası koruması, ilk sayfadan O(1) otomatik sütun genişliği (`<cols>`).

### Eklenenler & İyileştirilenler — Web Designer IDE
- **Canlı Rapor Denetleyici (`Report Checker`):** Alt durum çubuğunda canlı hata/uyarı sayacı ve tek tıkla ilgili bileşene odaklanma.
- **Yeni Diyaloglar (Sabit Yükseklikli):** Stil Tasarımcısı (`✨ Stiller`), Gelişmiş Kenarlık Ayarları (`⊞ Border Setup`), sağ ikon menülü ve canlı önizlemeli Sayfa Yapısı (`⚙ Sayfa Yapısı` — boş sayfaya çift tıkla da açılır), Yeni/Hesaplanan Sütun editörü (`#colEditorModal`) ve aramalı/HTML araç çubuklu yeni İfade Düzenleyici.
- **4 Ayrı Sağ Tık Menüsü:** Obje (`#ctxMenu`), Bant (`#ctxBandMenu`), Sayfa/Form (`#ctxPageMenu`) ve Sözlük (`#ctxDictMenu` — veri kaynağı/sütun ekleme, hesaplanan sütun, düzenleme ve silme).
- **Modern UI & Tema Tutarlılığı:** Kompakt 3 sekmeli Şeffaf destekli renk paleti (`Palet` / `Web` / `Özel`), canlı özellik arama/filtreleme (TR/EN eş anlamlı) ve alfabetik sıralama, tema uyumlu ince seçim vurgusu ve canlı iç boşluk (`padding`) desteği, tıklayınca anında görünen 8 boyutlandırma tutamacı, **Shift+Ok** ile ızgara adımı kadar genişlik/yükseklik boyutlandırma ve **Ctrl+Ok / Ok** ile ızgara adımı kadar taşıma, `AllowHtmlTags` çok satırlı `Input` ve karışık metin desteği, tam sayfa önizleme kaydırma düzeltmesi, sabit `14px` checkbox'lar, Sobis ikonu (`favicon.ico`) ve tüm tarayıcı `alert`/`confirm`/`prompt` pencerelerinin tema uyumlu `#sbsDialogModal` ile değiştirilmesi.

## [1.0.8] - 2026-11-24

### Düzeltilenler
- **Render motoru:** Raporda veri kaynağına bağlı bir Data bandı varken veri kaynağı boş bırakılmış Data bandı, o kaynağın kayıt sayısı kadar tekrar basılıyordu ("Boş Rapor" şablonunda satırların önüne boşluk ekliyordu). Bu bant artık statik bant gibi bir kez basılır. Raporda hiç bağlı Data bandı yoksa eski davranış (ilk veri kaynağını kullanma) korunur.
- **Web Designer:** Bant silme ve "Master Yap" ile bant çıkarma işlemlerinden sonra bantlar geri geliyordu (`syncActivePageToPagesArray` eski bant dizisini geri yüklüyordu).
- **Web Designer:** Yerleşim sihirbazındaki sütun ve bant onay kutuları alt alta ve ortalı görünüyordu; satır içi hizalandı.

## [1.0.5] - 2026-11-22

### Eklenenler & İyileştirilenler
- NuGet paketi içine kapsamlı adım adım `README.md` dokümantasyonu ve XML IntelliSense (`SbsReport.xml`) eklendi.
- Açık kaynak GitHub deposu için `docs/` rehberleri (`getting-started`, `headless-rendering`, `web-designer`, `api-reference`), `LICENSE` (MIT) ve `CONTRIBUTING.md` eklendi.
- NuGet paket meta verileri (`Description`, `PackageTags`, `PackageProjectUrl`, `RepositoryUrl`, `PackageReleaseNotes`) güncellendi.

## [1.0.4] - 2026-11-22

### Eklenenler & İyileştirilenler
- **Saf C# Streaming PDF 1.4 Motoru (`SbsPdfWriter`):** Harici bağımlılık olmadan ultra-kompakt PDF üretimi.
- **Gerçek TrueType Font Subsetting (`glyf`/`loca`):** Yalnızca kullanılan glifleri gömerek PDF boyutunu ciddi oranda küçültür.
- **Türkçe Karakter & Unicode Desteği:** 1-byte Windows-1254 (`/Differences` + `/ToUnicode`) hızlı yolu ve tablo dışı Unicode karakterler için otomatik 2-byte `CIDFontType2` yedeği.
- **Düşük Bellek Kullanımı:** `ArrayPool<byte>` ve `ZLibStream(Optimal)` ile sıfır LOH baskısı.
- **İkili Snapshot Kodeği (`SbsSnapshotCodec`):** SQL/veri kaynağını tekrar çalıştırmadan anında farklı formatlara dönüşüm.

## [1.0.0] - 2026-11-21

### Eklenenler
- İlk açık kaynak sürüm (`net10.0`).
- Gömülü 3 bölmeli Web Designer IDE & Viewer (`AddSbsReport` / `UseSbsReport`).
- Headless C# raporlama motoru (`SbsReportEngine.RenderHeadless` ve `ExportFromSnapshot`).
- 11 dışa aktarım formatı: `PDF`, `Excel (.xls)`, `Word (.doc)`, `HTML`, `CSV`, `RTF`, `TXT`, `XML`, `JSON`, `SVG`, `Snapshot (.sbsdoc)`.
- Master Form (kalıtsal üstbilgi/altbilgi) desteği.
- Önbellekli AST ifade motoru (`ExpressionEvaluator`) ve Türkçe karakter duyarsız kolon/değişken eşleştirme.
- Türkçe (TR) ve İngilizce (EN) arayüz dili ile Açık/Koyu tema desteği.
