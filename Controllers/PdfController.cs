using Microsoft.AspNetCore.Mvc;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using PdfiumSharp;
using PdfSharpCore.Pdf;
using PdfSharpCore.Drawing;

namespace pdf_tools.Controllers;

[ApiController]
[Route("[controller]")]
public class PdfController : ControllerBase
{
    private static readonly string[] Summaries = new[]
    {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };

    private readonly ILogger<PdfController> _logger;

    public PdfController(ILogger<PdfController> logger)
    {
        _logger = logger;
    }

    [HttpPost("merge")]
public async Task<IActionResult> MergePdf([FromForm] List<IFormFile> files)
{
    if (files == null || files.Count < 2)
        return BadRequest(new { success = false, message = "Please upload at least 2 files" });

    using var outputDocument = new PdfDocument();

    foreach (var file in files)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;

        using var inputDocument = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
        for (int idx = 0; idx < inputDocument.PageCount; idx++)
        {
            PdfPage page = inputDocument.Pages[idx];
            outputDocument.AddPage(page);
        }
    }

    // Save merged PDF in a folder (e.g. wwwroot/merged)
    var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "merged");
    if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

    var fileName = $"merged_{Guid.NewGuid()}.pdf";
    var filePath = Path.Combine(outputDir, fileName);

    outputDocument.Save(filePath);

    // Return JSON with download URL
    var downloadUrl = $"{Request.Scheme}://{Request.Host}/merged/{fileName}";

    return Ok(new
    {
        success = true,
        downloadUrl,
        filename = fileName
    });
}

[HttpPost("compress")]


public async Task<IActionResult> CompressPdfRaster([FromForm] IFormFile file)
{
    if (file == null)
        return BadRequest(new { success = false, message = "Please upload a PDF file" });

    long originalSize = file.Length;

    // Read PDF into memory
    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);
    ms.Position = 0;

    using var pdfDocument = PdfDocument.Load(ms);

    var outputDoc = new PdfDocument();

    int targetDpi = 150; // adjust for compression (lower = smaller file)

    for (int i = 0; i < pdfDocument.PageCount; i++)
    {
        using var page = pdfDocument.Pages[i];
        var size = page.Size;

        // Render page to bitmap
        using var bmp = page.Render((int)(size.Width * targetDpi / 72), (int)(size.Height * targetDpi / 72), true);

        // Optional: downscale if too large
        int maxDim = 1500;
        int newWidth = bmp.Width;
        int newHeight = bmp.Height;
        if (bmp.Width > maxDim || bmp.Height > maxDim)
        {
            double ratio = Math.Min((double)maxDim / bmp.Width, (double)maxDim / bmp.Height);
            newWidth = (int)(bmp.Width * ratio);
            newHeight = (int)(bmp.Height * ratio);
        }

        // Create new PDF page
        var newPage = outputDoc.AddPage();
        newPage.Width = XUnit.FromPoint(size.Width);
        newPage.Height = XUnit.FromPoint(size.Height);

        using var gfx = XGraphics.FromPdfPage(newPage);

        // Draw bitmap into PDF
        using var msBmp = new MemoryStream();
        var resizedBmp = new Bitmap(bmp, newWidth, newHeight);
        resizedBmp.Save(msBmp, System.Drawing.Imaging.ImageFormat.Jpeg);
        msBmp.Position = 0;

        var xImg = XImage.FromStream(msBmp);
        gfx.DrawImage(xImg, 0, 0, newPage.Width, newPage.Height);

        resizedBmp.Dispose();
    }

    // Remove metadata
    outputDoc.Info.Title = "";
    outputDoc.Info.Author = "";
    outputDoc.Info.Subject = "";
    outputDoc.Info.Keywords = "";
    outputDoc.Info.Creator = "";

    // Save compressed PDF
    var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "compressed");
    if (!Directory.Exists(outputDir))
        Directory.CreateDirectory(outputDir);

    var fileName = $"compressed_{Guid.NewGuid()}.pdf";
    var filePath = Path.Combine(outputDir, fileName);
    outputDoc.Save(filePath);

    long compressedSize = new FileInfo(filePath).Length;
    double compressionRatio = Math.Round((double)compressedSize / originalSize, 2);

    var downloadUrl = $"{Request.Scheme}://{Request.Host}/compressed/{fileName}";

    return Ok(new
    {
        success = true,
        downloadUrl,
        filename = fileName,
        originalSize,
        compressedSize,
        compressionRatio
    });
}


