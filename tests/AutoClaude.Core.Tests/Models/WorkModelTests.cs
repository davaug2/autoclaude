using AutoClaude.Core.Domain.Models;
using FluentAssertions;

namespace AutoClaude.Core.Tests.Models;

public class WorkModelTests
{
    [Fact]
    public void NewWorkModel_ShouldHaveDefaultValues()
    {
        var model = new WorkModel();

        model.Id.Should().NotBeEmpty();
        model.Name.Should().BeEmpty();
        model.Description.Should().BeNull();
        model.IsBuiltin.Should().BeFalse();
        model.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        model.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void NewWorkModel_ShouldAcceptValues()
    {
        var model = new WorkModel
        {
            Name = "CascadeFlow",
            Description = "Pipeline de 5 fases",
            IsBuiltin = true
        };

        model.Name.Should().Be("CascadeFlow");
        model.Description.Should().Be("Pipeline de 5 fases");
        model.IsBuiltin.Should().BeTrue();
    }

    [Fact]
    public void TwoNewWorkModels_ShouldHaveDifferentIds()
    {
        var model1 = new WorkModel();
        var model2 = new WorkModel();

        model1.Id.Should().NotBe(model2.Id);
    }
}
