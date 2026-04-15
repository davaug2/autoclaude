using AutoClaude.Core.Domain.Models;
using AutoClaude.Infrastructure.Repositories;
using FluentAssertions;

namespace AutoClaude.Infrastructure.Tests.Repositories;

public class WorkModelRepositoryTests : RepositoryTestBase
{
    [Fact]
    public async Task InsertAndGetById_ShouldRoundtrip()
    {
        var repo = new WorkModelRepository(Factory);
        var model = new WorkModel { Name = "CascadeFlow", Description = "Test model", IsBuiltin = true };

        await repo.InsertAsync(model);
        var result = await repo.GetByIdAsync(model.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(model.Id);
        result.Name.Should().Be("CascadeFlow");
        result.Description.Should().Be("Test model");
        result.IsBuiltin.Should().BeTrue();
    }

    [Fact]
    public async Task GetById_NonExistent_ShouldReturnNull()
    {
        var repo = new WorkModelRepository(Factory);

        var result = await repo.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByName_ShouldFindByName()
    {
        var repo = new WorkModelRepository(Factory);
        var model = new WorkModel { Name = "TestFlow", Description = "Test" };
        await repo.InsertAsync(model);

        var result = await repo.GetByNameAsync("TestFlow");

        result.Should().NotBeNull();
        result!.Name.Should().Be("TestFlow");
    }

    [Fact]
    public async Task GetAll_ShouldReturnAllModels()
    {
        var repo = new WorkModelRepository(Factory);
        await repo.InsertAsync(new WorkModel { Name = "Model1" });
        await repo.InsertAsync(new WorkModel { Name = "Model2" });

        var result = await repo.GetAllAsync();

        result.Should().HaveCount(2);
    }
}
