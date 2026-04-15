using AutoClaude.Core.Domain.Models;

namespace AutoClaude.Core.Ports;

public interface IWorkModelRepository
{
    Task<WorkModel?> GetByIdAsync(Guid id);
    Task<WorkModel?> GetByNameAsync(string name);
    Task<IReadOnlyList<WorkModel>> GetAllAsync();
    Task InsertAsync(WorkModel model);
}
