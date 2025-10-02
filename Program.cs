using Microsoft.Extensions.FileProviders;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// ✅ Add CORS service
// builder.Services.AddCors(options =>
// {
//     options.AddPolicy("AllowReactApp",
//         policy =>
//         {
//             policy.WithOrigins("http://localhost:8080") // your React app origin
//                   .AllowAnyHeader()
//                   .AllowAnyMethod()
//                   .AllowCredentials();
//         });
// });

// Enable CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

//app.UseCors("AllowReactApp");

app.UseCors("AllowAll");

app.UseStaticFiles();

var mergedFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "merged");

// Create the folder if it does not exist
if (!Directory.Exists(mergedFolder))
{
    Directory.CreateDirectory(mergedFolder);
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(mergedFolder),
    RequestPath = "/merged"
});

var compressedFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "compressed");
if (!Directory.Exists(compressedFolder))
{
    Directory.CreateDirectory(compressedFolder);
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(compressedFolder),
    RequestPath = "/compressed"
});

var splitFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "split");
if (!Directory.Exists(splitFolder))
{
    Directory.CreateDirectory(splitFolder);
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(splitFolder),
    RequestPath = "/split"
});

var pdfFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "pdf");
if (!Directory.Exists(pdfFolder))
{
    Directory.CreateDirectory(pdfFolder);
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(pdfFolder),
    RequestPath = "/pdf"
});

app.UseAuthorization();

app.MapControllers();

app.Run();
