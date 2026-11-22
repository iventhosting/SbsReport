using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using SbsReport.Models;

namespace SbsReport.Services;

public class ReportStorageService
{
    private readonly string _reportsDir;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ReportStorageService(IWebHostEnvironment env, SbsReportOptions options)
    {
        _reportsDir = !string.IsNullOrWhiteSpace(options.ReportsDirectory)
            ? options.ReportsDirectory
            : Path.Combine(env.ContentRootPath, "Reports");
        Directory.CreateDirectory(_reportsDir);
        EnsureSampleReports();
    }

    public IEnumerable<object> ListReports()
    {
        return Directory.GetFiles(_reportsDir, "*.sbs")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f =>
            {
                bool isMaster = false;
                string? masterReportName = null;
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(f.FullName));
                    if (doc.RootElement.TryGetProperty("isMasterTemplate", out var im) && im.ValueKind == JsonValueKind.True)
                        isMaster = true;
                    if (doc.RootElement.TryGetProperty("masterReportName", out var mr) && mr.ValueKind == JsonValueKind.String)
                        masterReportName = mr.GetString();
                }
                catch { /* ignore malformed metadata */ }

                return new
                {
                    name = Path.GetFileNameWithoutExtension(f.Name),
                    fileName = f.Name,
                    sizeBytes = f.Length,
                    updatedAt = f.LastWriteTimeUtc,
                    isMaster,
                    masterReportName
                };
            });
    }

    public SbsReportDocument? LoadReport(string name)
    {
        var path = GetSafePath(name);
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SbsReportDocument>(json, JsonOptions);
    }

    public void SaveReport(string name, SbsReportDocument doc)
    {
        doc.Name = SanitizeName(name);
        doc.UpdatedAt = DateTime.UtcNow;
        var path = GetSafePath(doc.Name);
        var json = JsonSerializer.Serialize(doc, JsonOptions);
        File.WriteAllText(path, json);
    }

    public bool DeleteReport(string name)
    {
        var path = GetSafePath(name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    private string GetSafePath(string name)
    {
        var clean = SanitizeName(name);
        return Path.Combine(_reportsDir, clean + ".sbs");
    }

    private static string SanitizeName(string name)
    {
        var baseName = Path.GetFileNameWithoutExtension(name.Trim());
        foreach (var ch in Path.GetInvalidFileNameChars())
            baseName = baseName.Replace(ch, '_');
        return string.IsNullOrWhiteSpace(baseName) ? "YeniRapor" : baseName;
    }

    private void EnsureSampleReports()
    {
        var formPath = Path.Combine(_reportsDir, "BasvuruFormu.sbs");
        if (!File.Exists(formPath))
        {
            SaveReport("BasvuruFormu", CreateApplicationFormSample());
        }

        var stockPath = Path.Combine(_reportsDir, "UrunStokRaporu.sbs");
        if (!File.Exists(stockPath))
        {
            SaveReport("UrunStokRaporu", CreateProductStockSample());
        }

        var medicalPath = Path.Combine(_reportsDir, "HastaMuayeneRaporu.sbs");
        if (!File.Exists(medicalPath))
        {
            SaveReport("HastaMuayeneRaporu", CreateMedicalReportSample());
        }

        var invoicePath = Path.Combine(_reportsDir, "SatisFaturasi.sbs");
        if (!File.Exists(invoicePath))
        {
            SaveReport("SatisFaturasi", CreateSalesInvoiceSample());
        }

        var masterPath = Path.Combine(_reportsDir, "SbsMasterAntet.sbs");
        if (!File.Exists(masterPath))
        {
            SaveReport("SbsMasterAntet", CreateMasterAntetSample());
        }
    }

    private static SbsReportDocument CreateApplicationFormSample()
    {
        return new SbsReportDocument
        {
            Version = "1.0",
            Name = "BasvuruFormu",
            Description = "Standart Başvuru Formu (PDF & Form Tasarımı)",
            Page = new SbsPageSettings
            {
                PaperSize = "A4",
                Orientation = "Portrait",
                WidthMm = 210,
                HeightMm = 297,
                MarginTopMm = 0,
                MarginLeftMm = 0,
                MarginRightMm = 0,
                MarginBottomMm = 10,
                GridColumns = 96,
                GridVerticalStep = 5,
                FormName = "Form",
                Watermark = new SbsWatermark
                {
                    Enabled = true,
                    Text = "Trial",
                    Angle = -45,
                    Opacity = 0.12,
                    FontSizePt = 76,
                    Color = "#64748b"
                }
            },
            Bands = new()
            {
                // Top Navy Banner Band (#0e2b4f)
                new SbsBand
                {
                    Type = "ReportTitle",
                    Name = "HeaderBanner",
                    HeightMm = 38,
                    BackgroundColor = "#0e2b4f",
                    Components = new()
                    {
                        new SbsComponent
                        {
                            Type = "Text", Name = "CompanyLogo",
                            XMm = 18, YMm = 14, WidthMm = 120, HeightMm = 12,
                            Text = "✦  EXAMPLE, INC.",
                            FontFamily = "Segoe UI, Inter, sans-serif",
                            FontSizePt = 15, Bold = true, Color = "#ffffff"
                        }
                    }
                },
                // Form Body Band
                new SbsBand
                {
                    Type = "Data",
                    Name = "FormBody",
                    HeightMm = 245,
                    BackgroundColor = "#ffffff",
                    Components = new()
                    {
                        // Application Form Title (Application in dark navy, Form in green)
                        new SbsComponent
                        {
                            Type = "Text", Name = "TitleApplication",
                            XMm = 18, YMm = 8, WidthMm = 44, HeightMm = 11,
                            Text = "Application",
                            FontFamily = "Segoe UI, Inter, sans-serif",
                            FontSizePt = 20, Bold = true, Color = "#0e2b4f"
                        },
                        new SbsComponent
                        {
                            Type = "Text", Name = "TitleForm",
                            XMm = 62, YMm = 8, WidthMm = 35, HeightMm = 11,
                            Text = "Form",
                            FontFamily = "Segoe UI, Inter, sans-serif",
                            FontSizePt = 20, Bold = true, Color = "#008b6b"
                        },
                        // Date of application
                        new SbsComponent
                        {
                            Type = "Text", Name = "DateLabel",
                            XMm = 135, YMm = 4, WidthMm = 55, HeightMm = 5,
                            Text = "DATE OF APPLICATION",
                            FontFamily = "Segoe UI, Inter, sans-serif",
                            FontSizePt = 7.5, Bold = true, Color = "#64748b", HAlign = "right"
                        },
                        new SbsComponent
                        {
                            Type = "DatePicker", Name = "DateValue",
                            XMm = 135, YMm = 10, WidthMm = 55, HeightMm = 8,
                            Text = "11/20/2026",
                            FontFamily = "Segoe UI, Inter, sans-serif",
                            FontSizePt = 10, Bold = true, Color = "#0e2b4f",
                            BorderWidthPx = 1, BorderColor = "#cbd5e1", CornerRadiusPx = 2,
                            BackgroundColor = "#ffffff", HAlign = "center"
                        },

                        // Section 1: PERSONAL INFORMATION
                        new SbsComponent
                        {
                            Type = "Shape", Name = "Sec1Bar",
                            XMm = 18, YMm = 24, WidthMm = 2.5, HeightMm = 7,
                            BackgroundColor = "#008b6b", BorderWidthPx = 0
                        },
                        new SbsComponent
                        {
                            Type = "Text", Name = "Sec1Title",
                            XMm = 22, YMm = 24, WidthMm = 168, HeightMm = 7,
                            Text = "PERSONAL INFORMATION",
                            FontFamily = "Segoe UI, Inter, sans-serif",
                            FontSizePt = 10.5, Bold = true, Color = "#0e2b4f"
                        },

                        // Left column
                        new SbsComponent { Type = "Text", Name = "LblFullName", XMm = 18, YMm = 36, WidthMm = 28, HeightMm = 7, Text = "Full Name :", FontSizePt = 9, Color = "#334155" },
                        new SbsComponent { Type = "Input", Name = "InpFullName", XMm = 48, YMm = 35, WidthMm = 48, HeightMm = 7.5, Text = "", Placeholder = "John Doe", BorderWidthPx = 1, BorderColor = "#cbd5e1", CornerRadiusPx = 2 },

                        new SbsComponent { Type = "Text", Name = "LblNickname", XMm = 18, YMm = 46, WidthMm = 28, HeightMm = 7, Text = "Nickname :", FontSizePt = 9, Color = "#334155" },
                        new SbsComponent { Type = "Input", Name = "InpNickname", XMm = 48, YMm = 45, WidthMm = 48, HeightMm = 7.5, Text = "", Placeholder = "Johnny", BorderWidthPx = 1, BorderColor = "#cbd5e1", CornerRadiusPx = 2 },

                        new SbsComponent { Type = "Text", Name = "LblBirthDate", XMm = 18, YMm = 56, WidthMm = 28, HeightMm = 7, Text = "Date of Birth :", FontSizePt = 9, Color = "#334155" },
                        new SbsComponent { Type = "DatePicker", Name = "InpBirthDate", XMm = 48, YMm = 55, WidthMm = 48, HeightMm = 7.5, Text = "01/01/1970", BorderWidthPx = 1, BorderColor = "#cbd5e1", CornerRadiusPx = 2 },

                        new SbsComponent { Type = "Text", Name = "LblEmail", XMm = 18, YMm = 66, WidthMm = 28, HeightMm = 7, Text = "Email :", FontSizePt = 9, Color = "#334155" },
                        new SbsComponent { Type = "Input", Name = "InpEmail", XMm = 48, YMm = 65, WidthMm = 48, HeightMm = 7.5, Text = "", Placeholder = "john@example.com", BorderWidthPx = 1, BorderColor = "#cbd5e1", CornerRadiusPx = 2 },

                        new SbsComponent { Type = "Text", Name = "LblGender", XMm = 18, YMm = 76, WidthMm = 28, HeightMm = 7, Text = "Gender :", FontSizePt = 9, Color = "#334155" },
                        new SbsComponent { Type = "Radio", Name = "RadMale", XMm = 48, YMm = 76, WidthMm = 20, HeightMm = 6, Text = "Male", Checked = false, GroupName = "Gender" },
                        new SbsComponent { Type = "Radio", Name = "RadFemale", XMm = 70, YMm = 76, WidthMm = 26, HeightMm = 6, Text = "Female", Checked = true, GroupName = "Gender" },

                        new SbsComponent { Type = "Text", Name = "LblMarital", XMm = 18, YMm = 86, WidthMm = 28, HeightMm = 7, Text = "Marital Status :", FontSizePt = 9, Color = "#334155" },
                        new SbsComponent { Type = "ComboBox", Name = "InpMarital", XMm = 48, YMm = 85, WidthMm = 48, HeightMm = 7.5, Text = "Single", Items = "Single,Married,Divorced", BorderWidthPx = 1, BorderColor = "#cbd5e1", CornerRadiusPx = 2 },

                        new SbsComponent { Type = "Text", Name = "LblCountry", XMm = 18, YMm = 96, WidthMm = 28, HeightMm = 7, Text = "Country :", FontSizePt = 9, Color = "#334155" },
                        new SbsComponent { Type = "ComboBox", Name = "InpCountry", XMm = 48, YMm = 95, WidthMm = 48, HeightMm = 7.5, Text = "United States", Items = "United States,United Kingdom,Germany,France,Türkiye,Canada", BorderWidthPx = 1, BorderColor = "#cbd5e1", CornerRadiusPx = 2 },

                        // Right column
                        new SbsComponent { Type = "Text", Name = "LblNatId", XMm = 104, YMm = 36, WidthMm = 28, HeightMm = 7, Text = "National Id No :", FontSizePt = 9, Color = "#334155" },
                        new SbsComponent { Type = "Input", Name = "InpNatId", XMm = 134, YMm = 35, WidthMm = 56, HeightMm = 7.5, Text = "", Placeholder = "12345678901", BorderWidthPx = 1, BorderColor = "#cbd5e1", CornerRadiusPx = 2 },

                        new SbsComponent { Type = "Text", Name = "LblPlaceBirth", XMm = 104, YMm = 46, WidthMm = 28, HeightMm = 7, Text = "Place Of birth :", FontSizePt = 9, Color = "#334155" },
                        new SbsComponent { Type = "Input", Name = "InpPlaceBirth", XMm = 134, YMm = 45, WidthMm = 56, HeightMm = 7.5, Text = "New York", BorderWidthPx = 1, BorderColor = "#cbd5e1", CornerRadiusPx = 2 },

                        new SbsComponent { Type = "Text", Name = "LblNationality", XMm = 104, YMm = 56, WidthMm = 28, HeightMm = 7, Text = "Nationality :", FontSizePt = 9, Color = "#334155" },
                        new SbsComponent { Type = "Input", Name = "InpNationality", XMm = 134, YMm = 55, WidthMm = 56, HeightMm = 7.5, Text = "American", BorderWidthPx = 1, BorderColor = "#cbd5e1", CornerRadiusPx = 2 },

                        new SbsComponent { Type = "Text", Name = "LblDomicile", XMm = 104, YMm = 66, WidthMm = 28, HeightMm = 7, Text = "Domicile :", FontSizePt = 9, Color = "#334155" },
                        new SbsComponent { Type = "Input", Name = "InpDomicile", XMm = 134, YMm = 65, WidthMm = 56, HeightMm = 7.5, Text = "Brooklyn", BorderWidthPx = 1, BorderColor = "#cbd5e1", CornerRadiusPx = 2 },

                        new SbsComponent { Type = "Text", Name = "LblPostCode", XMm = 104, YMm = 76, WidthMm = 28, HeightMm = 7, Text = "Post Code :", FontSizePt = 9, Color = "#334155" },
                        new SbsComponent { Type = "Input", Name = "InpPostCode", XMm = 134, YMm = 75, WidthMm = 56, HeightMm = 7.5, Text = "11201", BorderWidthPx = 1, BorderColor = "#cbd5e1", CornerRadiusPx = 2 },

                        new SbsComponent { Type = "Text", Name = "LblPhone", XMm = 104, YMm = 86, WidthMm = 28, HeightMm = 7, Text = "Phone :", FontSizePt = 9, Color = "#334155" },
                        new SbsComponent { Type = "Input", Name = "InpPhone", XMm = 134, YMm = 85, WidthMm = 56, HeightMm = 7.5, Text = "+1 (555) 234-5678", BorderWidthPx = 1, BorderColor = "#cbd5e1", CornerRadiusPx = 2 },

                        // Section 2: ADDRESS
                        new SbsComponent
                        {
                            Type = "Shape", Name = "Sec2Bar",
                            XMm = 18, YMm = 110, WidthMm = 2.5, HeightMm = 7,
                            BackgroundColor = "#008b6b", BorderWidthPx = 0
                        },
                        new SbsComponent
                        {
                            Type = "Text", Name = "Sec2Title",
                            XMm = 22, YMm = 110, WidthMm = 168, HeightMm = 7,
                            Text = "ADDRESS",
                            FontFamily = "Segoe UI, Inter, sans-serif",
                            FontSizePt = 10.5, Bold = true, Color = "#0e2b4f"
                        },

                        new SbsComponent { Type = "Text", Name = "LblPresentAddress", XMm = 18, YMm = 122, WidthMm = 28, HeightMm = 7, Text = "Present Address :", FontSizePt = 9, Color = "#334155" },
                        new SbsComponent { Type = "Input", Name = "InpPresentAddress", XMm = 48, YMm = 121, WidthMm = 142, HeightMm = 16, Text = "450 Lexington Ave, Suite 1400, New York, NY 10017", BorderWidthPx = 1, BorderColor = "#cbd5e1", CornerRadiusPx = 2, WordWrap = true },

                        // Confirmation & Signature & Barcode
                        new SbsComponent
                        {
                            Type = "CheckBox", Name = "ChkAgree",
                            XMm = 18, YMm = 144, WidthMm = 172, HeightMm = 7,
                            Text = "I confirm that all provided information is accurate and complete.",
                            Checked = true, FontSizePt = 8.5, Color = "#334155"
                        },
                        new SbsComponent
                        {
                            Type = "Barcode", Name = "FormBarcode",
                            XMm = 18, YMm = 156, WidthMm = 55, HeightMm = 14,
                            Text = "SBSREPORT-PDF-FORM-2026", BarcodeFormat = "Code128"
                        },
                        new SbsComponent
                        {
                            Type = "Signature", Name = "ApplicantSignature",
                            XMm = 135, YMm = 154, WidthMm = 55, HeightMm = 16,
                            Text = "Applicant Signature", BorderWidthPx = 1, BorderColor = "#cbd5e1", BorderStyle = "Dashed", CornerRadiusPx = 2, BackgroundColor = "#f8fafc"
                        }
                    }
                }
            }
        };
    }

    private static SbsReportDocument CreateMedicalReportSample()
    {
        return new SbsReportDocument
        {
            Version = "1.0",
            Name = "HastaMuayeneRaporu",
            Description = "Sobis HBYS Hasta Muayene ve Tedavi Raporu (.sbs)",
            Page = new SbsPageSettings
            {
                PaperSize = "A4",
                Orientation = "Portrait",
                WidthMm = 210,
                HeightMm = 297,
                MarginTopMm = 10,
                MarginRightMm = 10,
                MarginBottomMm = 10,
                MarginLeftMm = 10,
                Watermark = new SbsWatermark
                {
                    Enabled = false,
                    Text = "SOBİS HBYS",
                    Angle = -45,
                    Opacity = 0.10,
                    FontSizePt = 68,
                    Color = "#94a3b8"
                }
            },
            Bands = new()
            {
                new SbsBand
                {
                    Type = "ReportTitle",
                    Name = "HastaneBasligi",
                    HeightMm = 28,
                    BackgroundColor = "#f8fafc",
                    Components = new()
                    {
                        new SbsComponent { Type = "Text", Name = "HospTitle", XMm = 0, YMm = 2, WidthMm = 140, HeightMm = 8, Text = "T.C. SAĞLIK BAKANLIĞI — SOBİS EĞİTİM VE ARAŞTIRMA HASTANESİ", FontSizePt = 11, Bold = true, Color = "#0f172a" },
                        new SbsComponent { Type = "Text", Name = "DocTitle", XMm = 0, YMm = 10, WidthMm = 140, HeightMm = 7, Text = "HASTA MUAYENE VE TEDAVİ BİLGİLENDİRME RAPORU", FontSizePt = 10, Bold = true, Color = "#0d6b62" },
                        new SbsComponent { Type = "Barcode", Name = "HospQr", XMm = 152, YMm = 2, WidthMm = 38, HeightMm = 24, Text = "https://sobis.saglik.gov.tr/hbys/rapor/2026-994812", BarcodeFormat = "QR" }
                    }
                },
                new SbsBand
                {
                    Type = "Data",
                    Name = "HastaKarti",
                    HeightMm = 34,
                    BackgroundColor = "#f1f5f9",
                    Components = new()
                    {
                        new SbsComponent { Type = "Text", Name = "H1", XMm = 3, YMm = 3, WidthMm = 30, HeightMm = 6, Text = "T.C. Kimlik No:", Bold = true, FontSizePt = 8.5 },
                        new SbsComponent { Type = "Text", Name = "V1", XMm = 34, YMm = 3, WidthMm = 55, HeightMm = 6, Text = "12345678901", FontSizePt = 8.5 },
                        new SbsComponent { Type = "Text", Name = "H2", XMm = 95, YMm = 3, WidthMm = 30, HeightMm = 6, Text = "Protokol No:", Bold = true, FontSizePt = 8.5 },
                        new SbsComponent { Type = "Text", Name = "V2", XMm = 126, YMm = 3, WidthMm = 60, HeightMm = 6, Text = "PRT-2026-8819", FontSizePt = 8.5 },

                        new SbsComponent { Type = "Text", Name = "H3", XMm = 3, YMm = 10, WidthMm = 30, HeightMm = 6, Text = "Hasta Adı Soyadı:", Bold = true, FontSizePt = 8.5 },
                        new SbsComponent { Type = "Text", Name = "V3", XMm = 34, YMm = 10, WidthMm = 55, HeightMm = 6, Text = "Ahmet Yılmaz", Bold = true, FontSizePt = 9, Color = "#0f172a" },
                        new SbsComponent { Type = "Text", Name = "H4", XMm = 95, YMm = 10, WidthMm = 30, HeightMm = 6, Text = "Muayene Tarihi:", Bold = true, FontSizePt = 8.5 },
                        new SbsComponent { Type = "Text", Name = "V4", XMm = 126, YMm = 10, WidthMm = 60, HeightMm = 6, Text = "{Today} 14:35", FontSizePt = 8.5 },

                        new SbsComponent { Type = "Text", Name = "H5", XMm = 3, YMm = 17, WidthMm = 30, HeightMm = 6, Text = "Poliklinik / Servis:", Bold = true, FontSizePt = 8.5 },
                        new SbsComponent { Type = "Text", Name = "V5", XMm = 34, YMm = 17, WidthMm = 55, HeightMm = 6, Text = "Kardiyoloji Polikliniği", FontSizePt = 8.5 },
                        new SbsComponent { Type = "Text", Name = "H6", XMm = 95, YMm = 17, WidthMm = 30, HeightMm = 6, Text = "Tabip / Uzman:", Bold = true, FontSizePt = 8.5 },
                        new SbsComponent { Type = "Text", Name = "V6", XMm = 126, YMm = 17, WidthMm = 60, HeightMm = 6, Text = "Uzm. Dr. Mehmet Özkan", FontSizePt = 8.5 }
                    }
                },
                new SbsBand
                {
                    Type = "Data",
                    Name = "VitalBulgular",
                    HeightMm = 28,
                    BackgroundColor = "#ffffff",
                    Components = new()
                    {
                        new SbsComponent { Type = "Text", Name = "VitTitle", XMm = 0, YMm = 2, WidthMm = 190, HeightMm = 6, Text = "VİTAL BULGULAR & KLİNİK ÖLÇÜMLER", Bold = true, FontSizePt = 9, Color = "#0f172a", BorderSides = "Bottom", BorderWidthPx = 1, BorderColor = "#0d6b62" },
                        new SbsComponent { Type = "Text", Name = "Vit1", XMm = 2, YMm = 10, WidthMm = 45, HeightMm = 6, Text = "Tansiyon: 125/80 mmHg", FontSizePt = 8.5 },
                        new SbsComponent { Type = "Text", Name = "Vit2", XMm = 50, YMm = 10, WidthMm = 40, HeightMm = 6, Text = "Nabız: 74 /dk", FontSizePt = 8.5 },
                        new SbsComponent { Type = "Text", Name = "Vit3", XMm = 95, YMm = 10, WidthMm = 40, HeightMm = 6, Text = "Ateş: 36.6 °C", FontSizePt = 8.5 },
                        new SbsComponent { Type = "Text", Name = "Vit4", XMm = 140, YMm = 10, WidthMm = 45, HeightMm = 6, Text = "SpO2: %98", FontSizePt = 8.5 }
                    }
                },
                new SbsBand
                {
                    Type = "Data",
                    Name = "TaniVeTedavi",
                    HeightMm = 80,
                    BackgroundColor = "#ffffff",
                    Components = new()
                    {
                        new SbsComponent { Type = "Text", Name = "TaniTitle", XMm = 0, YMm = 2, WidthMm = 190, HeightMm = 6, Text = "ANAMNEZ, TANI (ICD-10) VE REÇETE EDİLEN İLAÇLAR", Bold = true, FontSizePt = 9, Color = "#0f172a", BorderSides = "Bottom", BorderWidthPx = 1, BorderColor = "#0d6b62" },
                        new SbsComponent { Type = "Text", Name = "AnamnezText", XMm = 2, YMm = 10, WidthMm = 186, HeightMm = 14, Text = "Şikayet: Eforla gelen nefes darlığı ve göğüste baskı hissi. EKG ritmi sinüs taşikardisi. Koroner anjiyografi kontrolü planlandı.", FontSizePt = 8.5, WordWrap = true },

                        new SbsComponent { Type = "Text", Name = "Icd1", XMm = 2, YMm = 28, WidthMm = 186, HeightMm = 6, Text = "• I10 - Esansiyel (Primer) Hipertansiyon", Bold = true, FontSizePt = 8.5, Color = "#1e40af" },
                        new SbsComponent { Type = "Text", Name = "Icd2", XMm = 2, YMm = 35, WidthMm = 186, HeightMm = 6, Text = "• E78.0 - Saf Hiperkolesterolemi", Bold = true, FontSizePt = 8.5, Color = "#1e40af" },

                        new SbsComponent { Type = "Text", Name = "ReceteText", XMm = 2, YMm = 44, WidthMm = 186, HeightMm = 12, Text = "1. BELOC ZOK 50 MG 20 TB (Günde 1x1, sabah aç)\n2. ATOR 20 MG 30 TB (Günde 1x1, akşam tok)", FontSizePt = 8.5, WordWrap = true },

                        new SbsComponent { Type = "Signature", Name = "DoktorKasesi", XMm = 130, YMm = 60, WidthMm = 58, HeightMm = 18, Text = "Uzm. Dr. Mehmet Özkan\nDip. No: 78945 Kase/İmza", BorderWidthPx = 1, BorderStyle = "Dashed", BorderColor = "#94a3b8", CornerRadiusPx = 2, BackgroundColor = "#f8fafc", FontSizePt = 8, HAlign = "center" }
                    }
                }
            }
        };
    }

    private static SbsReportDocument CreateSalesInvoiceSample()
    {
        return new SbsReportDocument
        {
            Version = "1.0",
            Name = "SatisFaturasi",
            Description = "E-Arşiv & Ticari Satış Faturası Örneği (.sbs)",
            Page = new SbsPageSettings
            {
                PaperSize = "A4",
                Orientation = "Portrait",
                WidthMm = 210,
                HeightMm = 297,
                MarginTopMm = 10,
                MarginRightMm = 10,
                MarginBottomMm = 10,
                MarginLeftMm = 10
            },
            Bands = new()
            {
                new SbsBand
                {
                    Type = "ReportTitle",
                    Name = "FaturaBaslik",
                    HeightMm = 36,
                    BackgroundColor = "#ffffff",
                    Components = new()
                    {
                        new SbsComponent { Type = "Text", Name = "CorpName", XMm = 0, YMm = 2, WidthMm = 110, HeightMm = 8, Text = "SOBİS BİLİŞİM VE SAĞLIK TEKNOLOJİLERİ A.Ş.", Bold = true, FontSizePt = 11, Color = "#0f172a" },
                        new SbsComponent { Type = "Text", Name = "CorpAddr", XMm = 0, YMm = 10, WidthMm = 110, HeightMm = 14, Text = "Bilişim Vadisi Gebze / Kocaeli\nVergi Dairesi: İlyasbey | VKN: 7810482910\nMersis: 0781048291000014", FontSizePt = 8, Color = "#475569", WordWrap = true },
                        new SbsComponent { Type = "Text", Name = "InvPill", XMm = 130, YMm = 2, WidthMm = 60, HeightMm = 8, Text = "e-ARŞİV FATURA", Bold = true, FontSizePt = 12, HAlign = "right", Color = "#0d6b62" },
                        new SbsComponent { Type = "Text", Name = "InvDetails", XMm = 130, YMm = 11, WidthMm = 60, HeightMm = 18, Text = "Fatura No: FTR-2026-00891\nFatura Tarihi: {Today}\nDüzenleme Zamanı: {Now}\nVade Tarihi: 30 Gün", FontSizePt = 8, HAlign = "right", Color = "#334155", WordWrap = true }
                    }
                },
                new SbsBand
                {
                    Type = "Header",
                    Name = "KalemBasliklari",
                    HeightMm = 8,
                    BackgroundColor = "#0d6b62",
                    Components = new()
                    {
                        new SbsComponent { Type = "Text", Name = "TH1", XMm = 0, YMm = 0, WidthMm = 14, HeightMm = 8, Text = "Sıra", Bold = true, FontSizePt = 8.5, Color = "#ffffff", HAlign = "center" },
                        new SbsComponent { Type = "Text", Name = "TH2", XMm = 14, YMm = 0, WidthMm = 86, HeightMm = 8, Text = "Mal / Hizmet Açıklaması", Bold = true, FontSizePt = 8.5, Color = "#ffffff" },
                        new SbsComponent { Type = "Text", Name = "TH3", XMm = 100, YMm = 0, WidthMm = 22, HeightMm = 8, Text = "Miktar", Bold = true, FontSizePt = 8.5, Color = "#ffffff", HAlign = "right" },
                        new SbsComponent { Type = "Text", Name = "TH4", XMm = 122, YMm = 0, WidthMm = 32, HeightMm = 8, Text = "Birim Fiyat", Bold = true, FontSizePt = 8.5, Color = "#ffffff", HAlign = "right" },
                        new SbsComponent { Type = "Text", Name = "TH5", XMm = 154, YMm = 0, WidthMm = 36, HeightMm = 8, Text = "Toplam (TL)", Bold = true, FontSizePt = 8.5, Color = "#ffffff", HAlign = "right" }
                    }
                },
                new SbsBand
                {
                    Type = "Data",
                    Name = "FaturaKalemleri",
                    HeightMm = 24,
                    BackgroundColor = "#ffffff",
                    Components = new()
                    {
                        new SbsComponent { Type = "Text", Name = "R1_1", XMm = 0, YMm = 0, WidthMm = 14, HeightMm = 8, Text = "1", FontSizePt = 8.5, HAlign = "center", BorderSides = "Bottom", BorderWidthPx = 1, BorderColor = "#e2e8f0" },
                        new SbsComponent { Type = "Text", Name = "R1_2", XMm = 14, YMm = 0, WidthMm = 86, HeightMm = 8, Text = "Sobis HBYS Sunucu Lisansı (Yıllık)", FontSizePt = 8.5, BorderSides = "Bottom", BorderWidthPx = 1, BorderColor = "#e2e8f0" },
                        new SbsComponent { Type = "Text", Name = "R1_3", XMm = 100, YMm = 0, WidthMm = 22, HeightMm = 8, Text = "1 Adet", FontSizePt = 8.5, HAlign = "right", BorderSides = "Bottom", BorderWidthPx = 1, BorderColor = "#e2e8f0" },
                        new SbsComponent { Type = "Text", Name = "R1_4", XMm = 122, YMm = 0, WidthMm = 32, HeightMm = 8, Text = "85.000,00 ₺", FontSizePt = 8.5, HAlign = "right", BorderSides = "Bottom", BorderWidthPx = 1, BorderColor = "#e2e8f0" },
                        new SbsComponent { Type = "Text", Name = "R1_5", XMm = 154, YMm = 0, WidthMm = 36, HeightMm = 8, Text = "85.000,00 ₺", Bold = true, FontSizePt = 8.5, HAlign = "right", BorderSides = "Bottom", BorderWidthPx = 1, BorderColor = "#e2e8f0" },

                        new SbsComponent { Type = "Text", Name = "R2_1", XMm = 0, YMm = 8, WidthMm = 14, HeightMm = 8, Text = "2", FontSizePt = 8.5, HAlign = "center", BorderSides = "Bottom", BorderWidthPx = 1, BorderColor = "#e2e8f0" },
                        new SbsComponent { Type = "Text", Name = "R2_2", XMm = 14, YMm = 8, WidthMm = 86, HeightMm = 8, Text = "SbsReport Web Raporlama Motoru Entegrasyonu", FontSizePt = 8.5, BorderSides = "Bottom", BorderWidthPx = 1, BorderColor = "#e2e8f0" },
                        new SbsComponent { Type = "Text", Name = "R2_3", XMm = 100, YMm = 8, WidthMm = 22, HeightMm = 8, Text = "1 Paket", FontSizePt = 8.5, HAlign = "right", BorderSides = "Bottom", BorderWidthPx = 1, BorderColor = "#e2e8f0" },
                        new SbsComponent { Type = "Text", Name = "R2_4", XMm = 122, YMm = 8, WidthMm = 32, HeightMm = 8, Text = "24.500,00 ₺", FontSizePt = 8.5, HAlign = "right", BorderSides = "Bottom", BorderWidthPx = 1, BorderColor = "#e2e8f0" },
                        new SbsComponent { Type = "Text", Name = "R2_5", XMm = 154, YMm = 8, WidthMm = 36, HeightMm = 8, Text = "24.500,00 ₺", Bold = true, FontSizePt = 8.5, HAlign = "right", BorderSides = "Bottom", BorderWidthPx = 1, BorderColor = "#e2e8f0" }
                    }
                },
                new SbsBand
                {
                    Type = "ReportSummary",
                    Name = "FaturaToplami",
                    HeightMm = 45,
                    BackgroundColor = "#f8fafc",
                    Components = new()
                    {
                        new SbsComponent { Type = "Barcode", Name = "FtrQr", XMm = 5, YMm = 5, WidthMm = 30, HeightMm = 30, Text = "FTR-2026-00891|SOBIS|131400.00", BarcodeFormat = "QR" },
                        new SbsComponent { Type = "Text", Name = "TotSubLbl", XMm = 110, YMm = 4, WidthMm = 40, HeightMm = 6, Text = "Ara Toplam:", FontSizePt = 8.5, HAlign = "right" },
                        new SbsComponent { Type = "Text", Name = "TotSubVal", XMm = 152, YMm = 4, WidthMm = 38, HeightMm = 6, Text = "109.500,00 ₺", FontSizePt = 8.5, HAlign = "right" },

                        new SbsComponent { Type = "Text", Name = "TotVatLbl", XMm = 110, YMm = 11, WidthMm = 40, HeightMm = 6, Text = "KDV (%20):", FontSizePt = 8.5, HAlign = "right" },
                        new SbsComponent { Type = "Text", Name = "TotVatVal", XMm = 152, YMm = 11, WidthMm = 38, HeightMm = 6, Text = "21.900,00 ₺", FontSizePt = 8.5, HAlign = "right" },

                        new SbsComponent { Type = "Text", Name = "TotGrandLbl", XMm = 110, YMm = 19, WidthMm = 40, HeightMm = 8, Text = "GENEL TOPLAM:", Bold = true, FontSizePt = 10, Color = "#0d6b62", HAlign = "right" },
                        new SbsComponent { Type = "Text", Name = "TotGrandVal", XMm = 152, YMm = 19, WidthMm = 38, HeightMm = 8, Text = "131.400,00 ₺", Bold = true, FontSizePt = 11, Color = "#0d6b62", HAlign = "right" }
                    }
                }
            }
        };
    }

    private static SbsReportDocument CreateProductStockSample()
    {
        return new SbsReportDocument
        {
            Version = "1.0",
            Name = "UrunStokRaporu",
            Description = "Örnek SbsReport Ürün & Stok Listesi (.sbs)",
            Page = new SbsPageSettings
            {
                PaperSize = "A4",
                Orientation = "Portrait",
                WidthMm = 210,
                HeightMm = 297,
                MarginTopMm = 12,
                MarginRightMm = 12,
                MarginBottomMm = 12,
                MarginLeftMm = 12
            },
            DataSources = new()
            {
                new SbsDataSource
                {
                    Name = "Products",
                    Alias = "Ürünler",
                    Type = "demo",
                    Columns = new()
                    {
                        new() { Name = "ProductID", DataType = "number" },
                        new() { Name = "ProductName", DataType = "string" },
                        new() { Name = "CategoryName", DataType = "string" },
                        new() { Name = "UnitPrice", DataType = "number" },
                        new() { Name = "UnitsInStock", DataType = "number" }
                    }
                }
            },
            Bands = new()
            {
                new SbsBand
                {
                    Type = "ReportTitle",
                    Name = "RaporBasligi",
                    HeightMm = 22,
                    BackgroundColor = "#0f172a",
                    Components = new()
                    {
                        new SbsComponent
                        {
                            Type = "Text", Name = "TxtBaslik",
                            XMm = 4, YMm = 3, WidthMm = 120, HeightMm = 9,
                            Text = "SOBISREPORT — ÜRÜN STOK RAPORU",
                            FontSizePt = 14, Bold = true, Color = "#ffffff"
                        },
                        new SbsComponent
                        {
                            Type = "Text", Name = "TxtAltBaslik",
                            XMm = 4, YMm = 12, WidthMm = 120, HeightMm = 6,
                            Text = "Modern .sbs Raporlama Altyapısı",
                            FontSizePt = 9, Color = "#94a3b8"
                        },
                        new SbsComponent
                        {
                            Type = "Text", Name = "TxtTarih",
                            XMm = 135, YMm = 6, WidthMm = 47, HeightMm = 8,
                            Text = "Tarih: {Today}",
                            FontSizePt = 9, Color = "#e2e8f0", HAlign = "right"
                        }
                    }
                },
                new SbsBand
                {
                    Type = "Header",
                    Name = "TabloBaslik",
                    HeightMm = 9,
                    BackgroundColor = "#e2e8f0",
                    Components = new()
                    {
                        new SbsComponent { Type = "Text", Name = "H1", XMm = 0, YMm = 0, WidthMm = 16, HeightMm = 9, Text = "#", Bold = true, FontSizePt = 9, HAlign = "center", BorderWidthPx = 1, BorderColor = "#cbd5e1" },
                        new SbsComponent { Type = "Text", Name = "H2", XMm = 16, YMm = 0, WidthMm = 76, HeightMm = 9, Text = "Ürün Adı", Bold = true, FontSizePt = 9, BorderWidthPx = 1, BorderColor = "#cbd5e1" },
                        new SbsComponent { Type = "Text", Name = "H3", XMm = 92, YMm = 0, WidthMm = 40, HeightMm = 9, Text = "Kategori", Bold = true, FontSizePt = 9, BorderWidthPx = 1, BorderColor = "#cbd5e1" },
                        new SbsComponent { Type = "Text", Name = "H4", XMm = 132, YMm = 0, WidthMm = 30, HeightMm = 9, Text = "Birim Fiyat", Bold = true, FontSizePt = 9, HAlign = "right", BorderWidthPx = 1, BorderColor = "#cbd5e1" },
                        new SbsComponent { Type = "Text", Name = "H5", XMm = 162, YMm = 0, WidthMm = 24, HeightMm = 9, Text = "Stok", Bold = true, FontSizePt = 9, HAlign = "right", BorderWidthPx = 1, BorderColor = "#cbd5e1" }
                    }
                },
                new SbsBand
                {
                    Type = "Data",
                    Name = "UrunSatirlari",
                    HeightMm = 8,
                    DataSource = "Products",
                    Components = new()
                    {
                        new SbsComponent { Type = "Text", Name = "D1", XMm = 0, YMm = 0, WidthMm = 16, HeightMm = 8, Text = "{Line}", FontSizePt = 9, HAlign = "center", BorderWidthPx = 1, BorderColor = "#e2e8f0" },
                        new SbsComponent { Type = "Text", Name = "D2", XMm = 16, YMm = 0, WidthMm = 76, HeightMm = 8, Text = "{Products.ProductName}", FontSizePt = 9, BorderWidthPx = 1, BorderColor = "#e2e8f0" },
                        new SbsComponent { Type = "Text", Name = "D3", XMm = 92, YMm = 0, WidthMm = 40, HeightMm = 8, Text = "{Products.CategoryName}", FontSizePt = 9, Color = "#475569", BorderWidthPx = 1, BorderColor = "#e2e8f0" },
                        new SbsComponent { Type = "Text", Name = "D4", XMm = 132, YMm = 0, WidthMm = 30, HeightMm = 8, Text = "{Products.UnitPrice} ₺", FontSizePt = 9, HAlign = "right", Format = "N2", BorderWidthPx = 1, BorderColor = "#e2e8f0" },
                        new SbsComponent { Type = "Text", Name = "D5", XMm = 162, YMm = 0, WidthMm = 24, HeightMm = 8, Text = "{Products.UnitsInStock}", FontSizePt = 9, HAlign = "right", Format = "N0", BorderWidthPx = 1, BorderColor = "#e2e8f0" }
                    }
                },
                new SbsBand
                {
                    Type = "ReportSummary",
                    Name = "RaporOzeti",
                    HeightMm = 11,
                    BackgroundColor = "#f8fafc",
                    Components = new()
                    {
                        new SbsComponent { Type = "Text", Name = "S1", XMm = 0, YMm = 1, WidthMm = 132, HeightMm = 9, Text = "Toplam {Count()} kalem ürün listelendi", Bold = true, FontSizePt = 9.5, Color = "#0f172a" },
                        new SbsComponent { Type = "Text", Name = "S2", XMm = 132, YMm = 1, WidthMm = 30, HeightMm = 9, Text = "{Sum(UnitPrice)} ₺", Bold = true, FontSizePt = 9.5, HAlign = "right", Format = "N2", Color = "#2563eb" },
                        new SbsComponent { Type = "Text", Name = "S3", XMm = 162, YMm = 1, WidthMm = 24, HeightMm = 9, Text = "{Sum(UnitsInStock)}", Bold = true, FontSizePt = 9.5, HAlign = "right", Format = "N0", Color = "#0f172a" }
                    }
                },
                new SbsBand
                {
                    Type = "PageFooter",
                    Name = "SayfaAltbilgisi",
                    HeightMm = 8,
                    Components = new()
                    {
                        new SbsComponent { Type = "Text", Name = "PF1", XMm = 0, YMm = 0, WidthMm = 100, HeightMm = 7, Text = "SbsReport Engine v1.0 (.sbs)", FontSizePt = 8, Color = "#64748b" },
                        new SbsComponent { Type = "Text", Name = "PF2", XMm = 130, YMm = 0, WidthMm = 56, HeightMm = 7, Text = "Sayfa {PageNumber} / {TotalPageCount}", FontSizePt = 8, Color = "#64748b", HAlign = "right" }
                    }
                }
            }
        };
    }

    private static SbsReportDocument CreateMasterAntetSample()
    {
        return new SbsReportDocument
        {
            Version = "1.0",
            Name = "SbsMasterAntet",
            Description = "Kurumsal Sabit Header & Footer Master Şablonu",
            IsMasterTemplate = true,
            Page = new SbsPageSettings
            {
                PaperSize = "A4",
                Orientation = "Portrait",
                WidthMm = 210,
                HeightMm = 297,
                MarginTopMm = 10,
                MarginRightMm = 10,
                MarginBottomMm = 10,
                MarginLeftMm = 10,
                GridSizeMm = 2,
                ShowGrid = true,
                SnapToGrid = true
            },
            Bands = new()
            {
                new SbsBand
                {
                    Type = "PageHeader",
                    Name = "MasterPageHeader",
                    HeightMm = 22,
                    BackgroundColor = "#0f2744",
                    Components = new()
                    {
                        new SbsComponent { Type = "Text", Name = "MstLogo", XMm = 4, YMm = 3, WidthMm = 115, HeightMm = 9, Text = "✦ SOBİS KURUMSAL RAPOR SİSTEMİ", Bold = true, FontSizePt = 13, Color = "#ffffff" },
                        new SbsComponent { Type = "Text", Name = "MstSub", XMm = 4, YMm = 12, WidthMm = 120, HeightMm = 6, Text = "Merkez Yönetim & HBYS — Standart Kurumsal Antet", FontSizePt = 8.5, Color = "#cbd5e1" },
                        new SbsComponent { Type = "Text", Name = "MstDate", XMm = 140, YMm = 4, WidthMm = 46, HeightMm = 6, Text = "Tarih: {Today}", FontSizePt = 8.5, Color = "#f8a045", HAlign = "right", Bold = true },
                        new SbsComponent { Type = "Text", Name = "MstDocNo", XMm = 140, YMm = 11, WidthMm = 46, HeightMm = 6, Text = "Form Kodu: SBS-MST-01", FontSizePt = 8, Color = "#e2e8f0", HAlign = "right" },
                        new SbsComponent { Type = "Shape", Name = "MstAccent", XMm = 0, YMm = 20, WidthMm = 190, HeightMm = 2, BackgroundColor = "#f8a045", BorderWidthPx = 0 }
                    }
                },
                new SbsBand
                {
                    Type = "PageFooter",
                    Name = "MasterPageFooter",
                    HeightMm = 11,
                    BackgroundColor = "#f8fafc",
                    Components = new()
                    {
                        new SbsComponent { Type = "Line", Name = "MstFLine", XMm = 0, YMm = 0, WidthMm = 190, HeightMm = 1, Color = "#0d6b62", BorderWidthPx = 1.5 },
                        new SbsComponent { Type = "Text", Name = "MstLegal", XMm = 2, YMm = 2, WidthMm = 125, HeightMm = 7, Text = "Bu belge SbsReport Master Şablonu ile elektronik olarak üretilmiştir. | Basım: {Now}", FontSizePt = 7.5, Color = "#64748b" },
                        new SbsComponent { Type = "Text", Name = "MstPageNo", XMm = 135, YMm = 2, WidthMm = 53, HeightMm = 7, Text = "Sayfa {PageNumber} / {TotalPageCount}", FontSizePt = 8.5, Bold = true, Color = "#0f2744", HAlign = "right" }
                    }
                }
            }
        };
    }
}
