using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Infrastructure.Repositories;
using FluentAssertions;

namespace AutoClaude.Infrastructure.Tests.Repositories;

public class ExecutionRecordRepositoryTests : RepositoryTestBase
{
    private async Task<(Session session, Phase phase)> CreateSessionWithPhaseAsync()
    {
        var wmRepo = new WorkModelRepository(Factory);
        var wm = new WorkModel { Name = "TestFlow" };
        await wmRepo.InsertAsync(wm);

        var phaseRepo = new PhaseRepository(Factory);
        var phase = new Phase { WorkModelId = wm.Id, Name = "Analysis", PhaseType = PhaseType.Analysis, Ordinal = 1 };
        await phaseRepo.InsertAsync(phase);

        var sessionRepo = new SessionRepository(Factory);
        var session = new Session { WorkModelId = wm.Id, Objective = "Test" };
        await sessionRepo.InsertAsync(session);

        return (session, phase);
    }

    [Fact]
    public async Task InsertAndGetBySessionId_ShouldRoundtrip()
    {
        var (session, phase) = await CreateSessionWithPhaseAsync();
        var repo = new ExecutionRecordRepository(Factory);
        var record = new ExecutionRecord
        {
            SessionId = session.Id, PhaseId = phase.Id,
            PromptSent = "Analyze this", CliType = "claude"
        };
        record.MarkStarted();
        record.MarkSuccess("result text", "{\"result\":\"ok\"}", 0, 1500);

        await repo.InsertAsync(record);
        var result = await repo.GetBySessionIdAsync(session.Id);

        result.Should().HaveCount(1);
        result[0].PromptSent.Should().Be("Analyze this");
        result[0].Outcome.Should().Be(ExecutionOutcome.Success);
        result[0].ResponseText.Should().Be("result text");
        result[0].DurationMs.Should().Be(1500);
    }

    [Fact]
    public async Task Update_ShouldModifyRecord()
    {
        var (session, phase) = await CreateSessionWithPhaseAsync();
        var repo = new ExecutionRecordRepository(Factory);
        var record = new ExecutionRecord
        {
            SessionId = session.Id, PhaseId = phase.Id,
            PromptSent = "Analyze this"
        };
        record.MarkStarted();
        await repo.InsertAsync(record);

        record.MarkSuccess("response", null, 0, 2000);
        await repo.UpdateAsync(record);

        var result = (await repo.GetBySessionIdAsync(session.Id))[0];
        result.Outcome.Should().Be(ExecutionOutcome.Success);
        result.ResponseText.Should().Be("response");
        result.DurationMs.Should().Be(2000);
    }
}
