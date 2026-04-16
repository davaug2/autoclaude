using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;

namespace AutoClaude.Core.Services;

public class SessionService
{
    private readonly ISessionRepository _sessionRepo;
    private readonly IWorkModelRepository _workModelRepo;
    private readonly ITaskRepository _taskRepo;
    private readonly ISubtaskRepository _subtaskRepo;
    private readonly IExecutionRecordRepository _executionRepo;
    private readonly IPhaseRepository _phaseRepo;
    private readonly WorkModelSeeder _seeder;
    private readonly OrchestrationEngine _engine;

    public SessionService(
        ISessionRepository sessionRepo,
        IWorkModelRepository workModelRepo,
        ITaskRepository taskRepo,
        ISubtaskRepository subtaskRepo,
        IExecutionRecordRepository executionRepo,
        IPhaseRepository phaseRepo,
        WorkModelSeeder seeder,
        OrchestrationEngine engine)
    {
        _sessionRepo = sessionRepo;
        _workModelRepo = workModelRepo;
        _taskRepo = taskRepo;
        _subtaskRepo = subtaskRepo;
        _executionRepo = executionRepo;
        _phaseRepo = phaseRepo;
        _seeder = seeder;
        _engine = engine;
    }

    public async Task<Session> CreateAsync(string objective, string? name = null, string? targetPath = null, Guid? workModelId = null, CancellationToken ct = default)
    {
        await _seeder.SeedAsync();

        WorkModel workModel;
        if (workModelId.HasValue)
        {
            workModel = await _workModelRepo.GetByIdAsync(workModelId.Value)
                ?? throw new KeyNotFoundException($"Work model {workModelId} not found");
        }
        else
        {
            workModel = await _workModelRepo.GetByNameAsync("CascadeFlow")
                ?? throw new InvalidOperationException("CascadeFlow work model not found after seeding");
        }

        var session = new Session
        {
            WorkModelId = workModel.Id,
            Name = name ?? $"Session {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            Objective = objective,
            TargetPath = targetPath
        };

        await _sessionRepo.InsertAsync(session);
        return session;
    }

    public async Task<IReadOnlyList<Session>> ListAsync()
    {
        return await _sessionRepo.GetAllAsync();
    }

    public async Task<Session> GetAsync(Guid sessionId)
    {
        return await _sessionRepo.GetByIdAsync(sessionId)
            ?? throw new KeyNotFoundException($"Session {sessionId} not found");
    }

    public async Task RunAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await GetAsync(sessionId);
        await _engine.RunAsync(session, ct);
    }

    public async Task ResumeAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await GetAsync(sessionId);

        if (session.Status == SessionStatus.Completed)
            throw new InvalidOperationException("Session is already completed");

        await _engine.RunAsync(session, ct);
    }

    public async Task<(IReadOnlyList<TaskItem> tasks, IReadOnlyList<SubtaskItem> subtasks)> GetStatusAsync(Guid sessionId)
    {
        var tasks = await _taskRepo.GetBySessionIdAsync(sessionId);
        var subtasks = await _subtaskRepo.GetBySessionIdAsync(sessionId);
        return (tasks, subtasks);
    }

    public async Task<IReadOnlyList<WorkModel>> ListWorkModelsAsync()
    {
        await _seeder.SeedAsync();
        return await _workModelRepo.GetAllAsync();
    }

    public async Task<WorkModel> CreateWorkModelAsync(string name, string? description = null)
    {
        var model = new WorkModel
        {
            Name = name,
            Description = description,
            IsBuiltin = false
        };
        await _workModelRepo.InsertAsync(model);
        return model;
    }

    public async Task<Phase> AddPhaseToWorkModelAsync(
        Guid workModelId, string name, PhaseType phaseType,
        int ordinal, RepeatMode repeatMode, string? description = null,
        string? promptTemplate = null)
    {
        var phase = new Phase
        {
            WorkModelId = workModelId,
            Name = name,
            PhaseType = phaseType,
            Ordinal = ordinal,
            RepeatMode = repeatMode,
            Description = description,
            PromptTemplate = promptTemplate
        };
        await _phaseRepo.InsertAsync(phase);
        return phase;
    }

    public async Task DeleteAsync(Guid sessionId)
    {
        var session = await _sessionRepo.GetByIdAsync(sessionId)
            ?? throw new KeyNotFoundException($"Session {sessionId} not found");

        await _executionRepo.DeleteBySessionIdAsync(sessionId);
        await _subtaskRepo.DeleteBySessionIdAsync(sessionId);
        await _taskRepo.DeleteBySessionIdAsync(sessionId);
        await _sessionRepo.DeleteAsync(sessionId);
    }
}
