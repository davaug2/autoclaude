using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;

namespace AutoClaude.Core.Services;

public class WorkModelSeeder
{
    private readonly IWorkModelRepository _workModelRepo;
    private readonly IPhaseRepository _phaseRepo;

    public WorkModelSeeder(IWorkModelRepository workModelRepo, IPhaseRepository phaseRepo)
    {
        _workModelRepo = workModelRepo;
        _phaseRepo = phaseRepo;
    }

    public async Task SeedAsync()
    {
        var existing = await _workModelRepo.GetByNameAsync("CascadeFlow");
        if (existing != null) return;

        var workModel = new WorkModel
        {
            Name = "CascadeFlow",
            Description = "Pipeline de 5 fases: Análise → Decomposição → Criação de Subtarefas → Execução → Validação",
            IsBuiltin = true
        };
        await _workModelRepo.InsertAsync(workModel);

        var phases = new[]
        {
            new Phase
            {
                WorkModelId = workModel.Id, Name = "Análise",
                PhaseType = PhaseType.Analysis, Ordinal = 1, RepeatMode = RepeatMode.Once,
                Description = "Entende objetivo, analisa código, gera especificação detalhada",
                PromptTemplate = "Analise o seguinte objetivo e gere uma especificação técnica detalhada.\n\nObjetivo: {{objective}}\nCaminho do projeto: {{target_path}}\n\nRetorne um JSON com a estrutura: {\"spec\": \"especificação detalhada\", \"technologies\": [\"tech1\"], \"risks\": [\"risk1\"]}"
            },
            new Phase
            {
                WorkModelId = workModel.Id, Name = "Decomposição",
                PhaseType = PhaseType.Decomposition, Ordinal = 2, RepeatMode = RepeatMode.Once,
                Description = "Quebra a especificação em macro tarefas",
                PromptTemplate = "Com base na seguinte especificação, decomponha em macro tarefas ordenadas.\n\nEspecificação: {{analysis_result}}\n\nRetorne um JSON array: [{\"title\": \"título\", \"description\": \"descrição detalhada\"}]"
            },
            new Phase
            {
                WorkModelId = workModel.Id, Name = "Criação de Subtarefas",
                PhaseType = PhaseType.SubtaskCreation, Ordinal = 3, RepeatMode = RepeatMode.PerTask,
                Description = "Gera subtarefas com prompts prontos para cada tarefa",
                PromptTemplate = "Para a seguinte tarefa, crie subtarefas com prompts prontos para execução via Claude Code CLI.\n\nTarefa: {{task_title}}\nDescrição: {{task_description}}\n\nRetorne um JSON array: [{\"title\": \"título\", \"prompt\": \"prompt completo para execução\"}]"
            },
            new Phase
            {
                WorkModelId = workModel.Id, Name = "Execução",
                PhaseType = PhaseType.Execution, Ordinal = 4, RepeatMode = RepeatMode.PerSubtask,
                Description = "Executa cada subtarefa via Claude Code CLI"
            },
            new Phase
            {
                WorkModelId = workModel.Id, Name = "Validação",
                PhaseType = PhaseType.Validation, Ordinal = 5, RepeatMode = RepeatMode.PerSubtask,
                Description = "Valida resultado de cada subtarefa e marca conclusão",
                PromptTemplate = "Valide se a seguinte subtarefa foi concluída corretamente.\n\nSubtarefa: {{subtask_title}}\nPrompt original: {{subtask_prompt}}\nResultado: {{subtask_result}}\n\nRetorne um JSON: {\"valid\": true/false, \"note\": \"observação\"}"
            }
        };

        foreach (var phase in phases)
            await _phaseRepo.InsertAsync(phase);
    }
}
