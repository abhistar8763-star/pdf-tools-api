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

     [HttpPost("merge1")]
    public IActionResult MergePdf1([FromForm] List<IFormFile> files)
    {
       return Ok("Hello from PdfController");
    }
}
