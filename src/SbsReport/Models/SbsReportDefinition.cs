using System.Text.Json;

namespace SbsReport.Models;

public class SbsReportDocument
{
    public string Version { get; set; } = "1.0";
    public string Name { get; set; } = "YeniRapor";
    public string? Description { get; set; }
    public bool IsMasterTemplate { get; set; }
    public string? MasterReportName { get; set; }
    public bool InheritMasterHeader { get; set; } = true;
    public bool InheritMasterFooter { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public SbsPageSettings Page { get; set; } = new();
    public List<SbsDataSource> DataSources { get; set; } = new();
    public Dictionary<string, string> Variables { get; set; } = new();
    public List<SbsBand> Bands { get; set; } = new();
    public List<SbsReportPage>? Pages { get; set; }
    public List<SbsStyleDefinition>? Styles { get; set; }
}

public class SbsReportPage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Page 1";
    public SbsPageSettings Page { get; set; } = new();
    public List<SbsBand> Bands { get; set; } = new();
}

public class SbsStyleDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Style1";
    public string FontFamily { get; set; } = "Segoe UI, Inter, sans-serif";
    public double FontSizePt { get; set; } = 10;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public string Color { get; set; } = "#111827";
    public string BackgroundColor { get; set; } = "transparent";
    public string BorderColor { get; set; } = "#cbd5e1";
    public double BorderWidthPx { get; set; } = 0;
    public string BorderStyle { get; set; } = "Solid";
    public string BorderSides { get; set; } = "All";
}

public class SbsPageSettings
{
    public string PaperSize { get; set; } = "A4"; // A4 | A5 | Letter | Custom
    public string Orientation { get; set; } = "Portrait"; // Portrait | Landscape
    public double WidthMm { get; set; } = 210;
    public double HeightMm { get; set; } = 297;
    public double MarginTopMm { get; set; } = 10;
    public double MarginRightMm { get; set; } = 10;
    public double MarginBottomMm { get; set; } = 10;
    public double MarginLeftMm { get; set; } = 10;
    public double GridSizeMm { get; set; } = 2;
    public int GridColumns { get; set; } = 96;
    public int GridVerticalStep { get; set; } = 5;
    public string FormName { get; set; } = "Form";
    public string? Picture { get; set; }

    // Default Form Settings (PDF & Form Designer)
    public string DefaultLabelFontFamily { get; set; } = "Segoe UI, sans-serif";
    public double DefaultLabelFontSizePt { get; set; } = 10;
    public bool DefaultLabelBold { get; set; }
    public bool DefaultLabelItalic { get; set; }
    public bool DefaultLabelUnderline { get; set; }
    public string DefaultLabelColor { get; set; } = "#000000";
    public string DefaultLabelHAlign { get; set; } = "left";
    public string DefaultLabelVAlign { get; set; } = "middle";
    public double DefaultLabelWidthMm { get; set; } = 30;

    public string DefaultInnerTextFontFamily { get; set; } = "Segoe UI, sans-serif";
    public double DefaultInnerTextFontSizePt { get; set; } = 9;
    public bool DefaultInnerTextBold { get; set; }
    public bool DefaultInnerTextItalic { get; set; }
    public bool DefaultInnerTextUnderline { get; set; }
    public string DefaultInnerTextColor { get; set; } = "#000000";
    public string DefaultInnerTextHAlign { get; set; } = "left";
    public string DefaultInnerTextVAlign { get; set; } = "middle";

    public string DefaultBackColor { get; set; } = "#ffffff";
    public string DefaultBorderColor { get; set; } = "#cbd5e1";
    public string DefaultCheckColor { get; set; } = "#000000";
    public string DefaultBorderWidth { get; set; } = "Thin"; // None | Thin | Medium | Thick

    public string DefaultDescriptionFontFamily { get; set; } = "Segoe UI, sans-serif";
    public double DefaultDescriptionFontSizePt { get; set; } = 9;
    public bool DefaultDescriptionBold { get; set; }
    public bool DefaultDescriptionItalic { get; set; }
    public bool DefaultDescriptionUnderline { get; set; }

    public double DefaultPaddingLeftMm { get; set; } = 0;
    public double DefaultPaddingTopMm { get; set; } = 0;
    public double DefaultPaddingRightMm { get; set; } = 0;
    public double DefaultPaddingBottomMm { get; set; } = 2;

    public bool ShowGrid { get; set; } = true;
    public bool SnapToGrid { get; set; } = true;
    public SbsWatermark? Watermark { get; set; }
}

public class SbsWatermark
{
    public bool Enabled { get; set; }
    public string Text { get; set; } = "SOBIS";
    public string FontFamily { get; set; } = "Inter, Arial, sans-serif";
    public double FontSizePt { get; set; } = 72;
    public string Color { get; set; } = "#cbd5e1";
    public double Angle { get; set; } = -45;
    public double Opacity { get; set; } = 0.18;
}

