using System.Net;
using Google.Apis.Drive.v3;

public record DownloadRequest(string Url); // 👈 FELHOZVA IDE

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseCors();

var mediaRoot = Path.Combine(app.Environment.ContentRootPath, "media");
if (!Directory.Exists(mediaRoot))
    Directory.CreateDirectory(mediaRoot);

app.MapGet("/", () => "VideoDownloader API running.");

app.MapPost("/api/download", async (DownloadRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Url))
        return Results.BadRequest("Url is required.");

    try
    {
        using var httpClient = new HttpClient();

        var fileName = $"{Guid.NewGuid()}.mp4";
        var filePath = Path.Combine(mediaRoot, fileName);

        var response = await httpClient.GetAsync(request.Url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using (var fs = new FileStream(filePath, FileMode.CreateNew))
        {
            await response.Content.CopyToAsync(fs);
        }

        var uploader = new GoogleDriveUploader(
            Environment.GetEnvironmentVariable("GOOGLE_CREDENTIALS_JSON")
        );

        var driveUrl = await uploader.UploadFileAsync(filePath, fileName);

        File.Delete(filePath);

        return Results.Ok(new { url = driveUrl });
    }
    catch
    {
        return Results.StatusCode(500);
    }
});

app.Run();
