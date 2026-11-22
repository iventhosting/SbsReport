# Değişiklik Günlüğü (Changelog)

Bu projedeki tüm önemli değişiklikler bu dosyada belgelenir.

Format [Keep a Changelog](https://keepachangelog.com/tr/1.0.0/) standardına ve [Semantic Versioning](https://semver.org/) sürümleme kurallarına uygundur.

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
