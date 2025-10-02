using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class FileCleanupService : BackgroundService
{
    private readonly ILogger<FileCleanupService> _logger;
    private readonly string _webRootPath;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(10); // Check every 10 minutes
    private readonly TimeSpan _fileAgeLimit = TimeSpan.FromMinutes(30);   // Delete files older than 30 minutes

    // List of directories where temporary files are stored
    private readonly string[] _targetDirectories = { "merged", "compressed", "split", "pdf", "protected" };

    public FileCleanupService(ILogger<FileCleanupService> logger)
    {
        _logger = logger;
        // In a real application, you'd inject IWebHostEnvironment or IHostEnvironment 
        // to get the WebRootPath. For simplicity, we'll construct it here.
        // NOTE: This assumes the service is running from the application's root directory.
        _webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("File Cleanup Service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("File Cleanup Service running at: {time}", DateTimeOffset.Now);
            CleanupFiles();

            try
            {
                // Wait for the next interval
                await Task.Delay(_cleanupInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // This is expected when the host shuts down
                break;
            }
        }

        _logger.LogInformation("File Cleanup Service is stopping.");
    }

    private void CleanupFiles()
    {
        var now = DateTime.UtcNow;

        foreach (var dirName in _targetDirectories)
        {
            var targetDir = Path.Combine(_webRootPath, dirName);

            if (!Directory.Exists(targetDir)) continue;

            try
            {
                var files = Directory.GetFiles(targetDir);
                foreach (var filePath in files)
                {
                    var fileInfo = new FileInfo(filePath);
                    
                    // Delete if file is older than the age limit
                    if ((now - fileInfo.LastWriteTimeUtc) > _fileAgeLimit)
                    {
                        try
                        {
                            File.Delete(filePath);
                            _logger.LogInformation("Deleted old file: {FilePath}", filePath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Could not delete file: {FilePath}. It might be in use.", filePath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accessing directory {Directory}", targetDir);
            }
        }
    }
}