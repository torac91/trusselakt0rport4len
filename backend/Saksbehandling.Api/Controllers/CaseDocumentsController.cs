using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saksbehandling.Api.Data;
using Saksbehandling.Api.Dtos;
using Saksbehandling.Api.FileStorage;
using Saksbehandling.Api.Models;
using Saksbehandling.Api.Services;

namespace Saksbehandling.Api.Controllers;

[ApiController]
[Route("api/v2/cases/{caseId:int}/documents")]
[Authorize]
public class CaseDocumentsController : ControllerBase
{
    // A short list of obviously dangerous extensions. Anything else -
    // including .html, .svg, .js - is accepted.
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".sh", ".dll", ".msi",
    };

    private readonly AppDbContext _db;
    private readonly IFileStorage _fileStorage;
    private readonly DocumentImportService _importService;
    private readonly CaseBulkUploadService _bulkUploadService;

    public CaseDocumentsController(
        AppDbContext db,
        IFileStorage fileStorage,
        DocumentImportService importService,
        CaseBulkUploadService bulkUploadService)
    {
        _db = db;
        _fileStorage = fileStorage;
        _importService = importService;
        _bulkUploadService = bulkUploadService;
    }

    [HttpGet]
    public async Task<IActionResult> List(int caseId)
    {
        var documents = await _db.Documents
            .Include(d => d.UploadedByUser)
            .Where(d => d.CaseId == caseId)
            .OrderByDescending(d => d.UploadedAt)
            .ToListAsync();

        return Ok(documents.Select(DocumentDto.From));
    }

    [HttpPost]
    [RequestSizeLimit(long.MaxValue)] // supports larger case attachments (e.g. scanned documents)
    public async Task<IActionResult> Upload(int caseId, IFormFile file)
    {
        var caseExists = await _db.Cases.AnyAsync(c => c.Id == caseId);
        if (!caseExists)
        {
            return NotFound();
        }

        var extension = Path.GetExtension(file.FileName);
        if (BlockedExtensions.Contains(extension))
        {
            return BadRequest(new { message = $"Filtypen {extension} er ikke tillatt." });
        }

        await using var stream = file.OpenReadStream();
        var storagePath = await _fileStorage.SaveAsync(caseId, file.FileName, stream);

        var document = new Document
        {
            CaseId = caseId,
            FileName = file.FileName,
            StoredFileName = Path.GetFileName(file.FileName),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            SizeBytes = file.Length,
            UploadedByUserId = GetCallerUserId(),
            StoragePath = storagePath,
        };
        _db.Documents.Add(document);

        _db.AuditEvents.Add(new AuditEvent
        {
            EntityType = nameof(Case),
            EntityId = caseId,
            UserId = GetCallerUserId(),
            Action = "DokumentLastetOpp",
            Details = $"Lastet opp {file.FileName}.",
        });

        await _db.SaveChangesAsync();

        var created = await _db.Documents.Include(d => d.UploadedByUser).FirstAsync(d => d.Id == document.Id);
        return Ok(DocumentDto.From(created));
    }

    [HttpPost("upload-zip")]
    [RequestSizeLimit(long.MaxValue)] // bulk upload - a scanned folder can be large
    public async Task<IActionResult> UploadZip(int caseId, IFormFile file)
    {
        var caseExists = await _db.Cases.AnyAsync(c => c.Id == caseId);
        if (!caseExists)
        {
            return NotFound();
        }

        if (!string.Equals(Path.GetExtension(file.FileName), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Kun .zip-filer støttes for samleopplasting." });
        }

        List<Document> extracted;
        await using (var stream = file.OpenReadStream())
        {
            extracted = await _bulkUploadService.ExtractZipAsync(caseId, stream, GetCallerUserId());
        }

        if (extracted.Count == 0)
        {
            return BadRequest(new { message = "Fant ingen gyldige filer i arkivet." });
        }

        _db.Documents.AddRange(extracted);

        _db.AuditEvents.Add(new AuditEvent
        {
            EntityType = nameof(Case),
            EntityId = caseId,
            UserId = GetCallerUserId(),
            Action = "DokumenterLastetOppSamlet",
            Details = $"Lastet opp {extracted.Count} dokumenter fra {file.FileName} (samleopplasting).",
        });

        await _db.SaveChangesAsync();

        var createdIds = extracted.Select(d => d.Id).ToHashSet();
        var created = await _db.Documents
            .Include(d => d.UploadedByUser)
            .Where(d => createdIds.Contains(d.Id))
            .ToListAsync();

        return Ok(created.Select(DocumentDto.From));
    }

    [HttpPost("import-url")]
    public async Task<IActionResult> ImportFromUrl(int caseId, ImportDocumentUrlRequest request)
    {
        var caseExists = await _db.Cases.AnyAsync(c => c.Id == caseId);
        if (!caseExists)
        {
            return NotFound();
        }

        ImportedDocument imported;
        try
        {
            imported = await _importService.ImportAsync(caseId, request.Url);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Kunne ikke hente dokument fra URL: {ex.Message}" });
        }

        var document = new Document
        {
            CaseId = caseId,
            FileName = imported.FileName,
            StoredFileName = Path.GetFileName(imported.FileName),
            ContentType = imported.ContentType,
            SizeBytes = imported.SizeBytes,
            UploadedByUserId = GetCallerUserId(),
            StoragePath = imported.StoragePath,
        };
        _db.Documents.Add(document);

        _db.AuditEvents.Add(new AuditEvent
        {
            EntityType = nameof(Case),
            EntityId = caseId,
            UserId = GetCallerUserId(),
            Action = "DokumentImportertFraUrl",
            Details = $"Importerte dokument fra {request.Url}.",
        });

        await _db.SaveChangesAsync();

        var created = await _db.Documents.Include(d => d.UploadedByUser).FirstAsync(d => d.Id == document.Id);
        return Ok(DocumentDto.From(created));
    }

    private int GetCallerUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
