using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

public class GoogleDriveUploader
{
    private readonly DriveService _driveService;
    private readonly string _folderId;

    public GoogleDriveUploader(string jsonCredentials)
    {
        var credential = GoogleCredential.FromJson(jsonCredentials)
            .CreateScoped(DriveService.ScopeConstants.Drive);

        _driveService = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "VideoDownloader"
        });

        // IDE ÍRD A MAPPÁD ID-JÁT
        _folderId = "1Qj8APGbL4L1U2NDTtm_5BgXeFokEJoHH";
    }

    public async Task<string> UploadFileAsync(string filePath, string fileName)
    {
        var fileMetadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = fileName,
            Parents = new[] { _folderId }   // <-- EZ A LÉNYEG
        };

        using var stream = new FileStream(filePath, FileMode.Open);

        var request = _driveService.Files.Create(fileMetadata, stream, "video/mp4");
        request.Fields = "id";
        var result = await request.UploadAsync();

        if (result.Status != Google.Apis.Upload.UploadStatus.Completed)
            throw new Exception("Upload failed");

        var fileId = request.ResponseBody.Id;

        var permission = new Google.Apis.Drive.v3.Data.Permission
        {
            Role = "reader",
            Type = "anyone"
        };

        await _driveService.Permissions.Create(permission, fileId).ExecuteAsync();

        return $"https://drive.google.com/uc?export=download&id={fileId}";
    }
}
