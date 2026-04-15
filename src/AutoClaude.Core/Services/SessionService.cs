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
    private readonly WorkModelSeeder _seeder;
    private readonly OrchestrationEngine _engine;

    public SessionService(
        ISessionRepository sessionRepo,
        IWorkModelRepository workModelRepo,
        ITaskRepository taskRepo,
        ISubtaskRepository subtaskRepo,
        WorkModelSeeder seeder,
        OrchestrationEngine engine)
    {
        _sessionRepo = sessionRepo;
        _workModelRepo = workModelRepo;
        _taskRepo = taskRepo;
        _subtaskRepo = subtaskRepo;
        _seeder = seeder;
        _engine = engine;
    }

    public async Task<Session> CreateAsync(string objective, string? name = null, string? targetPath = null, CancellationToken ct = default)
    {
        await _seeder.SeedAsync();

        var workModel = await _workModelRepo.GetByNameAsync("CascadeFlow")
            ?? throw new InvalidOperationException("CascadeFlow work model not found after seeding");

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
}
