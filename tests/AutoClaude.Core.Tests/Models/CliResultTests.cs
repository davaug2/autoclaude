using AutoClaude.Core.Domain.Models;
using FluentAssertions;

namespace AutoClaude.Core.Tests.Models;

public class CliResultTests
{
    [Fact]
    public void IsSuccess_ShouldReturnTrue_WhenExitCodeIsZero()
    {
        var result = new CliResult { ExitCode = 0 };

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void IsSuccess_ShouldReturnFalse_WhenExitCodeIsNonZero()
    {
        var result = new CliResult { ExitCode = 1 };

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void IsSuccess_ShouldReturnFalse_WhenExitCodeIsNegative()
    {
        var result = new CliResult { ExitCode = -1 };

        result.IsSuccess.Should().BeFalse();
    }
}
