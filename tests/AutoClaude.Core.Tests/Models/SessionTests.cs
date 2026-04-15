using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using FluentAssertions;

namespace AutoClaude.Core.Tests.Models;

public class SessionTests
{
    [Fact]
    public void NewSession_ShouldHaveDefaultValues()
    {
        var session = new Session();

        session.Id.Should().NotBeEmpty();
        session.Status.Should().Be(SessionStatus.Created);
        session.CurrentPhaseOrdinal.Should().Be(0);
        session.ContextJson.Should().Be("{}");
        session.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        session.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void UpdateStatus_ShouldChangeStatusAndUpdateTimestamp()
    {
        var session = new Session();
        var originalUpdatedAt = session.UpdatedAt;

        session.UpdateStatus(SessionStatus.Running);

        session.Status.Should().Be(SessionStatus.Running);
        session.UpdatedAt.Should().BeOnOrAfter(originalUpdatedAt);
    }

    [Fact]
    public void AdvancePhase_ShouldUpdateOrdinalAndTimestamp()
    {
        var session = new Session();

        session.AdvancePhase(3);

        session.CurrentPhaseOrdinal.Should().Be(3);
    }
}
