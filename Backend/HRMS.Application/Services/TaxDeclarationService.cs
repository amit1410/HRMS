using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>Read/write declaration workflow. It stores evidence and approved inputs; it never calculates tax.</summary>
public sealed class TaxDeclarationService(IHrmsDbContext db, ITenantContext tenant, IEmployeeIdentityResolver identity, TimeProvider clock) : ITaxDeclarationService
{
    public async Task<Result<IReadOnlyList<TaxDeclarationCycleDto>>> GetCyclesAsync(CancellationToken ct = default)
    {
        if (!Tenant(out var tenantId, out _)) return Result<IReadOnlyList<TaxDeclarationCycleDto>>.Unauthorized("An authenticated tenant is required.");
        var rows = await db.TaxDeclarationCycles.AsNoTracking().Where(x => x.TenantId == tenantId).OrderByDescending(x => x.FinancialYear).ThenBy(x => x.Code).Select(x => new TaxDeclarationCycleDto(x.Id, x.Code, x.Name, x.FinancialYear, x.DeclarationOpenDate, x.DeclarationCloseDate, x.ProofSubmissionOpenDate, x.ProofSubmissionCloseDate, x.Status)).ToListAsync(ct);
        return Result<IReadOnlyList<TaxDeclarationCycleDto>>.Success(rows);
    }

    public async Task<Result<TaxDeclarationCycleDto>> CreateCycleAsync(TaxDeclarationCycleRequest request, CancellationToken ct = default)
    {
        if (!Tenant(out var tenantId, out var userId)) return Result<TaxDeclarationCycleDto>.Unauthorized("An authenticated tenant is required.");
        if (string.IsNullOrWhiteSpace(request.Code) || request.DeclarationCloseDate < request.DeclarationOpenDate || request.ProofSubmissionCloseDate < request.ProofSubmissionOpenDate) return Result<TaxDeclarationCycleDto>.Invalid("cycle", "Cycle dates are invalid.");
        var exists = await db.TaxDeclarationCycles.AnyAsync(x => x.TenantId == tenantId && x.Code == request.Code.Trim(), ct); if (exists) return Result<TaxDeclarationCycleDto>.Conflict("A declaration cycle with this code already exists.");
        var cycle = new TaxDeclarationCycle { Id = Guid.NewGuid(), TenantId = tenantId, Code = request.Code.Trim(), Name = request.Name.Trim(), FinancialYear = request.FinancialYear, DeclarationOpenDate = request.DeclarationOpenDate, DeclarationCloseDate = request.DeclarationCloseDate, ProofSubmissionOpenDate = request.ProofSubmissionOpenDate, ProofSubmissionCloseDate = request.ProofSubmissionCloseDate, EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo, Status = request.Status, CreatedByUserId = userId };
        db.TaxDeclarationCycles.Add(cycle); await db.SaveChangesAsync(ct); return Result<TaxDeclarationCycleDto>.Success(ToCycle(cycle));
    }

