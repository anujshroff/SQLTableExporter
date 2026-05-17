using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Text.RegularExpressions;

namespace SQLTableExporter;

/// <summary>
/// Provides functionality to upload export files to Azure Blob Storage
/// </summary>
public partial class AzureBlobStorageUploader
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _containerName;
    private readonly string _folderPath;
    private readonly string _storageAccount;

    /// <summary>
    /// Initializes a new instance of the AzureBlobStorageUploader class
    /// </summary>
    /// <param name="azureBlobStorageUrl">The Azure Blob Storage URL or SAS URL</param>
    /// <exception cref="ArgumentException">Thrown when the URL is invalid or doesn't contain required components</exception>
    public AzureBlobStorageUploader(string azureBlobStorageUrl)
    {
        if (string.IsNullOrWhiteSpace(azureBlobStorageUrl))
        {
            throw new ArgumentException("Azure Blob Storage URL cannot be null or empty", nameof(azureBlobStorageUrl));
        }

        // Parse the URL to extract storage account, container, and folder path
        ParseUrl(azureBlobStorageUrl, out _storageAccount, out _containerName, out _folderPath);

        // Determine if it's a SAS URL or a regular URL (for managed identity)
        if (IsSasUrl(azureBlobStorageUrl))
        {
            // Use SAS token for authentication
            _blobServiceClient = new BlobServiceClient(new Uri(azureBlobStorageUrl));
            Console.WriteLine($"Using SAS token authentication for Azure Blob Storage");
        }
        else
        {
            // Use DefaultAzureCredential for managed identity authentication
            string serviceUrl = $"https://{_storageAccount}.blob.core.windows.net";
            _blobServiceClient = new BlobServiceClient(new Uri(serviceUrl), new DefaultAzureCredential());
            Console.WriteLine($"Using managed identity authentication for Azure Blob Storage");
        }
    }

    /// <summary>
    /// Determines if the URL is a SAS URL by checking for query parameters
    /// </summary>
    /// <param name="url">The URL to check</param>
    /// <returns>True if the URL is a SAS URL, false otherwise</returns>
    public static bool IsSasUrl(string url) => url.Contains('?') && url.Contains("sv=");

    /// <summary>
    /// Parses the Azure Blob Storage URL to extract storage account, container, and folder path
    /// </summary>
    /// <param name="url">The URL to parse</param>
    /// <param name="storageAccount">The extracted storage account name</param>
    /// <param name="containerName">The extracted container name</param>
    /// <param name="folderPath">The extracted folder path (can be empty)</param>
    /// <exception cref="ArgumentException">Thrown when the URL format is invalid</exception>
    public static void ParseUrl(string url, out string storageAccount, out string containerName, out string folderPath)
    {
        // Extract the base URL part without query string
        string baseUrl = url.Split('?')[0];

        // Regex to match https://{account}.blob.core.windows.net/{container}/{optional-folder-path}
        Regex regex = BlobUrlRegex();
        Match match = regex.Match(baseUrl);

        if (!match.Success || match.Groups.Count < 3)
        {
            throw new ArgumentException(
                "Invalid Azure Blob Storage URL format. Expected format: https://{account}.blob.core.windows.net/{container}/{optional-folder-path}",
                nameof(url));
        }

        storageAccount = match.Groups[1].Value;
        containerName = match.Groups[2].Value;
        folderPath = match.Groups.Count > 3 && !string.IsNullOrEmpty(match.Groups[3].Value)
            ? match.Groups[3].Value
            : string.Empty;

        // Ensure the folder path ends with a trailing slash if it's not empty
        if (!string.IsNullOrEmpty(folderPath) && !folderPath.EndsWith('/'))
        {
            folderPath += '/';
        }
    }

    /// <summary>
    /// Uploads a file to Azure Blob Storage
    /// </summary>
    /// <param name="filePath">The path to the file to upload</param>
    /// <param name="blobNamePrefix">Optional prefix for the blob name</param>
    /// <returns>The URL of the uploaded blob</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist</exception>
    /// <exception cref="Azure.RequestFailedException">Thrown when the upload fails</exception>
    public async Task<string> UploadFileAsync(string filePath, string? blobNamePrefix = null)
    {
        // Validate file exists
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        // Get container client
        BlobContainerClient containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);

        // Create container if it doesn't exist
        await containerClient.CreateIfNotExistsAsync();

        // Get file name
        string fileName = Path.GetFileName(filePath);

        // Combine folder path, prefix, and file name to create full blob name
        string blobName = string.IsNullOrEmpty(blobNamePrefix)
            ? Path.Combine(_folderPath, fileName).Replace('\\', '/')
            : Path.Combine(_folderPath, blobNamePrefix, fileName).Replace('\\', '/');

        // Get blob client
        BlobClient blobClient = containerClient.GetBlobClient(blobName);

        // Set upload options with progress handler and explicit overwrite behavior
        BlobUploadOptions options = new()
        {
            ProgressHandler = new Progress<long>(progress =>
            {
                // Report progress every 20 MB
                if (progress % (20 * 1024 * 1024) < 1024 * 1024)
                {
                    Console.WriteLine($"Uploaded {FormatBytes(progress)} to {blobName}");
                }
            }),
            Conditions = new BlobRequestConditions { IfNoneMatch = null } // Explicitly set to overwrite any existing blob
        };

        // Upload file with progress reporting
        await using FileStream fs = File.OpenRead(filePath);
        Console.WriteLine($"Uploading {filePath} to {blobClient.Uri}");

        DateTime startTime = DateTime.Now;
        await blobClient.UploadAsync(fs, options);
        TimeSpan elapsed = DateTime.Now - startTime;

        // Calculate and display upload statistics
        double mbPerSecond = fs.Length / (1024.0 * 1024.0) / elapsed.TotalSeconds;
        Console.WriteLine($"Upload complete: {FormatBytes(fs.Length)} in {FormatTimeSpan(elapsed)} ({mbPerSecond:F2} MB/s)");

        return blobClient.Uri.ToString();
    }

    /// <summary>
    /// Uploads a directory to Azure Blob Storage, preserving the folder structure
    /// </summary>
    /// <param name="directoryPath">The path to the directory to upload</param>
    /// <param name="preserveDirectoryStructure">Whether to preserve the directory structure in the blob names</param>
    /// <returns>The number of files uploaded</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when the directory does not exist</exception>
    /// <exception cref="InvalidOperationException">Thrown when any file upload fails</exception>
    public async Task<int> UploadDirectoryAsync(string directoryPath, bool preserveDirectoryStructure = true)
    {
        // Validate directory exists
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        // Get container client
        BlobContainerClient containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);

        // Create container if it doesn't exist
        await containerClient.CreateIfNotExistsAsync();

        // Get all files in the directory
        string[] files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);

        if (files.Length == 0)
        {
            Console.WriteLine($"Warning: Directory is empty: {directoryPath}");
            return 0;
        }

        // Calculate total size for progress reporting
        long totalBytes = 0;
        foreach (string file in files)
        {
            FileInfo fileInfo = new(file);
            totalBytes += fileInfo.Length;
        }

        // Set up progress tracking
        long processedBytes = 0;
        int processedFiles = 0;
        DateTime startTime = DateTime.Now;

        Console.WriteLine($"Starting upload of {files.Length} files ({FormatBytes(totalBytes)}) to Azure Blob Storage");

        // Upload each file preserving directory structure
        foreach (string file in files)
        {
            FileInfo fileInfo = new(file);
            string relativePath = Path.GetRelativePath(directoryPath, file);

            // Determine blob name based on whether to preserve structure
            string blobName = preserveDirectoryStructure
                ? Path.Combine(_folderPath, relativePath).Replace('\\', '/')
                : Path.Combine(_folderPath, Path.GetFileName(file)).Replace('\\', '/');

            // Get blob client
            BlobClient blobClient = containerClient.GetBlobClient(blobName);

            // Set upload options with progress handler and explicit overwrite behavior
            BlobUploadOptions options = new()
            {
                Conditions = new BlobRequestConditions { IfNoneMatch = null } // Explicitly set to overwrite any existing blob
            };

            try
            {
                // Upload file
                await using FileStream fs = File.OpenRead(file);
                Console.Write($"\rUploading {relativePath} ");

                await blobClient.UploadAsync(fs, options);

                // Update progress tracking
                processedBytes += fileInfo.Length;
                processedFiles++;

                // Display progress
                double percentComplete = (double)processedBytes / totalBytes * 100;
                double bytesPerSecond = processedBytes / (DateTime.Now - startTime).TotalSeconds;

                Console.Write($"\rUploaded {processedFiles}/{files.Length} files | {percentComplete:F1}% complete | {FormatBytes(processedBytes)}/{FormatBytes(totalBytes)} | {FormatBytes((long)bytesPerSecond)}/s");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError uploading {relativePath}: {ex.Message}");

                // Always throw an exception when a file upload fails
                // This ensures the application will exit with an error
                throw new InvalidOperationException(
                    $"Upload operation failed on file {relativePath}: {ex.Message}", ex);
            }
        }

        TimeSpan elapsed = DateTime.Now - startTime;
        double mbPerSecond = totalBytes / (1024.0 * 1024.0) / elapsed.TotalSeconds;

        Console.WriteLine();
        Console.WriteLine($"Upload complete: {processedFiles}/{files.Length} files, {FormatBytes(totalBytes)} in {FormatTimeSpan(elapsed)} ({mbPerSecond:F2} MB/s)");

        return processedFiles;
    }

    /// <summary>
    /// Formats bytes into a human-readable string (KB, MB, GB)
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        const int scale = 1024;
        string[] orders = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double size = bytes;

        while (size >= scale && order < orders.Length - 1)
        {
            order++;
            size /= scale;
        }

        return $"{size:0.##} {orders[order]}";
    }

    /// <summary>
    /// Formats a TimeSpan into a human-readable string
    /// </summary>
    private static string FormatTimeSpan(TimeSpan timeSpan) => timeSpan.TotalHours >= 1
            ? $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m"
            : timeSpan.TotalMinutes >= 1 ? $"{(int)timeSpan.TotalMinutes}m {timeSpan.Seconds}s" : $"{(int)timeSpan.TotalSeconds}s";

    [GeneratedRegex(@"https://([^.]+)\.blob\.core\.windows\.net/([^/]+)(?:/(.*))?", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex BlobUrlRegex();
}
