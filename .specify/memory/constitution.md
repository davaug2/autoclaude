# AutoClaude Constitution

## Core Principles

### I. Camadas e dependências

- **AutoClaude.Core** — domínio, portas, serviços de orquestração. Sem dependência de infraestrutura concreta.
- **AutoClaude.Infrastructure** — SQLite, repositórios, executores (ex.: Claude CLI). Implementa portas definidas em Core.
- **AutoClaude.Cli** — interface de linha de comando, notificadores, comandos Spectre.

Dependências só no sentido Core ← Infrastructure ← Cli (ou injeção via interfaces em Core).

### II. Segurança e dados locais

- Credenciais e tokens não devem ser persistidos em texto puro em artefatos versionados.
- Banco local (`autoclaude.db`) e `settings.json` em `%LocalAppData%` — documentar o que é armazenado.

### III. Observabilidade

- Falhas em integrações externas (CLI, rede) devem ser tratadas com mensagens claras ao usuário; logs estruturados onde aplicável.

### IV. Simplicidade (YAGNI)

- Novos projetos ou camadas exigem justificativa no plano de implementação.
- Evitar abstrações sem três usos concretos.

---

## Stack atual (repositório)

| Componente | Tecnologia |
|------------|------------|
| Runtime | .NET 8 |
| Persistência | SQLite (Dapper) |
| CLI | Spectre.Console |
| Orquestração | Serviços + phase handlers |

**Governança**: alterações desta constituição via PR; versionamento MAJOR/MINOR/PATCH como em projetos típicos.

**Version**: 1.0.0 | **Ratified**: 2026-04-17
