using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Win32_to_IntuneUI.Services;

public class IntuneGraphService
{
    private GraphServiceClient? _graphClient;
    private string? _accessToken;

    /// <summary>
    /// Initialize the service with a Client ID, Tenant ID, and Client Secret for app-only authentication
    /// </summary>
    public void InitializeWithClientCredentials(string clientId, string tenantId, string clientSecret)
    {
        var clientSecretCredential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        _graphClient = new GraphServiceClient(clientSecretCredential);
    }

    /// <summary>
    /// Initialize the service with an existing access token
    /// </summary>
    public void InitializeWithAccessToken(string accessToken)
    {
        _accessToken = accessToken;
        
        // We'll use the access token directly in HTTP requests
        // The GraphServiceClient will be used for supported operations only
    }

    /// <summary>
    /// Test the connection to Microsoft Graph
    /// </summary>
    public async Task<(bool Success, string Message)> TestConnectionAsync()
    {
        try
        {
            if (_graphClient == null)
                return (false, "Graph client not initialized. Please configure authentication.");

            // Try to get the organization details as a connection test
            var organization = await _graphClient.Organization.GetAsync();
            
            if (organization?.Value?.Any() == true)
            {
                var orgName = organization.Value.First().DisplayName;
                return (true, $"Successfully connected to {orgName}");
            }

            return (false, "Unable to retrieve organization information");
        }
        catch (Exception ex)
        {
            return (false, $"Connection failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Upload a .intunewin package to Intune
    /// </summary>
    public async Task<(bool Success, string Message, string? AppId)> UploadWin32AppAsync(
        string intunewinPath,
        string displayName,
        string description = "",
        Action<string>? logCallback = null)
    {
        try
        {
            if (_graphClient == null)
                return (false, "Graph client not initialized", null);

            if (!File.Exists(intunewinPath))
                return (false, $"File not found: {intunewinPath}", null);

            logCallback?.Invoke($"Starting upload for: {displayName}");

            // Step 1: Create the Win32LobApp
            logCallback?.Invoke("Step 1: Creating app registration in Intune...");
            var app = await CreateWin32LobAppAsync(displayName, description);
            
            if (app?.Id == null)
                return (false, "Failed to create app in Intune", null);

            logCallback?.Invoke($"App created with ID: {app.Id}");

            // Step 2: Create content version
            logCallback?.Invoke("Step 2: Creating content version...");
            var contentVersion = await CreateContentVersionAsync(app.Id);
            
            if (contentVersion?.Id == null)
                return (false, "Failed to create content version", app.Id);

            logCallback?.Invoke($"Content version created: {contentVersion.Id}");

            // Step 3: Extract and read detection.xml from .intunewin
            logCallback?.Invoke("Step 3: Reading package metadata...");
            var detectionInfo = ReadIntuneWinMetadata(intunewinPath);

            // Step 4: Create the content version file
            logCallback?.Invoke("Step 4: Preparing file upload...");
            var fileInfo = new FileInfo(intunewinPath);
            var contentFile = await CreateContentVersionFileAsync(
                app.Id,
                contentVersion.Id,
                fileInfo.Name,
                fileInfo.Length,
                detectionInfo);

            if (contentFile?.AzureStorageUri == null)
                return (false, "Failed to create content version file", app.Id);

            logCallback?.Invoke($"Upload URL obtained");

            // Step 5: Upload the file to Azure Storage
            logCallback?.Invoke("Step 5: Uploading file to Azure Storage...");
            await UploadFileToAzureStorageAsync(intunewinPath, contentFile.AzureStorageUri, logCallback);
            logCallback?.Invoke("File uploaded successfully");

            // Step 6: Commit the file
            logCallback?.Invoke("Step 6: Committing file...");
            await CommitContentVersionFileAsync(app.Id, contentVersion.Id, contentFile.Id!);
            logCallback?.Invoke("File committed");

            // Step 7: Commit the content version
            logCallback?.Invoke("Step 7: Finalizing content version...");
            await CommitContentVersionAsync(app.Id, contentVersion.Id);
            logCallback?.Invoke("Content version finalized");

            // Step 8: Wait for processing
            logCallback?.Invoke("Step 8: Waiting for Intune to process the app...");
            var committed = await WaitForContentVersionCommitAsync(app.Id, contentVersion.Id, logCallback);
            
            if (!committed)
                return (false, "Content version processing timed out or failed", app.Id);

            logCallback?.Invoke($"✓ Successfully uploaded: {displayName}");
            return (true, $"Successfully uploaded {displayName}", app.Id);
        }
        catch (Exception ex)
        {
            logCallback?.Invoke($"ERROR: {ex.Message}");
            return (false, $"Upload failed: {ex.Message}", null);
        }
    }

    private async Task<Win32LobApp?> CreateWin32LobAppAsync(string displayName, string description)
    {
        var app = new Win32LobApp
        {
            OdataType = "#microsoft.graph.win32LobApp",
            DisplayName = displayName,
            Description = description,
            Publisher = "Uploaded via Win32-to-IntuneUI",
            InstallExperience = new Win32LobAppInstallExperience
            {
                RunAsAccount = RunAsAccountType.System
            },
            ApplicableArchitectures = WindowsArchitecture.X64 | WindowsArchitecture.X86
        };

        return await _graphClient!.DeviceAppManagement.MobileApps.PostAsync(app) as Win32LobApp;
    }

    private async Task<MobileAppContent?> CreateContentVersionAsync(string appId)
    {
        var requestUrl = $"https://graph.microsoft.com/v1.0/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions";
        var content = new { };

        var response = await SendGraphRequestAsync<MobileAppContent>(requestUrl, HttpMethod.Post, content);
        return response;
    }

    private async Task<MobileAppContentFile?> CreateContentVersionFileAsync(
        string appId,
        string contentVersionId,
        string fileName,
        long fileSize,
        IntuneWinMetadata metadata)
    {
        var requestUrl = $"https://graph.microsoft.com/v1.0/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}/files";
        
        var fileRequest = new
        {
            OdataType = "#microsoft.graph.mobileAppContentFile",
            name = fileName,
            size = fileSize,
            sizeEncrypted = fileSize,
            manifest = metadata.Manifest,
            isDependency = false
        };

        return await SendGraphRequestAsync<MobileAppContentFile>(requestUrl, HttpMethod.Post, fileRequest);
    }

    private async Task UploadFileToAzureStorageAsync(string filePath, string azureStorageUri, Action<string>? logCallback)
    {
        const int chunkSize = 6 * 1024 * 1024; // 6 MB chunks
        var fileInfo = new FileInfo(filePath);
        var totalChunks = (int)Math.Ceiling((double)fileInfo.Length / chunkSize);

        logCallback?.Invoke($"Uploading in {totalChunks} chunk(s)...");

        using var fileStream = File.OpenRead(filePath);
        var buffer = new byte[chunkSize];
        var chunkNumber = 0;

        while (fileStream.Position < fileStream.Length)
        {
            var bytesRead = await fileStream.ReadAsync(buffer, 0, chunkSize);
            var actualBuffer = bytesRead < chunkSize ? buffer[..bytesRead] : buffer;

            var startByte = fileStream.Position - bytesRead;
            var endByte = fileStream.Position - 1;

            using var httpClient = new HttpClient();
            using var content = new ByteArrayContent(actualBuffer);
            
            content.Headers.ContentLength = bytesRead;
            content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(startByte, endByte, fileInfo.Length);

            var response = await httpClient.PutAsync(azureStorageUri, content);
            response.EnsureSuccessStatusCode();

            chunkNumber++;
            if (chunkNumber % 5 == 0 || chunkNumber == totalChunks)
            {
                logCallback?.Invoke($"  Uploaded {chunkNumber}/{totalChunks} chunks ({(int)((double)chunkNumber / totalChunks * 100)}%)");
            }
        }
    }

    private async Task CommitContentVersionFileAsync(string appId, string contentVersionId, string fileId)
    {
        var requestUrl = $"https://graph.microsoft.com/v1.0/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}/files/{fileId}/commit";
        
        var commitRequest = new
        {
            fileEncryptionInfo = new
            {
                fileEncryptionInfo = new { }
            }
        };

        await SendGraphRequestAsync<object>(requestUrl, HttpMethod.Post, commitRequest);
    }

    private async Task CommitContentVersionAsync(string appId, string contentVersionId)
    {
        var requestUrl = $"https://graph.microsoft.com/v1.0/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}/commit";
        var commitRequest = new { };

        await SendGraphRequestAsync<object>(requestUrl, HttpMethod.Post, commitRequest);
    }

    private async Task<bool> WaitForContentVersionCommitAsync(string appId, string contentVersionId, Action<string>? logCallback, int maxAttempts = 30)
    {
        var requestUrl = $"https://graph.microsoft.com/v1.0/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}";

        for (int i = 0; i < maxAttempts; i++)
        {
            await Task.Delay(2000); // Wait 2 seconds between checks

            try
            {
                var response = await SendGraphRequestAsync<dynamic>(requestUrl, HttpMethod.Get, null);
                
                // Check if content version has been committed (processing complete)
                if (response?.committedContentVersion != null)
                {
                    logCallback?.Invoke("Processing complete");
                    return true;
                }
            }
            catch
            {
                // Continue waiting
            }

            if (i % 5 == 0 && i > 0)
            {
                logCallback?.Invoke($"  Still processing... ({i * 2}s elapsed)");
            }
        }

        return false;
    }

    private async Task<T?> SendGraphRequestAsync<T>(string url, HttpMethod method, object? body) where T : class
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

        var request = new HttpRequestMessage(method, url);

        if (body != null && (method == HttpMethod.Post || method == HttpMethod.Patch || method == HttpMethod.Put))
        {
            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
            });
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        if (method == HttpMethod.Get || response.Content.Headers.ContentLength > 0)
        {
            var responseJson = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(responseJson, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });
        }

        return null;
    }

    private IntuneWinMetadata ReadIntuneWinMetadata(string intunewinPath)
    {
        // For now, return basic metadata
        // In a full implementation, you would extract detection.xml from the .intunewin file
        // The .intunewin file is a ZIP file containing metadata
        
        return new IntuneWinMetadata
        {
            Manifest = Convert.ToBase64String(Encoding.UTF8.GetBytes("<ManifestData></ManifestData>"))
        };
    }
}

public class IntuneWinMetadata
{
    public string? Manifest { get; set; }
}
