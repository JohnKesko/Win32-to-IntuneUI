namespace Win32_to_IntuneUI;

/// <summary>
/// Configuration for auto-updates. The token is injected at build time by GitHub Actions.
/// </summary>
public static class UpdateConfig
{
    /// <summary>
    /// GitHub repository URL for update checks
    /// </summary>
    public const string GitHubRepoUrl = "https://github.com/JohnKesko/Win32-to-IntuneUI";

    /// <summary>
    /// GitHub Personal Access Token for private repo access.
    /// This placeholder is replaced during CI/CD build.
    /// </summary>
    public static readonly string GitHubToken = "__UPDATE_PAT_PLACEHOLDER__";

    /// <summary>
    /// Check if a valid token is configured (not the placeholder)
    /// </summary>
    public static bool HasValidToken =>
        !string.IsNullOrEmpty(GitHubToken) &&
        GitHubToken != "__UPDATE_PAT_PLACEHOLDER__";
}
