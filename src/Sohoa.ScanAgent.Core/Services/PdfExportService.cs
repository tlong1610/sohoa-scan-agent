using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Sohoa.ScanAgent.Core.Models;

namespace Sohoa.ScanAgent.Core.Services;

/// <summary>
/// Merges processed page images into a single multi-page PDF.
/// </summary>
public static class PdfExportService
{
    /// <summary>
    /// Exports a document's ordered pages to a PDF file at <paramref name="outputPath"/>.
    /// Applies rotation and crop from each page's metadata.
    /// </summary>
    public static void ExportToPdf(
        List<PageMeta> orderedPages,
        string outputPath)
    {
        if (orderedPages.Count == 0)
            throw new InvalidOperationException("No pages to export");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var pdf = new PdfDocument();
        pdf.Info.Title = Path.GetFileNameWithoutExtension(outputPath);

        foreach (var page in orderedPages)
        {
            if (!File.Exists(page.TiffPath))
                throw new FileNotFoundException($"Page file not found: {page.TiffPath}");

            var jpegBytes = ImageProcessingService.GetProcessedJpeg(page.TiffPath, page);

            using var ms = new MemoryStream(jpegBytes);
            using var xImage = XImage.FromStream(ms);

            var pdfPage = pdf.AddPage();
            double dpiH = xImage.HorizontalResolution > 0 ? xImage.HorizontalResolution : 96;
            double dpiV = xImage.VerticalResolution > 0 ? xImage.VerticalResolution : 96;
            pdfPage.Width = XUnit.FromPoint(xImage.PixelWidth * 72.0 / dpiH);
            pdfPage.Height = XUnit.FromPoint(xImage.PixelHeight * 72.0 / dpiV);

            using var gfx = XGraphics.FromPdfPage(pdfPage);
            gfx.DrawImage(xImage, 0, 0, pdfPage.Width.Point, pdfPage.Height.Point);
        }

        pdf.Save(outputPath);
    }
}