    public async Task<Result<TaxDeclarationCategoryDto>> CreateCategoryAsync(TaxDeclarationCategoryRequest request, CancellationToken ct = default)
    {
        if (!Tenant(out var tenantId, out _)) return Result<TaxDeclarationCategoryDto>.Unauthorized("An authenticated tenant is required.");
        if (await db.TaxDeclarationCategories.AnyAsync(x => x.TenantId == tenantId && x.Code == request.Code.Trim(), ct)) return Result<TaxDeclarationCategoryDto>.Conflict("A declaration category with this code already exists.");
        var row = new TaxDeclarationCategory { Id = Guid.NewGuid(), TenantId = tenantId, Code = request.Code.Trim(), Name = request.Name.Trim(), Description = request.Description?.Trim(), CategoryType = request.CategoryType, RequiresProof = request.RequiresProof, AllowsMultipleEntries = request.AllowsMultipleEntries, DisplayOrder = request.DisplayOrder, EffectiveFrom = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) };
        db.TaxDeclarationCategories.Add(row); await db.SaveChangesAsync(ct); return Result<TaxDeclarationCategoryDto>.Success(ToCategory(row));
    }

    public async Task<Result<TaxDeclarationItemDto>> CreateItemAsync(TaxDeclarationItemRequest request, CancellationToken ct = default)
    {
        if (!Tenant(out var tenantId, out _)) return Result<TaxDeclarationItemDto>.Unauthorized("An authenticated tenant is required.");
        var category = await db.TaxDeclarationCategories.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.CategoryId, ct); if (category is null) return Result<TaxDeclarationItemDto>.NotFound("Declaration category was not found.");
        if (await db.TaxDeclarationItems.AnyAsync(x => x.TenantId == tenantId && x.TaxDeclarationCategoryId == request.CategoryId && x.Code == request.Code.Trim(), ct)) return Result<TaxDeclarationItemDto>.Conflict("A declaration item with this code already exists.");
        var row = new TaxDeclarationItem { Id = Guid.NewGuid(), TenantId = tenantId, TaxDeclarationCategoryId = category.Id, Code = request.Code.Trim(), Name = request.Name.Trim(), Description = request.Description?.Trim(), RequiresProof = request.RequiresProof, PayrollTaxInputCode = request.PayrollTaxInputCode?.Trim(), EffectiveFrom = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) };
        db.TaxDeclarationItems.Add(row); await db.SaveChangesAsync(ct); return Result<TaxDeclarationItemDto>.Success(ToItem(row));
    }

    public async Task<Result<EmployeeTaxDeclarationDto>> GetOwnAsync(CancellationToken ct = default)
    {
        var subject = await Subject(ct); if (!subject.Succeeded || subject.Value is null) return Result<EmployeeTaxDeclarationDto>.Failure(subject.Status, subject.Message, subject.Errors);
        var declaration = await db.EmployeeTaxDeclarations.Where(x => x.TenantId == subject.Value.TenantId && x.EmployeeId == subject.Value.EmployeeId).OrderByDescending(x => x.CreatedDate).FirstOrDefaultAsync(ct);
        return declaration is null ? Result<EmployeeTaxDeclarationDto>.NotFound("No tax declaration exists for the employee.") : await GetAsync(declaration.Id, subject.Value.TenantId, ct);
    }

    public async Task<Result<EmployeeTaxDeclarationDto>> CreateOwnAsync(Guid cycleId, CancellationToken ct = default)
    {
        var subject = await Subject(ct); if (!subject.Succeeded || subject.Value is null) return Result<EmployeeTaxDeclarationDto>.Failure(subject.Status, subject.Message, subject.Errors);
        var cycle = await db.TaxDeclarationCycles.SingleOrDefaultAsync(x => x.TenantId == subject.Value.TenantId && x.Id == cycleId, ct); if (cycle is null) return Result<EmployeeTaxDeclarationDto>.NotFound("Declaration cycle was not found.");
        if (cycle.Status is not (TaxDeclarationCycleStatus.Open or TaxDeclarationCycleStatus.ProofSubmissionOpen)) return Result<EmployeeTaxDeclarationDto>.Conflict("The declaration cycle is not open.");
        if (await db.EmployeeTaxDeclarations.AnyAsync(x => x.TenantId == subject.Value.TenantId && x.EmployeeId == subject.Value.EmployeeId && x.TaxDeclarationCycleId == cycleId, ct)) return Result<EmployeeTaxDeclarationDto>.Conflict("A declaration already exists for this cycle.");
        var declaration = new EmployeeTaxDeclaration { Id = Guid.NewGuid(), TenantId = subject.Value.TenantId, EmployeeId = subject.Value.EmployeeId, TaxDeclarationCycleId = cycleId };
        db.EmployeeTaxDeclarations.Add(declaration); AddAudit(declaration, TaxDeclarationAuditAction.Created, subject.Value.UserId, null, null, null); await db.SaveChangesAsync(ct); return await GetAsync(declaration.Id, subject.Value.TenantId, ct);
    }

    public async Task<Result<EmployeeTaxDeclarationDto>> AddLineOwnAsync(TaxDeclarationLineRequest request, CancellationToken ct = default)
    {
        var subject = await Subject(ct); if (!subject.Succeeded || subject.Value is null) return Result<EmployeeTaxDeclarationDto>.Failure(subject.Status, subject.Message, subject.Errors);
        var declaration = await db.EmployeeTaxDeclarations.SingleOrDefaultAsync(x => x.TenantId == subject.Value.TenantId && x.EmployeeId == subject.Value.EmployeeId && (x.Status == EmployeeTaxDeclarationStatus.Draft || x.Status == EmployeeTaxDeclarationStatus.ResubmissionRequired), ct); if (declaration is null) return Result<EmployeeTaxDeclarationDto>.Conflict("Only a draft or resubmission can be edited.");
        var item = await db.TaxDeclarationItems.SingleOrDefaultAsync(x => x.TenantId == subject.Value.TenantId && x.Id == request.ItemId && x.TaxDeclarationCategoryId == request.CategoryId && x.Active, ct); if (item is null) return Result<EmployeeTaxDeclarationDto>.NotFound("Declaration item was not found.");
        var line = new EmployeeTaxDeclarationLine { Id = Guid.NewGuid(), TenantId = subject.Value.TenantId, EmployeeTaxDeclarationId = declaration.Id, TaxDeclarationCategoryId = request.CategoryId, TaxDeclarationItemId = request.ItemId, DeclaredAmount = request.DeclaredAmount, ReferenceNumber = request.ReferenceNumber?.Trim(), DeclarationDate = request.DeclarationDate, Notes = request.Notes?.Trim(), Status = EmployeeTaxDeclarationLineStatus.Draft };
        db.EmployeeTaxDeclarationLines.Add(line); AddAudit(declaration, TaxDeclarationAuditAction.LineAdded, subject.Value.UserId, line.Id, null, request.DeclaredAmount.ToString()); await db.SaveChangesAsync(ct); return await GetAsync(declaration.Id, subject.Value.TenantId, ct);
    }

    public async Task<Result<EmployeeTaxDeclarationDto>> UpdateLineOwnAsync(Guid declarationId, Guid lineId, TaxDeclarationLineUpdateRequest request, CancellationToken ct = default)
    {
        var subject = await Subject(ct); if (!subject.Succeeded || subject.Value is null) return Result<EmployeeTaxDeclarationDto>.Failure(subject.Status, subject.Message, subject.Errors);
        var declaration = await db.EmployeeTaxDeclarations.Include(x => x.Lines).SingleOrDefaultAsync(x => x.TenantId == subject.Value.TenantId && x.Id == declarationId && x.EmployeeId == subject.Value.EmployeeId, ct);
        if (declaration is null) return Result<EmployeeTaxDeclarationDto>.NotFound("Declaration was not found.");
        var line = declaration.Lines.SingleOrDefault(x => x.Id == lineId); if (line is null) return Result<EmployeeTaxDeclarationDto>.NotFound("Declaration line was not found.");
        var editable = declaration.Status == EmployeeTaxDeclarationStatus.Draft || (declaration.Status == EmployeeTaxDeclarationStatus.ResubmissionRequired && line.Status is EmployeeTaxDeclarationLineStatus.Rejected or EmployeeTaxDeclarationLineStatus.ResubmissionRequired);
        if (!editable) return Result<EmployeeTaxDeclarationDto>.Conflict("The declaration line is not editable in its current state.");
        if (request.DeclaredAmount < 0) return Result<EmployeeTaxDeclarationDto>.Invalid("declaredAmount", "Declared amount cannot be negative.");
        var old = $"{line.DeclaredAmount}|{line.ReferenceNumber}|{line.DeclarationDate}|{line.Notes}";
        line.DeclaredAmount = request.DeclaredAmount; line.ReferenceNumber = request.ReferenceNumber?.Trim(); line.DeclarationDate = request.DeclarationDate; line.Notes = request.Notes?.Trim(); line.Status = EmployeeTaxDeclarationLineStatus.Draft; declaration.ConcurrencyVersion++; declaration.Version++;
        AddAudit(declaration, TaxDeclarationAuditAction.LineUpdated, subject.Value.UserId, line.Id, old, $"{line.DeclaredAmount}|{line.ReferenceNumber}|{line.DeclarationDate}|{line.Notes}");
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<EmployeeTaxDeclarationDto>.Conflict("The declaration changed while the line was being updated."); }
        return await GetAsync(declaration.Id, subject.Value.TenantId, ct);
    }

    public async Task<Result<EmployeeTaxDeclarationDto>> DeleteLineOwnAsync(Guid declarationId, Guid lineId, CancellationToken ct = default)
    {
        var subject = await Subject(ct); if (!subject.Succeeded || subject.Value is null) return Result<EmployeeTaxDeclarationDto>.Failure(subject.Status, subject.Message, subject.Errors);
        var declaration = await db.EmployeeTaxDeclarations.Include(x => x.Lines).SingleOrDefaultAsync(x => x.TenantId == subject.Value.TenantId && x.Id == declarationId && x.EmployeeId == subject.Value.EmployeeId, ct);
        if (declaration is null) return Result<EmployeeTaxDeclarationDto>.NotFound("Declaration was not found.");
        var line = declaration.Lines.SingleOrDefault(x => x.Id == lineId); if (line is null) return Result<EmployeeTaxDeclarationDto>.NotFound("Declaration line was not found.");
        if (declaration.Status != EmployeeTaxDeclarationStatus.Draft) return Result<EmployeeTaxDeclarationDto>.Conflict("Only draft declaration lines can be deleted.");
        db.EmployeeTaxDeclarationLines.Remove(line); declaration.ConcurrencyVersion++; declaration.Version++; AddAudit(declaration, TaxDeclarationAuditAction.LineDeleted, subject.Value.UserId, line.Id, line.DeclaredAmount.ToString(), null);
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<EmployeeTaxDeclarationDto>.Conflict("The declaration changed while the line was being deleted."); }
        return await GetAsync(declaration.Id, subject.Value.TenantId, ct);
    }

    public async Task<Result<EmployeeTaxDeclarationDto>> SubmitOwnAsync(CancellationToken ct = default)
    {
        var subject = await Subject(ct); if (!subject.Succeeded || subject.Value is null) return Result<EmployeeTaxDeclarationDto>.Failure(subject.Status, subject.Message, subject.Errors);
        var declaration = await db.EmployeeTaxDeclarations.Include(x => x.Lines).ThenInclude(x => x.Item).SingleOrDefaultAsync(x => x.TenantId == subject.Value.TenantId && x.EmployeeId == subject.Value.EmployeeId, ct); if (declaration is null) return Result<EmployeeTaxDeclarationDto>.NotFound("Declaration was not found.");
        if (declaration.Status is not (EmployeeTaxDeclarationStatus.Draft or EmployeeTaxDeclarationStatus.ResubmissionRequired)) return Result<EmployeeTaxDeclarationDto>.Conflict("The declaration cannot be submitted in its current state.");
        if (declaration.Lines.Count == 0) return Result<EmployeeTaxDeclarationDto>.Invalid("lines", "At least one declaration line is required.");
        var required = declaration.Lines.Any(x => x.Item!.RequiresProof && !db.TaxDeclarationProofs.Any(p => p.TenantId == subject.Value.TenantId && p.EmployeeTaxDeclarationLineId == x.Id && p.Status != TaxDeclarationProofStatus.Rejected && p.Status != TaxDeclarationProofStatus.Replaced)); if (required) return Result<EmployeeTaxDeclarationDto>.Conflict("Every proof-required line must have a proof.");
        var resubmission = declaration.Status == EmployeeTaxDeclarationStatus.ResubmissionRequired;
        foreach (var line in declaration.Lines) line.Status = EmployeeTaxDeclarationLineStatus.Submitted;
        declaration.Status = EmployeeTaxDeclarationStatus.Submitted; declaration.SubmittedAtUtc = clock.GetUtcNow().UtcDateTime; declaration.SubmittedByUserId = subject.Value.UserId; declaration.Version++; declaration.ConcurrencyVersion++;
        AddAudit(declaration, resubmission ? TaxDeclarationAuditAction.Resubmitted : TaxDeclarationAuditAction.Submitted, subject.Value.UserId, null, null, declaration.Version.ToString()); try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<EmployeeTaxDeclarationDto>.Conflict("The declaration changed while it was being submitted."); } return await GetAsync(declaration.Id, subject.Value.TenantId, ct);
    }

    public async Task<Result<EmployeeTaxDeclarationDto>> ResubmitOwnAsync(Guid declarationId, CancellationToken ct = default)
    {
        var subject = await Subject(ct); if (!subject.Succeeded || subject.Value is null) return Result<EmployeeTaxDeclarationDto>.Failure(subject.Status, subject.Message, subject.Errors);
        var owns = await db.EmployeeTaxDeclarations.AnyAsync(x => x.TenantId == subject.Value.TenantId && x.Id == declarationId && x.EmployeeId == subject.Value.EmployeeId && x.Status == EmployeeTaxDeclarationStatus.ResubmissionRequired, ct);
        return owns ? await SubmitOwnAsync(ct) : Result<EmployeeTaxDeclarationDto>.Conflict("Only your resubmission-required declaration can be resubmitted.");
    }

    public async Task<Result<TaxDeclarationProofDto>> AddProofOwnAsync(TaxDeclarationProofRequest request, CancellationToken ct = default)
    {
        var subject = await Subject(ct); if (!subject.Succeeded || subject.Value is null) return Result<TaxDeclarationProofDto>.Failure(subject.Status, subject.Message, subject.Errors);
        var line = await db.EmployeeTaxDeclarationLines.Include(x => x.Declaration).SingleOrDefaultAsync(x => x.TenantId == subject.Value.TenantId && x.Id == request.LineId && x.Declaration!.EmployeeId == subject.Value.EmployeeId, ct); if (line is null) return Result<TaxDeclarationProofDto>.NotFound("Declaration line was not found.");
        if (line.Declaration!.Status is not (EmployeeTaxDeclarationStatus.Draft or EmployeeTaxDeclarationStatus.ResubmissionRequired)) return Result<TaxDeclarationProofDto>.Conflict("Proof cannot be changed after submission.");
        var proof = new TaxDeclarationProof { Id = Guid.NewGuid(), TenantId = subject.Value.TenantId, EmployeeTaxDeclarationLineId = line.Id, FileName = request.FileName.Trim(), ContentType = request.ContentType.Trim(), FileSize = request.FileSize, StorageReference = request.StorageReference.Trim(), UploadedByUserId = subject.Value.UserId, UploadedAtUtc = clock.GetUtcNow().UtcDateTime, DocumentType = request.DocumentType?.Trim(), Hash = request.Hash?.Trim() };
        db.TaxDeclarationProofs.Add(proof); AddAudit(line.Declaration, TaxDeclarationAuditAction.ProofUploaded, subject.Value.UserId, line.Id, null, proof.FileName); await db.SaveChangesAsync(ct); return Result<TaxDeclarationProofDto>.Success(ToProof(proof));
    }

    public async Task<Result<TaxDeclarationProofDto>> ReplaceProofOwnAsync(Guid declarationId, Guid lineId, Guid proofId, TaxDeclarationProofRequest request, CancellationToken ct = default)
    {
        var subject = await Subject(ct); if (!subject.Succeeded || subject.Value is null) return Result<TaxDeclarationProofDto>.Failure(subject.Status, subject.Message, subject.Errors);
        var line = await db.EmployeeTaxDeclarationLines.Include(x => x.Declaration).Include(x => x.Proofs).SingleOrDefaultAsync(x => x.TenantId == subject.Value.TenantId && x.Id == lineId && x.EmployeeTaxDeclarationId == declarationId && x.Declaration!.EmployeeId == subject.Value.EmployeeId, ct);
        if (line is null) return Result<TaxDeclarationProofDto>.NotFound("Declaration line was not found.");
        if (line.Declaration!.Status != EmployeeTaxDeclarationStatus.ResubmissionRequired) return Result<TaxDeclarationProofDto>.Conflict("Proof replacement is only available during resubmission.");
        var old = line.Proofs.SingleOrDefault(x => x.Id == proofId); if (old is null) return Result<TaxDeclarationProofDto>.NotFound("Proof was not found.");
        if (old.Status != TaxDeclarationProofStatus.Rejected) return Result<TaxDeclarationProofDto>.Conflict("Only a rejected proof can be replaced.");
        old.Status = TaxDeclarationProofStatus.Replaced;
        var replacement = new TaxDeclarationProof { Id = Guid.NewGuid(), TenantId = subject.Value.TenantId, EmployeeTaxDeclarationLineId = line.Id, FileName = request.FileName.Trim(), ContentType = request.ContentType.Trim(), FileSize = request.FileSize, StorageReference = request.StorageReference.Trim(), UploadedByUserId = subject.Value.UserId, UploadedAtUtc = clock.GetUtcNow().UtcDateTime, DocumentType = request.DocumentType?.Trim(), Hash = request.Hash?.Trim() };
        db.TaxDeclarationProofs.Add(replacement); line.Status = EmployeeTaxDeclarationLineStatus.ResubmissionRequired; line.Declaration.ConcurrencyVersion++; line.Declaration.Version++; AddAudit(line.Declaration, TaxDeclarationAuditAction.ProofReplaced, subject.Value.UserId, line.Id, old.FileName, replacement.FileName);
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<TaxDeclarationProofDto>.Conflict("The declaration changed while proof was being replaced."); }
        return Result<TaxDeclarationProofDto>.Success(ToProof(replacement));
    }

    public async Task<Result<PagedResult<EmployeeTaxDeclarationDto>>> GetReviewAsync(TaxDeclarationReviewQuery query, CancellationToken ct = default)
    {
        if (!Tenant(out var tenantId, out _)) return Result<PagedResult<EmployeeTaxDeclarationDto>>.Unauthorized("An authenticated tenant is required.");
        var source = db.EmployeeTaxDeclarations.Where(x => x.TenantId == tenantId && (query.CycleId == null || x.TaxDeclarationCycleId == query.CycleId) && (query.Status == null || x.Status == query.Status) && (query.EmployeeId == null || x.EmployeeId == query.EmployeeId)).OrderBy(x => x.Status).ThenBy(x => x.CreatedDate);
        var total = await source.CountAsync(ct); var ids = await source.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).Select(x => x.Id).ToListAsync(ct); var rows = new List<EmployeeTaxDeclarationDto>(); foreach (var id in ids) { var row = await GetAsync(id, tenantId, ct); if (row.Succeeded && row.Value is not null) rows.Add(row.Value); }
        return Result<PagedResult<EmployeeTaxDeclarationDto>>.Success(new(rows, query.Page, query.PageSize, total));
    }

    public async Task<Result<EmployeeTaxDeclarationDto>> ReviewLineAsync(Guid declarationId, TaxDeclarationReviewRequest request, CancellationToken ct = default)
    {
        if (!Tenant(out var tenantId, out var userId)) return Result<EmployeeTaxDeclarationDto>.Unauthorized("An authenticated tenant is required.");
        var declaration = await db.EmployeeTaxDeclarations.Include(x => x.Lines).SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == declarationId, ct); if (declaration is null) return Result<EmployeeTaxDeclarationDto>.NotFound("Declaration was not found.");
        var line = declaration.Lines.SingleOrDefault(x => x.Id == request.LineId); if (line is null) return Result<EmployeeTaxDeclarationDto>.NotFound("Declaration line was not found.");
        if (declaration.Status is EmployeeTaxDeclarationStatus.Draft or EmployeeTaxDeclarationStatus.Locked) return Result<EmployeeTaxDeclarationDto>.Conflict("The declaration is not reviewable.");
        if (request.ApprovedAmount.HasValue && (request.ApprovedAmount.Value < 0 || request.ApprovedAmount.Value > line.DeclaredAmount)) return Result<EmployeeTaxDeclarationDto>.Invalid("approvedAmount", "Approved amount must be between zero and the declared amount.");
        line.ApprovedAmount = request.Decision == EmployeeTaxDeclarationLineStatus.Rejected ? 0m : request.ApprovedAmount ?? line.DeclaredAmount; line.Status = request.Decision; line.ReviewerComment = request.Comment?.Trim(); declaration.Status = EmployeeTaxDeclarationStatus.UnderReview; declaration.ReviewedByUserId = userId; declaration.ReviewedAtUtc = clock.GetUtcNow().UtcDateTime; declaration.ConcurrencyVersion++; AddAudit(declaration, request.Decision == EmployeeTaxDeclarationLineStatus.Approved ? TaxDeclarationAuditAction.LineApproved : request.Decision == EmployeeTaxDeclarationLineStatus.PartiallyApproved ? TaxDeclarationAuditAction.LinePartiallyApproved : TaxDeclarationAuditAction.LineRejected, userId, line.Id, null, line.ApprovedAmount.ToString()); try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<EmployeeTaxDeclarationDto>.Conflict("The declaration changed while it was being reviewed."); } return await GetAsync(declaration.Id, tenantId, ct);
    }

    public async Task<Result<TaxDeclarationProofDto>> ReviewProofAsync(Guid declarationId, Guid lineId, Guid proofId, TaxDeclarationProofReviewRequest request, CancellationToken ct = default)
    {
        if (!Tenant(out var tenantId, out var userId)) return Result<TaxDeclarationProofDto>.Unauthorized("An authenticated tenant is required.");
        if (request.Decision is not (TaxDeclarationProofStatus.Accepted or TaxDeclarationProofStatus.Rejected)) return Result<TaxDeclarationProofDto>.Invalid("decision", "Proof decision must be Accepted or Rejected.");
        var proof = await db.TaxDeclarationProofs.Include(x => x.Line).ThenInclude(x => x!.Declaration).SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == proofId && x.EmployeeTaxDeclarationLineId == lineId && x.Line!.EmployeeTaxDeclarationId == declarationId, ct);
        if (proof is null) return Result<TaxDeclarationProofDto>.NotFound("Proof was not found.");
        if (proof.Line!.Declaration!.Status is EmployeeTaxDeclarationStatus.Locked or EmployeeTaxDeclarationStatus.Approved) return Result<TaxDeclarationProofDto>.Conflict("Proof cannot be reviewed after approval or lock.");
        proof.Status = request.Decision; proof.ReviewedByUserId = userId; proof.ReviewedAtUtc = clock.GetUtcNow().UtcDateTime; proof.ReviewerComment = request.Comment?.Trim(); if (request.Decision == TaxDeclarationProofStatus.Rejected) proof.Line.Status = EmployeeTaxDeclarationLineStatus.ResubmissionRequired; proof.Line.Declaration.ConcurrencyVersion++;
        AddAudit(proof.Line.Declaration, request.Decision == TaxDeclarationProofStatus.Accepted ? TaxDeclarationAuditAction.ProofAccepted : TaxDeclarationAuditAction.ProofRejected, userId, lineId, null, proof.Status.ToString(), request.Comment);
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<TaxDeclarationProofDto>.Conflict("The declaration changed while proof was being reviewed."); }
        return Result<TaxDeclarationProofDto>.Success(ToProof(proof));
    }

    public Task<Result<EmployeeTaxDeclarationDto>> ApproveAsync(Guid declarationId, CancellationToken ct = default) => TransitionAsync(declarationId, EmployeeTaxDeclarationStatus.Approved, TaxDeclarationAuditAction.Approved, ct);
    public Task<Result<EmployeeTaxDeclarationDto>> RequestResubmissionAsync(Guid declarationId, string? comment, CancellationToken ct = default) => TransitionAsync(declarationId, EmployeeTaxDeclarationStatus.ResubmissionRequired, TaxDeclarationAuditAction.ResubmissionRequested, ct, comment);
    public Task<Result<EmployeeTaxDeclarationDto>> LockAsync(Guid declarationId, CancellationToken ct = default) => TransitionAsync(declarationId, EmployeeTaxDeclarationStatus.Locked, TaxDeclarationAuditAction.Locked, ct);
    public Task<Result<EmployeeTaxDeclarationDto>> ReopenAsync(Guid declarationId, string? reason, CancellationToken ct = default) => TransitionAsync(declarationId, EmployeeTaxDeclarationStatus.ResubmissionRequired, TaxDeclarationAuditAction.Reopened, ct, reason);

    public async Task<Result<IReadOnlyList<TaxDeclarationAuditDto>>> GetAuditAsync(Guid declarationId, CancellationToken ct = default)
    {
        if (!Tenant(out var tenantId, out _)) return Result<IReadOnlyList<TaxDeclarationAuditDto>>.Unauthorized("An authenticated tenant is required.");
        var rows = await db.TaxDeclarationAuditEvents.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeTaxDeclarationId == declarationId).OrderBy(x => x.OccurredAtUtc).Select(x => new TaxDeclarationAuditDto(x.Id, x.EmployeeTaxDeclarationLineId, x.Action, x.ActorUserId, x.OccurredAtUtc, x.OldValue, x.NewValue, x.Comment)).ToListAsync(ct); return Result<IReadOnlyList<TaxDeclarationAuditDto>>.Success(rows);
    }

    public async Task<Result<IReadOnlyList<ApprovedTaxDeclarationInput>>> ResolveApprovedAsync(Guid employeeId, int financialYear, DateOnly asOfDate, CancellationToken ct = default)
    {
        if (!Tenant(out var tenantId, out _)) return Result<IReadOnlyList<ApprovedTaxDeclarationInput>>.Unauthorized("An authenticated tenant is required.");
        var rows = await db.EmployeeTaxDeclarations.AsNoTracking().Include(x => x.Cycle).Include(x => x.Lines).ThenInclude(x => x.Item).Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Cycle!.FinancialYear == financialYear && x.Cycle.EffectiveFrom <= asOfDate && (x.Cycle.EffectiveTo == null || x.Cycle.EffectiveTo >= asOfDate) && (x.Status == EmployeeTaxDeclarationStatus.Approved || x.Status == EmployeeTaxDeclarationStatus.PartiallyApproved || x.Status == EmployeeTaxDeclarationStatus.Locked)).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
        if (rows is null) return Result<IReadOnlyList<ApprovedTaxDeclarationInput>>.Success([]);
        return Result<IReadOnlyList<ApprovedTaxDeclarationInput>>.Success(rows.Lines.Where(x => (x.Status == EmployeeTaxDeclarationLineStatus.Approved || x.Status == EmployeeTaxDeclarationLineStatus.PartiallyApproved) && x.ApprovedAmount > 0).Select(x => new ApprovedTaxDeclarationInput(rows.Id, x.Id, x.TaxDeclarationItemId, x.Item!.Code, x.ApprovedAmount ?? 0m, x.ReferenceNumber, rows.Version, rows.ReviewedAtUtc)).ToList());
    }

    private async Task<Result<EmployeeTaxDeclarationDto>> TransitionAsync(Guid id, EmployeeTaxDeclarationStatus status, TaxDeclarationAuditAction action, CancellationToken ct, string? comment = null)
    {
        if (!Tenant(out var tenantId, out var userId)) return Result<EmployeeTaxDeclarationDto>.Unauthorized("An authenticated tenant is required.");
        var declaration = await db.EmployeeTaxDeclarations.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct); if (declaration is null) return Result<EmployeeTaxDeclarationDto>.NotFound("Declaration was not found.");
        if (status == EmployeeTaxDeclarationStatus.Approved && declaration.Status is not (EmployeeTaxDeclarationStatus.Submitted or EmployeeTaxDeclarationStatus.UnderReview or EmployeeTaxDeclarationStatus.PartiallyApproved)) return Result<EmployeeTaxDeclarationDto>.Conflict("The declaration is not ready for approval.");
        if (status == EmployeeTaxDeclarationStatus.Locked && declaration.Status != EmployeeTaxDeclarationStatus.Approved) return Result<EmployeeTaxDeclarationDto>.Conflict("Only an approved declaration can be locked.");
        if (status == EmployeeTaxDeclarationStatus.ResubmissionRequired && action == TaxDeclarationAuditAction.Reopened && declaration.Status != EmployeeTaxDeclarationStatus.Locked) return Result<EmployeeTaxDeclarationDto>.Conflict("Only a locked declaration can be reopened.");
        var old = declaration.Status.ToString(); declaration.Status = status; declaration.ReviewedAtUtc = clock.GetUtcNow().UtcDateTime; declaration.ReviewedByUserId = userId; declaration.ConcurrencyVersion++; if (status == EmployeeTaxDeclarationStatus.Locked) { declaration.LockedAtUtc = declaration.ReviewedAtUtc; declaration.LockedByUserId = userId; } AddAudit(declaration, action, userId, null, old, status.ToString(), comment); try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<EmployeeTaxDeclarationDto>.Conflict("The declaration changed while the command was in progress."); } return await GetAsync(id, tenantId, ct);
    }

    private async Task<Result<EmployeeTaxDeclarationDto>> GetAsync(Guid id, Guid tenantId, CancellationToken ct)
    {
        var x = await db.EmployeeTaxDeclarations.AsNoTracking().Include(x => x.Cycle).Include(x => x.Lines).ThenInclude(x => x.Category).Include(x => x.Lines).ThenInclude(x => x.Item).Include(x => x.Lines).ThenInclude(x => x.Proofs).SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct); return x is null ? Result<EmployeeTaxDeclarationDto>.NotFound("Declaration was not found.") : Result<EmployeeTaxDeclarationDto>.Success(new(x.Id, x.EmployeeId, x.TaxDeclarationCycleId, x.Cycle!.Code, x.Cycle.FinancialYear, x.Status, x.Version, x.SubmittedAtUtc, x.ReviewedAtUtc, x.Lines.OrderBy(l => l.CreatedDate).Select(l => new TaxDeclarationLineDto(l.Id, l.TaxDeclarationCategoryId, l.Category!.Code, l.TaxDeclarationItemId, l.Item!.Code, l.DeclaredAmount, l.ApprovedAmount, l.ReferenceNumber, l.DeclarationDate, l.Notes, l.Status, l.ReviewerComment, l.Proofs.Select(ToProof).ToList())).ToList()));
    }

    private async Task<Result<RuntimeEmployeeIdentity>> Subject(CancellationToken ct) => await identity.ResolveCurrentAsync(ct);
    private bool Tenant(out Guid tenantId, out Guid userId) { tenantId = tenant.TenantId ?? Guid.Empty; userId = tenant.UserId ?? Guid.Empty; return tenant.TenantId.HasValue; }
    private void AddAudit(EmployeeTaxDeclaration d, TaxDeclarationAuditAction action, Guid userId, Guid? lineId, string? oldValue, string? newValue, string? comment = null) => db.TaxDeclarationAuditEvents.Add(new TaxDeclarationAuditEvent { Id = Guid.NewGuid(), TenantId = d.TenantId, EmployeeTaxDeclarationId = d.Id, EmployeeTaxDeclarationLineId = lineId, Action = action, ActorUserId = userId, OccurredAtUtc = clock.GetUtcNow().UtcDateTime, OldValue = oldValue, NewValue = newValue, Comment = comment });
    private static TaxDeclarationCycleDto ToCycle(TaxDeclarationCycle x) => new(x.Id, x.Code, x.Name, x.FinancialYear, x.DeclarationOpenDate, x.DeclarationCloseDate, x.ProofSubmissionOpenDate, x.ProofSubmissionCloseDate, x.Status);
    private static TaxDeclarationCategoryDto ToCategory(TaxDeclarationCategory x) => new(x.Id, x.Code, x.Name, x.CategoryType, x.RequiresProof, x.AllowsMultipleEntries, x.Active, x.DisplayOrder);
    private static TaxDeclarationItemDto ToItem(TaxDeclarationItem x) => new(x.Id, x.TaxDeclarationCategoryId, x.Code, x.Name, x.RequiresProof, x.AllowsAmount, x.AllowsReferenceNumber, x.AllowsDate, x.Active, x.PayrollTaxInputCode);
    private static TaxDeclarationProofDto ToProof(TaxDeclarationProof x) => new(x.Id, x.FileName, x.ContentType, x.FileSize, x.DocumentType ?? string.Empty, x.Status, x.UploadedAtUtc, x.ReviewerComment);
}
