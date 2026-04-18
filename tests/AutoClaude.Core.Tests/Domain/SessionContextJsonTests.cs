using AutoClaude.Core.Domain;
using AutoClaude.Core.Domain.Models;
using FluentAssertions;

namespace AutoClaude.Core.Tests.Domain;

public class SessionContextJsonTests
{
    [Fact]
    public void MergeAllowedDirectories_ShouldPreserveOtherContextKeys()
    {
        var merged = SessionContextJson.MergeAllowedDirectories(
            """{"existingKey":"keep"}""",
            new List<string> { "/a", "/b" });

        merged.Should().Contain("allowed_directories");
        merged.Should().Contain("existingKey");
    }

    [Fact]
    public void HydrateAllowedDirectories_ShouldPopulateSession()
    {
        var session = new Session
        {
            ContextJson = """{"allowed_directories":["/x","/y"]}"""
        };

        SessionContextJson.HydrateAllowedDirectories(session);

        session.AllowedDirectories.Should().BeEquivalentTo(new[] { "/x", "/y" });
    }
}
