using System.IO.Compression;
using Saksbehandling.Api.Models;

namespace Saksbehandling.Api.Services;

/// <summary>
/// Extracts a zip archive uploaded for a case, registering each contained
/// file as a Document. Lets a saksbehandler attach a whole folder of scanned
/// pages or correspondence in one upload instead of one file at a time.
/// </summary>
public class CaseBulkUploadService
{
    // Same short blocklist as the single-file upload endpoint.
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".sh", ".dll", ".msi",
    };

    private readonly string _rootPath;

    public CaseBulkUploadService(IConfiguration configuration)
    {
        _rootPath = configuration["Storage:RootPath"] ?? "/app/storage";
    }

    public async Task<List<Document>> ExtractZipAsync(int caseId, Stream zipStream, int uploadedByUserId)
    {
        var caseFolder = Path.Combine(_rootPath, "cases", caseId.ToString());
        Directory.CreateDirectory(caseFolder);

        var documents = new List<Document>();

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            // Directory entries have an empty Name (only FullName, ending in "/").
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var extension = Path.GetExtension(entry.Name);
            if (BlockedExtensions.Contains(extension))
            {
                continue;
            }

            var destinationPath = Path.Combine(caseFolder, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            await using (var entryStream = entry.Open())
            await using (var fileStream = File.Create(destinationPath))
            {
                await entryStream.CopyToAsync(fileStream);
            }

            var relativeStoragePath = Path.Combine("cases", caseId.ToString(), entry.FullName);

            documents.Add(new Document
            {
                CaseId = caseId,
                FileName = entry.Name,
                StoredFileName = Path.GetFileName(entry.FullName),
                ContentType = "application/octet-stream",
                SizeBytes = entry.Length,
                UploadedByUserId = uploadedByUserId,
                StoragePath = relativeStoragePath,
            });
        }

        return documents;
    }
}
