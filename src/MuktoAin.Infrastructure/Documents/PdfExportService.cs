using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Services;

namespace MuktoAin.Infrastructure.Documents;

/// <summary>
/// QuestPDF-based export of finalized (lawyer-approved) documents.
/// Renders <c>ContentFinal ?? ContentDraft</c> on A4 with the Noto Sans Bengali
/// font (Bangla script support) and permanently stamps the bilingual legal
/// disclaimer into every page footer + the last content page (Surface 3 of 3).
/// </summary>
public class PdfExportService : IPdfExporter
{
    public const string BengaliFontName = "Noto Sans Bengali";

    private readonly string _regularFontPath;
    private readonly string _boldFontPath;

    private static bool _fontsRegistered;

    // Internal for tests: allows verifying the gate + content selection without
    // touching QuestPDF's static font registry.
    internal static string SelectContent(GeneratedDocument document)
        => document.ContentFinal ?? document.ContentDraft;

    public PdfExportService(string regularFontPath, string boldFontPath)
    {
        _regularFontPath = regularFontPath;
        _boldFontPath = boldFontPath;
    }

    /// <summary>
    /// One-time registration of the Bangla fonts with QuestPDF's FontManager.
    /// Both weights share the family name embedded in the TTF ("Noto Sans
    /// Bengali"), so auto-detection registers Regular + Bold under one family
    /// and SemiBold/Bold text styles resolve to the 700-weight file.
    /// Also applies the Community license (free under $1M revenue — academic
    /// project). Thread-safe and idempotent; safe to call on every GeneratePdf.
    /// </summary>
    private static void EnsureFontsRegistered(string regularFontPath, string boldFontPath)
    {
        if (_fontsRegistered) return;

        lock (typeof(PdfExportService))
        {
            if (_fontsRegistered) return;

            QuestPDF.Settings.License = LicenseType.Community;

            if (!File.Exists(regularFontPath))
                throw new FileNotFoundException(
                    $"Bangla font not found at '{regularFontPath}'. PDF export requires Noto Sans Bengali.", regularFontPath);
            if (!File.Exists(boldFontPath))
                throw new FileNotFoundException(
                    $"Bangla bold font not found at '{boldFontPath}'. PDF export requires Noto Sans Bengali Bold.", boldFontPath);

            using (var regular = File.OpenRead(regularFontPath))
            {
                FontManager.RegisterFont(regular);
            }
            using (var bold = File.OpenRead(boldFontPath))
            {
                FontManager.RegisterFont(bold);
            }

            _fontsRegistered = true;
        }
    }

    /// <summary>
    /// Generates the PDF for an approved document.
    /// Callers (DocumentService.GetPdfIfApprovedAsync) must gate on
    /// DocumentStatus.Approved — this method does NOT re-check status.
    /// </summary>
    public byte[] GeneratePdf(GeneratedDocument document, Case caseEntity)
    {
        EnsureFontsRegistered(_regularFontPath, _boldFontPath);

        var content = SelectContent(document);
        var title = document.DocumentType.ToString();
        var caseRef = $"Case #{caseEntity.CaseId}";
        var generatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x
                    .FontFamily(BengaliFontName)
                    .FontSize(11));

                page.Header().PaddingBottom(10).BorderBottom(1).BorderColor(Colors.Grey.Lighten1).Row(row =>
                {
                    row.RelativeItem().Text("MuktoAin — মুক্ত আইন")
                        .SemiBold().FontSize(16).FontColor(Colors.Blue.Darken2);
                    row.RelativeItem().AlignRight().Text($"{title}\n{caseRef}")
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Item().Text(content).LineHeight(1.5f);

                    // Disclaimer stamp — Surface 3 of 3 (permanent, in the PDF itself)
                    col.Item().PaddingTop(20)
                        .Border(1).BorderColor(Colors.Red.Darken1)
                        .Padding(10)
                        .Column(disclaimerCol =>
                        {
                            disclaimerCol.Item().Text(Disclaimers.Legal)
                                .FontSize(8.5f).FontColor(Colors.Red.Darken1);
                            disclaimerCol.Item().PaddingTop(4).Text(Disclaimers.LegalBangla)
                                .FontSize(8.5f).FontColor(Colors.Red.Darken1);
                        });
                });

                page.Footer().AlignCenter()
                    .Text(x =>
                    {
                        x.DefaultTextStyle(t => t.FontSize(8).FontColor(Colors.Grey.Darken1));
                        x.Span("Generated by MuktoAin — ");
                        x.Span(generatedAt);
                        x.Span("  |  ");
                        x.Span(Disclaimers.Legal);
                    });
            });
        }).GeneratePdf();
    }
}