public class SbsDataSource
{
    public string Name { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string Type { get; set; } = "demo"; // demo | json | sql
    public List<SbsColumn> Columns { get; set; } = new();
    public List<Dictionary<string, JsonElement>>? InlineData { get; set; }
}

public class SbsColumn
{
    public string Name { get; set; } = string.Empty;
    public string? Alias { get; set; }
    public string? NameInSource { get; set; }
    public string DataType { get; set; } = "string"; // string | number | date | boolean
    public bool IsCalculated { get; set; }
    public string? Expression { get; set; }
}

public class SbsBand
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Type { get; set; } = "Data"; // ReportTitle | PageHeader | Header | GroupHeader | Data | GroupFooter | Footer | PageFooter | ReportSummary
    public string Name { get; set; } = "DataBand1";
    public double HeightMm { get; set; } = 12;
    public string? DataSource { get; set; }
    public string? GroupBy { get; set; }
    public string? SortBy { get; set; }
    public string SortDirection { get; set; } = "asc"; // asc | desc
    public string? FilterExpression { get; set; }
    public int CountData { get; set; } = 0; // 0 = tüm satırlar, >0 = belirtilen satır sayısı
    public string? MasterComponent { get; set; }
    public bool CanGrow { get; set; }
    public bool CanShrink { get; set; }
    public bool PrintOnAllPages { get; set; } = true;
    public bool NewPageBefore { get; set; }
    public bool NewPageAfter { get; set; }
    public bool Enabled { get; set; } = true;
    public string PrintOn { get; set; } = "AllPages"; // AllPages | ExceptFirstPage | ExceptLastPage | OnlyFirstPage | OnlyLastPage
    public bool PrintAtBottom { get; set; }
    public bool ResetPageNumber { get; set; }
    public bool KeepGroupTogether { get; set; }
    public int Columns { get; set; } = 1;
    public double ColumnGapsMm { get; set; } = 4;
    public string? KeyDataColumn { get; set; }
    public string? MasterKeyDataColumn { get; set; }
    public double IndentMm { get; set; } = 8;
    public string BackgroundColor { get; set; } = "transparent";
    public string? EvenBackgroundColor { get; set; }
    public List<SbsComponent> Components { get; set; } = new();
}

public class SbsCondition
{
    public string Expression { get; set; } = string.Empty;
    public string? TextColor { get; set; }
    public string? BackgroundColor { get; set; }
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public bool? Underline { get; set; }
    public string? BorderColor { get; set; }
    public bool? Visible { get; set; }
}

public class SbsComponent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Type { get; set; } = "Text"; // Text | Input | CheckBox | Radio | DatePicker | Image | Shape | Line | Barcode | Chart | Table
    public string Name { get; set; } = "Text1";
    public double XMm { get; set; }
    public double YMm { get; set; }
    public double WidthMm { get; set; } = 40;
    public double HeightMm { get; set; } = 8;
    public string Text { get; set; } = string.Empty;
    public string FontFamily { get; set; } = "Inter, Arial, sans-serif";
    public double FontSizePt { get; set; } = 10;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strike { get; set; }
    public string Color { get; set; } = "#111827";
    public string BackgroundColor { get; set; } = "transparent";
    public string BorderColor { get; set; } = "#cbd5e1";
    public double BorderWidthPx { get; set; } = 0;
    public string BorderStyle { get; set; } = "Solid"; // Solid | Dashed | Dotted | Double
    public string BorderSides { get; set; } = "All"; // All | Top | Bottom | Left | Right | None
    public double CornerRadiusPx { get; set; } = 0;
    public string HAlign { get; set; } = "left"; // left | center | right | justify
    public string VAlign { get; set; } = "middle"; // top | middle | bottom
    public string? Format { get; set; }
    public bool WordWrap { get; set; } = true;
    public bool AllowHtmlTags { get; set; }
    public double Angle { get; set; } = 0;

    // Padding (mm)
    public double PaddingLeftMm { get; set; }
    public double PaddingTopMm { get; set; }
    public double PaddingRightMm { get; set; }
    public double PaddingBottomMm { get; set; }

    // Form Controls (Input / CheckBox / Radio / DatePicker / ComboBox / Signature / Button)
    public string? Label { get; set; }
    public string? Placeholder { get; set; }
    public bool Checked { get; set; }
    public string? GroupName { get; set; }
    public string? Items { get; set; } // ComboBox items
    public string? SignatureData { get; set; } // Base64 signature path/SVG
    public string? ButtonAction { get; set; } // Print | Submit | Reset
    public bool ReadOnly { get; set; }
    public bool Required { get; set; }
    public string? TableColumnsJson { get; set; }

    // Media & Shape
    public string? ImageUrl { get; set; }
    public string ImageFit { get; set; } = "Contain"; // Contain (Orantılı) | Fill (Ger/Stretch) | Cover (Kırp) | None (Orijinal)
    public string? ShapeType { get; set; } = "Rectangle"; // Rectangle | RoundedRectangle | Ellipse | Triangle
    public string? LineOrientation { get; set; } = "Horizontal"; // Horizontal | Vertical | Slant

    // Barcode
    public string BarcodeFormat { get; set; } = "Code128"; // Code128 | QR | EAN13

    // TextInCells (TC Kimlik No / IBAN / Vergi No kutucuklu metin)
    public int CellCount { get; set; } = 0; // 0 = genişliğe göre otomatik
    public double CellWidthMm { get; set; } = 5;
    public double CellGapMm { get; set; } = 0.8;

    // Chart
    public string? ChartType { get; set; } = "Bar"; // Bar | Line | Pie | Doughnut
    public string? DataSource { get; set; }
    public string? CategoryField { get; set; }
    public string? ValueField { get; set; }

    // Behaviors
    public bool Enabled { get; set; } = true;
    public bool Printable { get; set; } = true;
    public bool CanGrow { get; set; }
    public bool CanShrink { get; set; }
    public bool GrowToHeight { get; set; }
    public bool ShrinkFontToFit { get; set; }
    public double ShrinkFontToFitMinPt { get; set; } = 6;
    public bool HideZeros { get; set; }
    public int MaxNumberOfLines { get; set; } = 0; // 0 = sınırsız
    public string ProcessingDuplicates { get; set; } = "None"; // None | Hide | Merge
    public string PrintOn { get; set; } = "AllPages"; // AllPages | ExceptFirstPage | ExceptLastPage | OnlyFirstPage | OnlyLastPage
    public bool Locked { get; set; }
    public List<SbsCondition>? Conditions { get; set; }
}
