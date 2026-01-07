using System.Net;
using Google.Apis.Drive.v3;
using System.Threading.Channels;

var builder = WebApplication.CreateBuilder(args);

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Background queue
builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddHostedService<BackgroundWorker>();

// Task store (in-memory)
builder.Services.AddSingleton<TaskStore>();

var app = builder.Build();
app.UseCors();

// Media folder
var mediaRoot = Path.Combine(app.Environment.ContentRootPath, "media");
if (!Directory.Exists(mediaRoot))
    Directory.CreateDirectory(mediaRoot);

app.MapGet("/", () => "VideoDownloader API running.");

// ----------------------------
// POST /api/download
// ----------------------------
app.MapPost("/api/download", async (DownloadRequest request, 
                                    IBackgroundTaskQueue queue,
                                    TaskStore store) =>
{
    if (string.IsNullOrWhiteSpace(request.Url))
        return Results.BadRequest("Url is required.");

    var taskId = Guid.NewGuid().ToString();
    store.Results[taskId] = "PENDING";

    await queue.QueueBackgroundWorkItemAsync(async token =>
    {
        try
        {
            using var httpClient = new HttpClient();

            var fileName = $"{Guid.NewGuid()}.mp4";
            var filePath = Path.Combine(mediaRoot, fileName);

            var response = await httpClient.GetAsync(request.Url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            await using (var fs = new FileStream(filePath, FileMode.CreateNew))
            {
                await response.Content.CopyToAsync(fs, token);
            }

            var uploader = new GoogleDriveUploader(
                Environment.GetEnvironmentVariable("GOOGLE_CREDENTIALS_JSON")
            );

            var driveUrl = await uploader.UploadFileAsync(filePath, fileName);

            File.Delete(filePath);

            store.Results[taskId] = driveUrl;
        }
        catch (Exception ex)
        {
            store.Results[taskId] = "ERROR: " + ex.Message;
        }
    });

    return Results.Ok(new { taskId });
});

// ----------------------------
// GET /api/status/{taskId}
// ----------------------------
app.MapGet("/api/status/{taskId}", (string taskId, TaskStore store) =>
{
    if (store.Results.TryGetValue(taskId, out var result))
        return Results.Ok(new { status = result });

    return Results.Ok(new { status = "NOT_FOUND" });
});

app.Run();

public record DownloadRequest(string Url);

// ----------------------------
// Background Queue
// ----------------------------
public interface IBackgroundTaskQueue
{
    ValueTask QueueBackgroundWorkItemAsync(Func<CancellationToken, Task> workItem);
    ValueTask<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken);
}

public class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<CancellationToken, Task>> _queue =
        Channel.CreateUnbounded<Func<CancellationToken, Task>>();

    public async ValueTask QueueBackgroundWorkItemAsync(Func<CancellationToken, Task> workItem)
    {
        await _queue.Writer.WriteAsync(workItem);
    }

    public async ValueTask<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }
}

// ----------------------------
// Background Worker
// ----------------------------
public class BackgroundWorker : BackgroundService
{
    private readonly IBackgroundTaskQueue _taskQueue;

    public BackgroundWorker(IBackgroundTaskQueue taskQueue)
    {
        _taskQueue = taskQueue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var workItem = await _taskQueue.DequeueAsync(stoppingToken);
            await workItem(stoppingToken);
        }
    }
}

// ----------------------------
// Task Store
// ----------------------------
public class TaskStore
{
    public Dictionary<string, string> Results { get; } = new();
}
