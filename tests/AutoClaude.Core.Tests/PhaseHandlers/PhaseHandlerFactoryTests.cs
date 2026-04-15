using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.PhaseHandlers;
using FluentAssertions;
using Moq;

namespace AutoClaude.Core.Tests.PhaseHandlers;

public class PhaseHandlerFactoryTests
{
    [Fact]
    public void GetHandler_ShouldReturnCorrectHandler()
    {
        var handler = new Mock<IPhaseHandler>();
        handler.Setup(h => h.HandledPhase).Returns(PhaseType.Analysis);

        var factory = new PhaseHandlerFactory(new[] { handler.Object });

        factory.GetHandler(PhaseType.Analysis).Should().Be(handler.Object);
    }

    [Fact]
    public void GetHandler_UnregisteredPhase_ShouldThrow()
    {
        var factory = new PhaseHandlerFactory(Array.Empty<IPhaseHandler>());

        var act = () => factory.GetHandler(PhaseType.Analysis);

        act.Should().Throw<InvalidOperationException>();
    }
}
