using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

public class EmployeeDocumentService : IEmployeeDocumentService
{
    private const string NoTenantMessage = "No authenticated tenant.";
    private const string NotFoundMessage = "Employee not found.";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<EmployeeDocumentService> _logger;

    public EmployeeDocumentService(
        IHrmsDbContext db,
        ITenantContext tenantContext,
        ILogger<EmployeeDocumentService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<EmployeeDocumentDto>>> GetAsync(
        Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<IReadOnlyList<EmployeeDocumentDto>>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<IReadOnlyList<EmployeeDocumentDto>>.NotFound(NotFoundMessage);
        }

        var documents = await _db.EmployeeDocuments.AsNoTracking()
            .Where(d => d.EmployeeId == employeeId)
            .OrderByDescending(d => d.CreatedDate)
            .Select(d => new EmployeeDocumentDto(
                d.Id,
                d.DocumentName,
                d.DocumentCategory,
                d.DocumentNumber,
                d.FilePath,
                d.FileSize,
                d.ContentType,
                d.UploadedBy,
                d.CreatedDate))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<EmployeeDocumentDto>>.Success(documents);
    }

    public async Task<Result<EmployeeDocumentDto>> UploadAsync(
        Guid employeeId,
        EmployeeDocumentRequest request,
        string fileName,
        long fileSize,
        string contentType,
        string uploadedBy,
        CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<EmployeeDocumentDto>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<EmployeeDocumentDto>.NotFound(NotFoundMessage);
        }

        var document = new EmployeeDocument
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            DocumentName = request.DocumentName.Trim(),
            DocumentCategory = request.DocumentCategory,
            DocumentNumber = Normalize(request.DocumentNumber),
            FilePath = request.FilePath,
            FileSize = fileSize,
            ContentType = contentType,
            UploadedBy = Normalize(uploadedBy)
        };

        _db.EmployeeDocuments.Add(document);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Uploaded document {DocumentId} ({DocumentName}) for employee {EmployeeId}.",
            document.Id, document.DocumentName, employeeId);

        var dto = new EmployeeDocumentDto(
            document.Id,
            document.DocumentName,
            document.DocumentCategory,
            document.DocumentNumber,
            document.FilePath,
            document.FileSize,
            document.ContentType,
            document.UploadedBy,
            document.CreatedDate);

        return Result<EmployeeDocumentDto>.Success(dto, "Document uploaded.");
    }

    public async Task<Result<bool>> DeleteAsync(
        Guid employeeId, Guid documentId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<bool>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<bool>.NotFound(NotFoundMessage);
        }

        var document = await _db.EmployeeDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId && d.EmployeeId == employeeId, cancellationToken);

        if (document is null)
        {
            return Result<bool>.NotFound("Document not found.");
        }

        _db.EmployeeDocuments.Remove(document);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _logger.LogWarning("Document {DocumentId} for employee {EmployeeId} could not be deleted.", documentId, employeeId);
            return Result<bool>.Conflict("This document is still referenced and cannot be deleted.");
        }

        _logger.LogInformation("Deleted document {DocumentId} for employee {EmployeeId} in tenant {TenantId}.",
            documentId, employeeId, tenantId);

        return Result<bool>.Success(true, "Document deleted.");
    }

    private async Task<bool> EmployeeExistsAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        return await _db.Employees.AsNoTracking().AnyAsync(e => e.Id == employeeId, cancellationToken);
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
