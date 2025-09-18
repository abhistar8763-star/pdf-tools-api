using Microsoft.AspNetCore.Mvc;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
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
public async Task<IActionResult> CompressPdf([FromForm] IFormFile file)
{
    if (file == null)
        return BadRequest(new { success = false, message = "Please upload a PDF file" });

    // Original file size in bytes
    long originalSize = file.Length;

    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);
    ms.Position = 0;

    // Open original PDF
    using var inputDocument = PdfReader.Open(ms, PdfDocumentOpenMode.Import);

    // Create compressed PDF
    using var outputDocument = new PdfDocument();
    outputDocument.Options.CompressContentStreams = true;
    outputDocument.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;

    for (int i = 0; i < inputDocument.PageCount; i++)
    {
        PdfPage page = inputDocument.Pages[i];
        outputDocument.AddPage(page);
    }

    // Save compressed PDF to wwwroot/compressed
    var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "compressed");
    if (!Directory.Exists(outputDir))
        Directory.CreateDirectory(outputDir);

    var fileName = $"compressed_{Guid.NewGuid()}.pdf";
    var filePath = Path.Combine(outputDir, fileName);
    outputDocument.Save(filePath);

    // Compressed file size in bytes
    long compressedSize = new FileInfo(filePath).Length;

    // Compression ratio (e.g., 0.75 means 75% of original size)
    double compressionRatio = Math.Round((double)compressedSize / originalSize, 2);

    // Return JSON response matching CompressPdfResponse interface
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
    [FromForm] string pages // e.g., "1-3,5,7-8"
)
{
    if (file == null)
        return BadRequest(new { success = false, message = "Please upload a PDF file" });

    if (string.IsNullOrWhiteSpace(pages))
        return BadRequest(new { success = false, message = "Please provide pages or ranges" });

    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);
    ms.Position = 0;

    using var inputDocument = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
    var outputDocument = new PdfDocument();

    var pageNumbers = new List<int>();

    try
    {
        var parts = pages.Split(',', StringSplitOptions.RemoveEmptyEntries);
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

    // ✅ Safe save using FileStream (avoids "already saved" issue)
    using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
    {
        outputDocument.Save(fs, false); // false = keep stream open for disposal
    }

    var downloadUrl = $"{Request.Scheme}://{Request.Host}/split/{fileName}";

    return Ok(new
    {
        success = true,
        downloadUrl,
        filename = fileName,
        originalPages = 1,
        outputPages = 1
    });
}


}
