# AutoClaude

Orquestrador de sessões que executa um pipeline de fases (análise, decomposição, subtarefas, execução, validação) usando a [Claude Code CLI](https://docs.anthropic.com/) (`claude` no PATH).

## Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Executável `claude` instalado e acessível no `PATH` (mesma máquina em que o AutoClaude roda)
- Conta/credenciais configuradas para a CLI, conforme a documentação da Anthropic

## Permissões da CLI

O executor invoca o `claude` com `--permission-mode auto` e, nas fases somente leitura, restringe ferramentas de escrita via `--disallowedTools`. Isso automatiza prompts de permissão; revise se isso é aceitável para o seu ambiente e política de segurança.

## Uso

```bash
dotnet run --project src/AutoClaude.Cli
```

Comandos principais (via `Spectre.Console.Cli`):

| Comando | Descrição |
|---------|-----------|
| *(sem argumentos)* | Menu interativo: listar sessões, retomar, criar ou excluir |
| `new "<objetivo>"` | Nova sessão com objetivo e caminho (`-p` / `--path`) |
| `list` | Listar sessões |
| `resume <session-id>` | Retomar sessão pausada ou com falha |
| `status <session-id>` | Ver progresso |

## Dados locais

O SQLite fica em `%LOCALAPPDATA%\AutoClaude\autoclaude.db` (Windows) ou equivalente no SO.

Diretórios adicionais permitidos para a CLI (modo interativo) são gravados no `context_json` da sessão e restaurados ao retomar.

## Desenvolvimento

```bash
dotnet build
dotnet test
```