[HttpPost("split")]
public async Task<IActionResult> SplitPdf(
    [FromForm] IFormFile file,
    [FromForm] string? pages,              // existing
    [FromForm] string? ranges,             // frontend sends this
    [FromForm] string? selectedPages,      // JSON array string
    [FromForm] string? mode,               // optional (not used yet)
    [FromForm] string? sizeLimitMB         // optional (not used yet)
)
{
    if (file == null)
        return BadRequest(new { success = false, message = "Please upload a PDF file" });

    // 🔹 Normalize pages input
    string? finalPages = null;

    if (!string.IsNullOrWhiteSpace(pages))
    {
        finalPages = pages;
    }
    else if (!string.IsNullOrWhiteSpace(ranges))
    {
        finalPages = ranges;
    }
    else if (!string.IsNullOrWhiteSpace(selectedPages))
    {
        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<List<int>>(selectedPages);
            if (parsed != null && parsed.Any())
                finalPages = string.Join(",", parsed);
        }
        catch
        {
            return BadRequest(new { success = false, message = "Invalid selectedPages format" });
        }
    }

    if (string.IsNullOrWhiteSpace(finalPages))
        return BadRequest(new { success = false, message = "Please provide pages, ranges, or selectedPages" });

    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);
    ms.Position = 0;

    using var inputDocument = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
    var outputDocument = new PdfDocument();

    var pageNumbers = new List<int>();

    try
    {
        var parts = finalPages.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.Contains('-'))
            {
                var range = part.Split('-');
                if (int.TryParse(range[0], out int start) &&
                    int.TryParse(range[1], out int end))
                {
                    if (start > 0 && end >= start)
                    {
                        for (int i = start; i <= end; i++)
                            pageNumbers.Add(i);
                    }
                }
            }
            else if (int.TryParse(part, out int pageNum))
            {
                if (pageNum > 0)
                    pageNumbers.Add(pageNum);
            }
        }
    }
    catch
    {
        return BadRequest(new { success = false, message = "Invalid page range format" });
    }

    if (pageNumbers.Count == 0)
        return BadRequest(new { success = false, message = "No valid pages specified" });

    foreach (var pageNum in pageNumbers.Distinct().OrderBy(x => x))
    {
        if (pageNum <= inputDocument.PageCount)
        {
            outputDocument.AddPage(inputDocument.Pages[pageNum - 1]);
        }
    }

    var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "split");
    if (!Directory.Exists(outputDir))
        Directory.CreateDirectory(outputDir);

    var fileName = $"split_{Guid.NewGuid()}.pdf";
    var filePath = Path.Combine(outputDir, fileName);

    using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
    {
        outputDocument.Save(fs, false);
    }

    var downloadUrl = $"{Request.Scheme}://{Request.Host}/split/{fileName}";

    return Ok(new
    {
        success = true,
        downloadUrl,
        filename = fileName,
        outputPages = 1
    });
}

  [HttpPost("JpgToPdf")]
        public async Task<IActionResult> JpgToPdf([FromForm] List<IFormFile> files, [FromForm] string orientation)
        {
            if (files == null || !files.Any())
                return BadRequest(new { success = false, message = "No images uploaded" });

            if (string.IsNullOrWhiteSpace(orientation) || !(orientation.ToLower() == "portrait" || orientation.ToLower() == "landscape"))
                orientation = "portrait"; // default

            using var pdf = new PdfDocument();

            foreach (var file in files)
            {
                if (file.Length == 0) continue;

                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                ms.Position = 0;

                using var img = XImage.FromStream(ms);
                var page = new PdfPage();

                // Set page orientation
                if (orientation.ToLower() == "landscape")
                {
                    page.Width = XUnit.FromPoint(img.PointWidth > img.PointHeight ? img.PointWidth : img.PointHeight);
                    page.Height = XUnit.FromPoint(img.PointWidth > img.PointHeight ? img.PointHeight : img.PointWidth);
                }
                else
                {
                    page.Width = XUnit.FromPoint(img.PointWidth);
                    page.Height = XUnit.FromPoint(img.PointHeight);
                }

                pdf.AddPage(page);

                using var gfx = XGraphics.FromPdfPage(page);

                // Draw image centered
                var x = (page.Width.Point - img.PointWidth) / 2;
                var y = (page.Height.Point - img.PointHeight) / 2;
                gfx.DrawImage(img, x, y, img.PointWidth, img.PointHeight);
            }

            var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "pdf");
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            var fileName = $"jpgToPdf_{Guid.NewGuid()}.pdf";
            var filePath = Path.Combine(outputDir, fileName);

            pdf.Save(filePath);

            var downloadUrl = $"{Request.Scheme}://{Request.Host}/pdf/{fileName}";

            return Ok(new
            {
                success = true,
                downloadUrl,
                filename = fileName
            });
        }
    

}
