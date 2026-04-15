using AutoClaude.Core.Domain.Models;

namespace AutoClaude.Core.Ports;

public interface ICliExecutor
{
    string CliType { get; }
    Task<CliResult> ExecuteAsync(CliRequest request, CancellationToken ct = default);
}
