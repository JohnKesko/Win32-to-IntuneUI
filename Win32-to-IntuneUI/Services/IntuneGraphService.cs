using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Win32_to_IntuneUI.Services;

public class IntuneGraphService
{
    private string? _accessToken;
    private static readonly HttpClient _httpClient;
    private const string GraphBaseUrl = "https://graph.microsoft.com/beta";

    static IntuneGraphService()
    {
        // Use a shared static HttpClient to prevent port exhaustion
        // HttpClient is designed to be reused and is thread-safe
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(30); // Long timeout for large file uploads
    }

    public IntuneGraphService()
    {
        // Instance constructor - nothing to initialize since HttpClient is static
    }

    /// <summary>
    /// Initialize the service with an existing access token
    /// </summary>
    public void InitializeWithAccessToken(string accessToken)
    {
        _accessToken = accessToken;
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _accessToken);
    }

    /// <summary>
    /// Exchange client credentials for an access token using OAuth 2.0 Client Credentials flow
    /// </summary>
    /// <param name="tenantId">Azure AD Tenant ID</param>
    /// <param name="clientId">Application (Client) ID</param>
    /// <param name="clientSecret">Client Secret</param>
    /// <returns>Tuple with success status and message (or token on success)</returns>
    public async Task<(bool Success, string Message, string? Token, DateTime? ExpiresAt)> AcquireTokenAsync(
        string tenantId,
        string clientId,
        string clientSecret)
    {
        try
        {
            var tokenEndpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";

            var requestBody = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["scope"] = "https://graph.microsoft.com/.default"
            });

            // Reuse the shared HttpClient for token acquisition
            var response = await _httpClient.PostAsync(tokenEndpoint, requestBody);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent);

                if (tokenResponse?.AccessToken != null)
                {
                    var expiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

                    // Initialize the service with the new token
                    InitializeWithAccessToken(tokenResponse.AccessToken);

                    return (true, "Token acquired successfully", tokenResponse.AccessToken, expiresAt);
                }

                return (false, "No access token in response", null, null);
            }

            // Parse error response
            try
            {
                var errorResponse = JsonSerializer.Deserialize<TokenErrorResponse>(responseContent);
                var errorMessage = errorResponse?.ErrorDescription ?? errorResponse?.Error ?? "Unknown error";
                return (false, $"Authentication failed: {errorMessage}", null, null);
            }
            catch
            {
                return (false, $"Authentication failed: {response.StatusCode}", null, null);
            }
        }
        catch (Exception ex)
        {
            return (false, $"Token acquisition error: {ex.Message}", null, null);
        }
    }

    /// <summary>
    /// Test the connection to Microsoft Graph and verify required permissions
    /// </summary>
    public async Task<(bool Success, string Message)> TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(_accessToken))
            return (false, "Access token not configured. Please enter your access token.");

        try
        {
            // Step 1: Test basic connectivity by getting organization info
            var orgResponse = await _httpClient.GetAsync($"{GraphBaseUrl}/organization");

            if (!orgResponse.IsSuccessStatusCode)
            {
                if (orgResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return (false, "Unauthorized - token may be expired or invalid");
                }
                var errorContent = await orgResponse.Content.ReadAsStringAsync();
                return (false, $"Connection failed: {orgResponse.StatusCode}");
            }

            var orgContent = await orgResponse.Content.ReadAsStringAsync();
            var orgData = JsonSerializer.Deserialize<GraphListResponse<OrganizationInfo>>(orgContent);
            var orgName = orgData?.Value?.FirstOrDefault()?.DisplayName ?? "Unknown Organization";

            // Step 2: Test DeviceManagementApps.ReadWrite.All permission
            // Try to list mobile apps - this requires the correct permission
            var appsResponse = await _httpClient.GetAsync($"{GraphBaseUrl}/deviceAppManagement/mobileApps?$top=1");

            if (!appsResponse.IsSuccessStatusCode)
            {
                if (appsResponse.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    return (false,
                        $"Connected to: {orgName}\n\n" +
                        "⚠️ Missing required permission!\n" +
                        "The app needs 'DeviceManagementApps.ReadWrite.All' permission.\n\n" +
                        "To fix:\n" +
                        "1. Go to Azure Portal → Microsoft Entra ID → App registrations\n" +
                        "2. Select your app → API permissions → Add permission\n" +
                        "3. Microsoft Graph → Application permissions\n" +
                        "4. Add: DeviceManagementApps.ReadWrite.All\n" +
                        "5. Grant admin consent");
                }

                var errorContent = await appsResponse.Content.ReadAsStringAsync();
                return (false, $"Connected to {orgName}, but permission check failed: {appsResponse.StatusCode}");
            }

            return (true, $"✓ Connected to: {orgName}\n✓ Intune app permissions verified");
        }
        catch (Exception ex)
        {
            return (false, $"Connection error: {ex.Message}");
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
        if (string.IsNullOrWhiteSpace(_accessToken))
            return (false, "Access token not configured", null);

        if (!File.Exists(intunewinPath))
            return (false, $"File not found: {intunewinPath}", null);

        try
        {
            logCallback?.Invoke($"Starting upload for: {displayName}");

            // Step 1: Extract metadata from .intunewin file
            logCallback?.Invoke("Step 1: Reading package metadata...");
            var metadata = ExtractIntuneWinMetadata(intunewinPath);

            if (metadata == null)
            {
                return (false, "Failed to extract metadata from .intunewin file", null);
            }

            logCallback?.Invoke($"  Installer: {metadata.FileName}");
            logCallback?.Invoke($"  Unencrypted size: {FormatBytes(metadata.UnencryptedContentSize)}");

            // Step 2: Create the Win32LobApp
            logCallback?.Invoke("Step 2: Creating app registration in Intune...");
            var appId = await CreateWin32LobAppAsync(displayName, description, metadata);

            if (string.IsNullOrEmpty(appId))
                return (false, "Failed to create app in Intune", null);

            logCallback?.Invoke($"  App created with ID: {appId}");

            // Step 3: Create content version
            logCallback?.Invoke("Step 3: Creating content version...");
            var contentVersionId = await CreateContentVersionAsync(appId);

            if (string.IsNullOrEmpty(contentVersionId))
                return (false, "Failed to create content version", appId);

            logCallback?.Invoke($"  Content version: {contentVersionId}");

            // Step 4: Create content file entry
            logCallback?.Invoke("Step 4: Creating content file entry...");
            var fileInfo = new FileInfo(intunewinPath);
            var contentFile = await CreateContentFileAsync(appId, contentVersionId, metadata, fileInfo.Length);

            if (contentFile == null || string.IsNullOrEmpty(contentFile.Id))
                return (false, "Failed to create content file entry", appId);

            logCallback?.Invoke($"  File entry created, waiting for Azure upload URL...");

            // Step 5: Wait for Azure Storage URI
            var uploadInfo = await WaitForAzureStorageUriAsync(appId, contentVersionId, contentFile.Id);

            if (uploadInfo == null || string.IsNullOrEmpty(uploadInfo.AzureStorageUri))
                return (false, "Failed to get Azure Storage upload URL", appId);

            logCallback?.Invoke("Step 5: Uploading file to Azure Storage...");

            // Step 6: Upload to Azure Storage using block blobs
            await UploadFileToAzureStorageAsync(intunewinPath, uploadInfo.AzureStorageUri, logCallback);
            logCallback?.Invoke("  File uploaded successfully");

            // Step 7: Commit the file
            logCallback?.Invoke("Step 6: Committing file...");
            await CommitFileAsync(appId, contentVersionId, contentFile.Id, metadata);

            // Step 8: Wait for file processing
            logCallback?.Invoke("Step 7: Waiting for file processing...");
            var fileReady = await WaitForFileProcessingAsync(appId, contentVersionId, contentFile.Id, logCallback);

            if (!fileReady)
            {
                logCallback?.Invoke("  Warning: File processing may not be complete");
            }

            // Step 9: Commit content version to the app
            logCallback?.Invoke("Step 8: Finalizing app content...");
            await CommitContentVersionToAppAsync(appId, contentVersionId);

            logCallback?.Invoke($"✓ Successfully uploaded: {displayName}");
            return (true, $"Successfully uploaded {displayName}", appId);
        }
        catch (Exception ex)
        {
            logCallback?.Invoke($"ERROR: {ex.Message}");
            return (false, $"Upload failed: {ex.Message}", null);
        }
    }

    private async Task<string?> CreateWin32LobAppAsync(string displayName, string description, IntuneWinMetadata metadata)
    {
        // Determine install command based on file extension
        var fileName = metadata.FileName ?? "setup.exe";
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        string installCommand;
        string uninstallCommand;

        if (extension == ".msi")
        {
            installCommand = $"msiexec /i \"{fileName}\" /qn";
            uninstallCommand = $"msiexec /x \"{metadata.MsiProductCode ?? fileName}\" /qn";
        }
        else
        {
            // For EXE files, use common silent install switches
            installCommand = $"\"{fileName}\" /S";
            uninstallCommand = $"\"{fileName}\" /uninstall /S";
        }

        var app = new
        {
            odatatype = "#microsoft.graph.win32LobApp",
            displayName,
            description = string.IsNullOrEmpty(description) ? displayName : description,
            publisher = "Uploaded via Win32-to-IntuneUI",
            fileName,
            installCommandLine = installCommand,
            uninstallCommandLine = uninstallCommand,
            installExperience = new
            {
                runAsAccount = "system",
                deviceRestartBehavior = "suppress"
            },
            applicableArchitectures = "x64,x86",
            minimumSupportedWindowsRelease = "1607",
            detectionRules = new object[]
            {
                // File detection rule - check if the installer file exists in Program Files
                new
                {
                    odatatype = "#microsoft.graph.win32LobAppFileSystemDetectionRule",
                    path = extension == ".msi"
                        ? "%ProgramFiles%"
                        : $"%ProgramFiles%\\{Path.GetFileNameWithoutExtension(fileName)}",
                    fileOrFolderName = extension == ".msi" ? displayName : fileName,
                    check32BitOn64System = false,
                    detectionType = "exists"
                }
            },
            returnCodes = new object[]
            {
                new { returnCode = 0, type = "success" },
                new { returnCode = 1707, type = "success" },
                new { returnCode = 3010, type = "softReboot" },
                new { returnCode = 1641, type = "hardReboot" },
                new { returnCode = 1618, type = "retry" }
            }
        };

        var json = JsonSerializer.Serialize(app, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        // Fix the @odata.type property name
        json = json.Replace("\"odatatype\"", "\"@odata.type\"");

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{GraphBaseUrl}/deviceAppManagement/mobileApps", content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to create app: {response.StatusCode} - {error}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        return doc.RootElement.TryGetProperty("id", out var idElement)
            ? idElement.GetString()
            : null;
    }

    private async Task<string?> CreateContentVersionAsync(string appId)
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(
            $"{GraphBaseUrl}/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions",
            content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to create content version: {response.StatusCode} - {error}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        return doc.RootElement.TryGetProperty("id", out var idElement)
            ? idElement.GetString()
            : null;
    }

    private async Task<ContentFileInfo?> CreateContentFileAsync(
        string appId,
        string contentVersionId,
        IntuneWinMetadata metadata,
        long encryptedSize)
    {
        var fileRequest = new
        {
            odatatype = "#microsoft.graph.mobileAppContentFile",
            name = Path.GetFileName(metadata.FileName) + ".intunewin",
            size = metadata.UnencryptedContentSize,
            sizeEncrypted = encryptedSize,
            manifest = null as string,
            isDependency = false
        };

        var json = JsonSerializer.Serialize(fileRequest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        json = json.Replace("\"odatatype\"", "\"@odata.type\"");

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(
            $"{GraphBaseUrl}/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}/files",
            content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to create content file: {response.StatusCode} - {error}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ContentFileInfo>(responseJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    private async Task<ContentFileInfo?> WaitForAzureStorageUriAsync(
        string appId,
        string contentVersionId,
        string fileId,
        int maxAttempts = 30)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            await Task.Delay(2000);

            var response = await _httpClient.GetAsync(
                $"{GraphBaseUrl}/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}/files/{fileId}");

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                var fileInfo = JsonSerializer.Deserialize<ContentFileInfo>(responseJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (!string.IsNullOrEmpty(fileInfo?.AzureStorageUri))
                {
                    return fileInfo;
                }
            }
        }

        return null;
    }

    private async Task UploadFileToAzureStorageAsync(string filePath, string azureStorageUri, Action<string>? logCallback)
    {
        const int blockSize = 4 * 1024 * 1024; // 4 MB blocks
        var fileInfo = new FileInfo(filePath);
        var totalBlocks = (int)Math.Ceiling((double)fileInfo.Length / blockSize);
        var blockIds = new List<string>();

        logCallback?.Invoke($"  Uploading in {totalBlocks} block(s)...");

        using var fileStream = File.OpenRead(filePath);
        var buffer = new byte[blockSize];
        var blockNumber = 0;

        while (fileStream.Position < fileStream.Length)
        {
            var bytesRead = await fileStream.ReadAsync(buffer, 0, blockSize);
            var actualBuffer = bytesRead < blockSize ? buffer[..bytesRead] : buffer;

            // Create block ID (must be base64 encoded, same length)
            var blockId = Convert.ToBase64String(Encoding.UTF8.GetBytes(blockNumber.ToString("D6")));
            blockIds.Add(blockId);

            // Upload block
            var blockUri = $"{azureStorageUri}&comp=block&blockid={Uri.EscapeDataString(blockId)}";

            using var blockContent = new ByteArrayContent(actualBuffer);
            blockContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            // Use a request message to set per-request headers without modifying shared HttpClient defaults
            using var blockRequest = new HttpRequestMessage(HttpMethod.Put, blockUri);
            blockRequest.Headers.Add("x-ms-blob-type", "BlockBlob");
            blockRequest.Content = blockContent;

            var blockResponse = await _httpClient.SendAsync(blockRequest);
            blockResponse.EnsureSuccessStatusCode();

            blockNumber++;
            if (blockNumber % 10 == 0 || blockNumber == totalBlocks)
            {
                var percentage = (int)((double)blockNumber / totalBlocks * 100);
                logCallback?.Invoke($"  Progress: {blockNumber}/{totalBlocks} blocks ({percentage}%)");
            }
        }

        // Commit blocks
        logCallback?.Invoke("  Committing blocks...");
        var blockListXml = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?><BlockList>");
        foreach (var id in blockIds)
        {
            blockListXml.Append($"<Latest>{id}</Latest>");
        }
        blockListXml.Append("</BlockList>");

        var commitUri = $"{azureStorageUri}&comp=blocklist";
        using var commitContent = new StringContent(blockListXml.ToString(), Encoding.UTF8, "application/xml");

        // Reuse shared HttpClient with a request message
        using var commitRequest = new HttpRequestMessage(HttpMethod.Put, commitUri);
        commitRequest.Content = commitContent;

        var commitResponse = await _httpClient.SendAsync(commitRequest);
        commitResponse.EnsureSuccessStatusCode();
    }

    private async Task CommitFileAsync(string appId, string contentVersionId, string fileId, IntuneWinMetadata metadata)
    {
        var commitRequest = new
        {
            fileEncryptionInfo = new
            {
                encryptionKey = metadata.EncryptionKey,
                macKey = metadata.MacKey,
                initializationVector = metadata.InitializationVector,
                mac = metadata.Mac,
                profileIdentifier = metadata.ProfileIdentifier,
                fileDigest = metadata.FileDigest,
                fileDigestAlgorithm = metadata.FileDigestAlgorithm
            }
        };

        var json = JsonSerializer.Serialize(commitRequest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(
            $"{GraphBaseUrl}/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}/files/{fileId}/commit",
            content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to commit file: {response.StatusCode} - {error}");
        }
    }

    private async Task<bool> WaitForFileProcessingAsync(
        string appId,
        string contentVersionId,
        string fileId,
        Action<string>? logCallback,
        int maxAttempts = 60)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            await Task.Delay(2000);

            var response = await _httpClient.GetAsync(
                $"{GraphBaseUrl}/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}/files/{fileId}");

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);

                if (doc.RootElement.TryGetProperty("uploadState", out var stateElement))
                {
                    var state = stateElement.GetString();

                    if (state == "commitFileSuccess")
                    {
                        return true;
                    }

                    if (state == "commitFileFailed")
                    {
                        logCallback?.Invoke("  File commit failed");
                        return false;
                    }

                    if (i % 5 == 0)
                    {
                        logCallback?.Invoke($"  Processing... (state: {state})");
                    }
                }
            }
        }

        return false;
    }

    private async Task CommitContentVersionToAppAsync(string appId, string contentVersionId)
    {
        var updateRequest = new
        {
            odatatype = "#microsoft.graph.win32LobApp",
            committedContentVersion = contentVersionId
        };

        var json = JsonSerializer.Serialize(updateRequest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        json = json.Replace("\"odatatype\"", "\"@odata.type\"");

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Patch,
            $"{GraphBaseUrl}/deviceAppManagement/mobileApps/{appId}")
        {
            Content = content
        };

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to commit content version: {response.StatusCode} - {error}");
        }
    }

    /// <summary>
    /// Extract metadata from .intunewin file (which is a ZIP containing detection.xml)
    /// </summary>
    private IntuneWinMetadata? ExtractIntuneWinMetadata(string intunewinPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(intunewinPath);

            // Find the detection.xml file in the IntuneWinPackage/Metadata folder
            var detectionEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith("detection.xml", StringComparison.OrdinalIgnoreCase));

            if (detectionEntry == null)
            {
                return null;
            }

            using var stream = detectionEntry.Open();
            var doc = XDocument.Load(stream);

            var appInfo = doc.Descendants("ApplicationInfo").FirstOrDefault();
            var encryptionInfo = doc.Descendants("EncryptionInfo").FirstOrDefault();

            if (appInfo == null || encryptionInfo == null)
            {
                return null;
            }

            return new IntuneWinMetadata
            {
                FileName = appInfo.Element("FileName")?.Value ?? appInfo.Element("SetupFile")?.Value,
                Name = appInfo.Element("Name")?.Value,
                UnencryptedContentSize = long.TryParse(appInfo.Element("UnencryptedContentSize")?.Value, out var size)
                    ? size
                    : 0,
                MsiProductCode = appInfo.Element("MsiInfo")?.Element("MsiProductCode")?.Value,
                EncryptionKey = encryptionInfo.Element("EncryptionKey")?.Value,
                MacKey = encryptionInfo.Element("MacKey")?.Value,
                InitializationVector = encryptionInfo.Element("InitializationVector")?.Value,
                Mac = encryptionInfo.Element("Mac")?.Value,
                ProfileIdentifier = encryptionInfo.Element("ProfileIdentifier")?.Value ?? "ProfileVersion1",
                FileDigest = encryptionInfo.Element("FileDigest")?.Value,
                FileDigestAlgorithm = encryptionInfo.Element("FileDigestAlgorithm")?.Value ?? "SHA256"
            };
        }
        catch
        {
            return null;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        double len = bytes;
        var order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}

#region DTOs

public class IntuneWinMetadata
{
    public string? FileName { get; set; }
    public string? Name { get; set; }
    public long UnencryptedContentSize { get; set; }
    public string? MsiProductCode { get; set; }
    public string? EncryptionKey { get; set; }
    public string? MacKey { get; set; }
    public string? InitializationVector { get; set; }
    public string? Mac { get; set; }
    public string? ProfileIdentifier { get; set; }
    public string? FileDigest { get; set; }
    public string? FileDigestAlgorithm { get; set; }
}

public class ContentFileInfo
{
    public string? Id { get; set; }
    public string? AzureStorageUri { get; set; }
    public string? UploadState { get; set; }
}

public class GraphListResponse<T>
{
    [JsonPropertyName("value")]
    public T[]? Value { get; set; }
}

public class OrganizationInfo
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
}

public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

public class TokenErrorResponse
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

#endregion
