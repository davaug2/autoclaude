using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Infrastructure.Repositories;
using FluentAssertions;

namespace AutoClaude.Infrastructure.Tests.Repositories;

public class PhaseRepositoryTests : RepositoryTestBase
{
    private async Task<WorkModel> CreateWorkModelAsync()
    {
        var wmRepo = new WorkModelRepository(Factory);
        var model = new WorkModel { Name = "TestFlow" };
        await wmRepo.InsertAsync(model);
        return model;
    }

    [Fact]
    public async Task InsertAndGetById_ShouldRoundtrip()
    {
        var wm = await CreateWorkModelAsync();
        var repo = new PhaseRepository(Factory);
        var phase = new Phase
        {
            WorkModelId = wm.Id, Name = "Analysis", PhaseType = PhaseType.Analysis,
            Ordinal = 1, RepeatMode = RepeatMode.Once, Description = "Analyze"
        };

        await repo.InsertAsync(phase);
        var result = await repo.GetByIdAsync(phase.Id);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Analysis");
        result.PhaseType.Should().Be(PhaseType.Analysis);
        result.Ordinal.Should().Be(1);
        result.RepeatMode.Should().Be(RepeatMode.Once);
    }

    [Fact]
    public async Task GetByWorkModelId_ShouldReturnPhasesInOrder()
    {
        var wm = await CreateWorkModelAsync();
        var repo = new PhaseRepository(Factory);
        await repo.InsertAsync(new Phase { WorkModelId = wm.Id, Name = "Validation", PhaseType = PhaseType.Validation, Ordinal = 3 });
        await repo.InsertAsync(new Phase { WorkModelId = wm.Id, Name = "Analysis", PhaseType = PhaseType.Analysis, Ordinal = 1 });
        await repo.InsertAsync(new Phase { WorkModelId = wm.Id, Name = "Decomposition", PhaseType = PhaseType.Decomposition, Ordinal = 2 });

        var result = await repo.GetByWorkModelIdAsync(wm.Id);

        result.Should().HaveCount(3);
        result[0].Ordinal.Should().Be(1);
        result[1].Ordinal.Should().Be(2);
        result[2].Ordinal.Should().Be(3);
    }
}
