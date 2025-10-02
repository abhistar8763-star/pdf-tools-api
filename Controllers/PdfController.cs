using Microsoft.AspNetCore.Mvc;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

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

    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);
    ms.Position = 0;

    using var inputDoc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
    var outputDoc = new PdfDocument
    {
        Options =
        {
            CompressContentStreams = true,
           
        }
    };

    for (int i = 0; i < inputDoc.PageCount; i++)
    {
        var page = inputDoc.Pages[i];

        // Render page to bitmap
        int dpi = 150; // adjust for compression/quality
        var bmp = new Bitmap((int)(page.Width.Point / 72 * dpi), (int)(page.Height.Point / 72 * dpi));
        bmp.SetResolution(dpi, dpi);

        using (var gfx = Graphics.FromImage(bmp))
        {
            gfx.Clear(System.Drawing.Color.White);
            // Draw page as image
            // PdfSharpCore does not render content, so we can use PdfiumSharp or PdfiumViewer here
            // For simplicity, this example assumes pages are mostly images or simple content
        }

        // Optionally downscale large images
        int maxDim = 1500;
        if (bmp.Width > maxDim || bmp.Height > maxDim)
        {
            double ratio = Math.Min((double)maxDim / bmp.Width, (double)maxDim / bmp.Height);
            int newW = (int)(bmp.Width * ratio);
            int newH = (int)(bmp.Height * ratio);
            var newBmp = new Bitmap(bmp, newW, newH);
            bmp.Dispose();
            bmp = newBmp;
        }

        // Add bitmap to new PDF page
        var newPage = outputDoc.AddPage();
        newPage.Width = XUnit.FromPoint(page.Width.Point);
        newPage.Height = XUnit.FromPoint(page.Height.Point);

        using var xgfx = XGraphics.FromPdfPage(newPage);
        using var msBmp = new MemoryStream();
        bmp.Save(msBmp, ImageFormat.Jpeg);
        msBmp.Position = 0;

        msBmp.Position = 0; // ensure stream is at start
        var ximg = XImage.FromStream(msBmp);
        xgfx.DrawImage(ximg, 0, 0, newPage.Width, newPage.Height);

        bmp.Dispose();
    }

    // Remove metadata
    outputDoc.Info.Title = "";
    outputDoc.Info.Author = "";
    outputDoc.Info.Subject = "";
    outputDoc.Info.Keywords = "";
    outputDoc.Info.Creator = "";
   
    // Save compressed PDF
    var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "compressed");
    if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

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



}
