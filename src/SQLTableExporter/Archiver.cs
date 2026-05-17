using SharpCompress.Common;
using SharpCompress.Writers;

namespace SQLTableExporter;

/// <summary>
/// Provides functionality to archive a directory using ZIP format with LZMA compression
/// </summary>
public static class Archiver
{
    /// <summary>
    /// Archives a directory using ZIP format with LZMA compression
    /// </summary>
    /// <param name="directoryPath">The directory to archive</param>
    /// <param name="archivePath">Optional custom path for the archive. If not specified, uses directory name + .zip</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The path to the created archive</returns>
    public static async Task<string> ArchiveDirectoryAsync(
        string directoryPath,
        string? archivePath = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        if (IsDirectoryEmpty(directoryPath))
        {
            throw new InvalidOperationException($"Directory is empty: {directoryPath}");
        }

        string finalArchivePath = archivePath ?? $"{directoryPath}.zip";

        string? archiveDirectory = Path.GetDirectoryName(finalArchivePath);
        if (!string.IsNullOrEmpty(archiveDirectory) && !Directory.Exists(archiveDirectory))
        {
            Directory.CreateDirectory(archiveDirectory);
        }

        if (File.Exists(finalArchivePath))
        {
            File.Delete(finalArchivePath);
        }

        string[] files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);

        long totalBytes = 0;
        foreach (string file in files)
        {
            FileInfo fileInfo = new(file);
            totalBytes += fileInfo.Length;
        }

        long processedBytes = 0;
        DateTime startTime = DateTime.Now;

        Console.WriteLine("Creating ZIP archive with LZMA compression...");

        await using (FileStream outStream = File.Create(finalArchivePath))
        {
            WriterOptions options = new(CompressionType.LZMA)
            {
                ArchiveEncoding = new ArchiveEncoding { Default = System.Text.Encoding.UTF8 },
            };

            await using IAsyncWriter writer = await WriterFactory.OpenAsyncWriter(
                outStream, ArchiveType.Zip, options, cancellationToken);

            foreach (string file in files)
            {
                string relativePath = Path.GetRelativePath(directoryPath, file)
                    .Replace(Path.DirectorySeparatorChar, '/');
                FileInfo fileInfo = new(file);
                Console.Write($"\rAdding to archive: {relativePath} ");

                await writer.WriteAsync(relativePath, file, cancellationToken);

                processedBytes += fileInfo.Length;
                double percentComplete = (double)processedBytes / totalBytes * 100;
                double bytesPerSecond = processedBytes / (DateTime.Now - startTime).TotalSeconds;

                Console.Write($"\rArchiving: {percentComplete:F1}% complete | {FormatBytes(processedBytes)}/{FormatBytes(totalBytes)} | {FormatBytes((long)bytesPerSecond)}/s");
            }
        }

        Console.WriteLine();
        Console.WriteLine("ZIP archive creation complete!");

        FileInfo archiveInfo = new(finalArchivePath);
        double compressionRatio = (double)totalBytes / archiveInfo.Length;
        double spaceSaved = 100 - (100 / compressionRatio);

        Console.WriteLine($"Archive created: {finalArchivePath}");
        Console.WriteLine($"Original size: {FormatBytes(totalBytes)}");
        Console.WriteLine($"Archive size: {FormatBytes(archiveInfo.Length)}");
        Console.WriteLine($"Compression ratio: {compressionRatio:F2}x ({spaceSaved:F1}% space saved)");

        return finalArchivePath;
    }

    /// <summary>
    /// Checks if a directory is empty (no files or subdirectories)
    /// </summary>
    private static bool IsDirectoryEmpty(string path) => !Directory.EnumerateFileSystemEntries(path).Any();

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
}
