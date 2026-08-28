using System.Diagnostics;
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;

namespace MuktoAin.Application.Services;

public class AiOrchestrationService : IAiOrchestrationService
{
    private const string DefaultModelName = "gemini-2.0-flash";

    private readonly IRagContextBuilder _ragContextBuilder;
    private readonly IPromptAssembler _promptAssembler;
    private readonly MuktoAin.Domain.Interfaces.IAiService _aiService;
    private readonly DisclaimerInjector _disclaimerInjector;
    private readonly IAiLogService _aiLogService;
    private readonly IRepository<AiLog> _logRepo;
    private readonly IRepository<CaseActReference> _caseActRefRepo;
    private readonly string _modelName;

    public AiOrchestrationService(
        IRagContextBuilder ragContextBuilder,
        IPromptAssembler promptAssembler,
        MuktoAin.Domain.Interfaces.IAiService aiService,
        DisclaimerInjector disclaimerInjector,
        IAiLogService aiLogService,
        IRepository<AiLog> logRepo,
        IRepository<CaseActReference> caseActRefRepo,
        string modelName = DefaultModelName)
    {
        _ragContextBuilder = ragContextBuilder;
        _promptAssembler = promptAssembler;
        _aiService = aiService;
        _disclaimerInjector = disclaimerInjector;
        _aiLogService = aiLogService;
        _logRepo = logRepo;
        _caseActRefRepo = caseActRefRepo;
        _modelName = string.IsNullOrWhiteSpace(modelName) ? DefaultModelName : modelName;
    }

    public async Task<AiOrchestrationResult> ProcessCaseAsync(
        Case @case,
        AiRequestType requestType,
        string? documentType = null,
        CancellationToken ct = default)
    {
        var disclaimer = Disclaimers.ForLanguage(@case.Language);

        // 0. Cache Check for existing case AI runs
        if (@case.CaseId > 0 && requestType == AiRequestType.RightsExplanation)
        {
            var logs = await _logRepo.GetAllAsync();
            var existingLog = logs
                .Where(l => l.CaseId == @case.CaseId && l.RequestType == requestType)
                .OrderByDescending(l => l.CreatedAt)
                .FirstOrDefault();

            if (existingLog != null && !string.IsNullOrWhiteSpace(existingLog.ResponseText))
            {
                var cachedSections = @case.ActReferences?
                    .Select(r => new RetrievedSection(
                        r.SectionId,
                        r.Section?.Act?.Title ?? string.Empty,
                        r.Section?.SectionNumber ?? string.Empty,
                        r.Section?.SectionText ?? string.Empty,
                        (float)r.RelevanceScore,
                        r.RetrievalMethod,
                        r.Section?.Act?.ActNumber ?? string.Empty,
                        r.Section?.Act?.Year ?? 0))
                    .ToList() ?? new List<RetrievedSection>();

                return new AiOrchestrationResult(
                    existingLog.ResponseText,
                    cachedSections,
                    disclaimer,
                    IsCached: true);
            }
        }

        // 1. Retrieve statutory context
        var sections = (await _ragContextBuilder.RetrieveContextAsync(@case.Description, topK: 8)).ToList();

        // 2. Assemble prompt
        var prompt = await _promptAssembler.AssemblePromptAsync(
            @case.Description,
            sections,
            @case.Language,
            requestType,
            documentType,
            ct);

        // 3. Call AI Service with stopwatch
        var sw = Stopwatch.StartNew();
        var rawResponse = await _aiService.GenerateContentAsync(prompt, ct);
        sw.Stop();

        // 4. Inject Disclaimer
        var finalResponse = _disclaimerInjector.InjectDisclaimer(rawResponse, @case.Language);

        // 5. Estimate tokens and Log
        var tokensEstimated = Math.Max(1, (prompt.Length + rawResponse.Length) / 4);
        var caseId = @case.CaseId > 0 ? (int?)@case.CaseId : null;

        await _aiLogService.LogAsync(
            caseId,
            requestType,
            prompt,
            finalResponse,
            _modelName,
            tokensEstimated,
            (int)sw.ElapsedMilliseconds,
            ct);

        // 6. Save CaseActReference records for citations
        if (@case.CaseId > 0 && sections.Count > 0)
        {
            foreach (var section in sections)
            {
                var caseActRef = new CaseActReference
                {
                    CaseId = @case.CaseId,
                    SectionId = section.SectionId,
                    RelevanceScore = (decimal)section.RelevanceScore,
                    RetrievalMethod = section.Method
                };

                await _caseActRefRepo.AddAsync(caseActRef);
            }
            await _caseActRefRepo.SaveChangesAsync();
        }

        return new AiOrchestrationResult(
            finalResponse,
            sections,
            disclaimer,
            IsCached: false);
    }
}
