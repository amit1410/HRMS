using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

public class ImportBatchService : IImportBatchService
{
    private const string NoTenantMessage = "No authenticated tenant.";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ImportBatchService> _logger;

    public ImportBatchService(
        IHrmsDbContext db,
        ITenantContext tenantContext,
        ILogger<ImportBatchService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<ImportBatchDto>>> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<IReadOnlyList<ImportBatchDto>>.Unauthorized(NoTenantMessage);
        }

        var batches = await _db.ImportBatches.AsNoTracking()
            .OrderByDescending(b => b.CreatedDate)
            .Select(b => new ImportBatchDto(
                b.Id,
                b.FileName,
                b.ImportedBy,
                b.TotalRows,
                b.SuccessfulRows,
                b.FailedRows,
                b.SkippedRows,
                b.Status,
                b.StartedAtUtc,
                b.CompletedAtUtc,
                b.Message,
                b.CreatedDate))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<ImportBatchDto>>.Success(batches);
    }

    public async Task<Result<ImportBatchDto>> GetByIdAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<ImportBatchDto>.Unauthorized(NoTenantMessage);
        }

        var batch = await _db.ImportBatches.AsNoTracking()
            .Where(b => b.Id == batchId)
            .Select(b => new ImportBatchDto(
                b.Id,
                b.FileName,
                b.ImportedBy,
                b.TotalRows,
                b.SuccessfulRows,
                b.FailedRows,
                b.SkippedRows,
                b.Status,
                b.StartedAtUtc,
                b.CompletedAtUtc,
                b.Message,
                b.CreatedDate))
            .FirstOrDefaultAsync(cancellationToken);

        return batch is null
            ? Result<ImportBatchDto>.NotFound("Import batch not found.")
            : Result<ImportBatchDto>.Success(batch);
    }

    public async Task<Result<ImportBatchDto>> ImportAsync(
        string fileName, string importedBy, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<ImportBatchDto>.Unauthorized(NoTenantMessage);
        }

        var batch = new ImportBatch
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FileName = Normalize(fileName),
            ImportedBy = importedBy,
            TotalRows = 0,
            SuccessfulRows = 0,
            FailedRows = 0,
            SkippedRows = 0,
            Status = "Pending",
            StartedAtUtc = DateTime.UtcNow
        };

        _db.ImportBatches.Add(batch);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created import batch {BatchId} by {ImportedBy} in tenant {TenantId}.",
            batch.Id, importedBy, tenantId);

        var dto = new ImportBatchDto(
            batch.Id,
            batch.FileName,
            batch.ImportedBy,
            batch.TotalRows,
            batch.SuccessfulRows,
            batch.FailedRows,
            batch.SkippedRows,
            batch.Status,
            batch.StartedAtUtc,
            batch.CompletedAtUtc,
            batch.Message,
            batch.CreatedDate);

        return Result<ImportBatchDto>.Success(dto, "Import batch created.");
    }

    public async Task<Result<bool>> DeleteAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<bool>.Unauthorized(NoTenantMessage);
        }

        var batch = await _db.ImportBatches
            .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);

        if (batch is null)
        {
            return Result<bool>.NotFound("Import batch not found.");
        }

        if (batch.Status == "Processing")
        {
            return Result<bool>.Conflict("Cannot delete an import batch that is currently processing.");
        }

        _db.ImportBatches.Remove(batch);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _logger.LogWarning("Import batch {BatchId} could not be deleted.", batchId);
            return Result<bool>.Conflict("This import batch is still referenced and cannot be deleted.");
        }

        _logger.LogInformation("Deleted import batch {BatchId} in tenant {TenantId}.", batchId, tenantId);

        return Result<bool>.Success(true, "Import batch deleted.");
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
