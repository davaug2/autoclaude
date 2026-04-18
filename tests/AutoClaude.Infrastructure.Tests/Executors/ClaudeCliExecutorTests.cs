using AutoClaude.Core.Domain.Models;
using AutoClaude.Infrastructure.Configuration;
using AutoClaude.Infrastructure.Executors;
using FluentAssertions;

namespace AutoClaude.Infrastructure.Tests.Executors;

public class ClaudeCliExecutorTests
{
    [Fact]
    public void BuildArguments_ShouldConstructCorrectCommandLine()
    {
        var request = new CliRequest { Prompt = "Hello world" };

        var args = ClaudeCliExecutor.BuildArguments(request);

        args.Should().Contain("--print");
        args.Should().Contain("--output-format stream-json");
        args.Should().Contain("--permission-mode auto");
        args.Should().Contain("-p ");
        args.Should().Contain("Hello world");
    }

    [Fact]
    public void BuildArguments_WithSystemPrompt_ShouldIncludeSystemPromptFlag()
    {
        var request = new CliRequest
        {
            Prompt = "Do something",
            SystemPrompt = "You are a helpful assistant"
        };

        var args = ClaudeCliExecutor.BuildArguments(request);

        args.Should().Contain("--system-prompt");
        args.Should().Contain("You are a helpful assistant");
    }

    [Fact]
    public void BuildArguments_WithAdditionalArgs_ShouldIncludeThem()
    {
        var request = new CliRequest
        {
            Prompt = "Test",
            AdditionalArgs = new List<string> { "--max-tokens", "4000" }
        };

        var args = ClaudeCliExecutor.BuildArguments(request);

        args.Should().Contain("--max-tokens");
        args.Should().Contain("4000");
    }

    [Fact]
    public void BuildArguments_WithQuotesInPrompt_ShouldEscapeThem()
    {
        var request = new CliRequest { Prompt = "Say \"hello\"" };

        var args = ClaudeCliExecutor.BuildArguments(request);

        args.Should().Contain("\\\"hello\\\"");
    }

    [Fact]
    public void BuildArguments_AllowWrite_ShouldNotBlockTools()
    {
        var request = new CliRequest { Prompt = "Test", AllowWrite = true };

        var args = ClaudeCliExecutor.BuildArguments(request);

        args.Should().Contain("--permission-mode auto");
        args.Should().NotContain("--disallowedTools");
    }

    [Fact]
    public void BuildArguments_ReadOnly_ShouldBlockEditTools_ButKeepWriteForOutputFile()
    {
        // Write must remain available so Claude can produce the JSON output file;
        // the system prompt restricts its use to the output path.
        var request = new CliRequest { Prompt = "Test", AllowWrite = false };

        var args = ClaudeCliExecutor.BuildArguments(request);

        args.Should().Contain("--permission-mode auto");
        args.Should().Contain("--disallowedTools \"Edit NotebookEdit\"");
    }

    [Fact]
    public void BuildArguments_WithOutputJsonPath_ShouldInstructClaudeToWriteFile()
    {
        var request = new CliRequest
        {
            Prompt = "Test",
            OutputJsonFilePath = "/tmp/out.json"
        };

        var args = ClaudeCliExecutor.BuildArguments(request);

        args.Should().Contain("/tmp/out.json");
        args.Should().Contain("FORMATO DE SAIDA OBRIGATORIO");
    }

    [Fact]
    public void BuildArguments_WithAllowedDirectories_ShouldIncludeAddDir()
    {
        var request = new CliRequest
        {
            Prompt = "Test",
            AllowedDirectories = new List<string> { "/tmp/project", "/var/data" }
        };

        var args = ClaudeCliExecutor.BuildArguments(request);

        args.Should().Contain("--add-dir");
        args.Should().Contain("/tmp/project");
        args.Should().Contain("/var/data");
    }

    [Fact]
    public void CliType_ShouldBeClaude()
    {
        var executor = new ClaudeCliExecutor(new NullAutoClaudeAppSettings());

        executor.CliType.Should().Be("claude");
    }
}
