using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;
using AutoClaude.Core.Services;
using FluentAssertions;
using Moq;

namespace AutoClaude.Core.Tests.Services;

public class WorkModelSeederTests
{
    private readonly Mock<IWorkModelRepository> _workModelRepo = new();
    private readonly Mock<IPhaseRepository> _phaseRepo = new();

    [Fact]
    public async Task SeedAsync_ShouldCreateCascadeFlowWithFivePhases()
    {
        _workModelRepo.Setup(r => r.GetByNameAsync("CascadeFlow")).ReturnsAsync((WorkModel?)null);

        var seeder = new WorkModelSeeder(_workModelRepo.Object, _phaseRepo.Object);
        await seeder.SeedAsync();

        _workModelRepo.Verify(r => r.InsertAsync(It.Is<WorkModel>(m =>
            m.Name == "CascadeFlow" && m.IsBuiltin)), Times.Once);

        _phaseRepo.Verify(r => r.InsertAsync(It.IsAny<Phase>()), Times.Exactly(5));
    }

    [Fact]
    public async Task SeedAsync_ShouldCreatePhasesInCorrectOrder()
    {
        _workModelRepo.Setup(r => r.GetByNameAsync("CascadeFlow")).ReturnsAsync((WorkModel?)null);

        var insertedPhases = new List<Phase>();
        _phaseRepo.Setup(r => r.InsertAsync(It.IsAny<Phase>()))
            .Callback<Phase>(p => insertedPhases.Add(p));

        var seeder = new WorkModelSeeder(_workModelRepo.Object, _phaseRepo.Object);
        await seeder.SeedAsync();

        insertedPhases.Should().HaveCount(5);
        insertedPhases[0].PhaseType.Should().Be(PhaseType.Analysis);
        insertedPhases[0].Ordinal.Should().Be(1);
        insertedPhases[0].RepeatMode.Should().Be(RepeatMode.Once);

        insertedPhases[1].PhaseType.Should().Be(PhaseType.Decomposition);
        insertedPhases[1].Ordinal.Should().Be(2);
        insertedPhases[1].RepeatMode.Should().Be(RepeatMode.Once);

        insertedPhases[2].PhaseType.Should().Be(PhaseType.SubtaskCreation);
        insertedPhases[2].Ordinal.Should().Be(3);
        insertedPhases[2].RepeatMode.Should().Be(RepeatMode.PerTask);

        insertedPhases[3].PhaseType.Should().Be(PhaseType.Execution);
        insertedPhases[3].Ordinal.Should().Be(4);
        insertedPhases[3].RepeatMode.Should().Be(RepeatMode.PerSubtask);

        insertedPhases[4].PhaseType.Should().Be(PhaseType.Validation);
        insertedPhases[4].Ordinal.Should().Be(5);
        insertedPhases[4].RepeatMode.Should().Be(RepeatMode.PerSubtask);
    }

    [Fact]
    public async Task SeedAsync_WhenAlreadyExists_ShouldSkip()
    {
        _workModelRepo.Setup(r => r.GetByNameAsync("CascadeFlow"))
            .ReturnsAsync(new WorkModel { Name = "CascadeFlow" });

        var seeder = new WorkModelSeeder(_workModelRepo.Object, _phaseRepo.Object);
        await seeder.SeedAsync();

        _workModelRepo.Verify(r => r.InsertAsync(It.IsAny<WorkModel>()), Times.Never);
        _phaseRepo.Verify(r => r.InsertAsync(It.IsAny<Phase>()), Times.Never);
    }
}
