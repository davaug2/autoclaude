# Feature Specification: Frontend desktop Windows (Electron)

**Feature Branch**: `001-electron-windows-ui`  
**Created**: 2026-04-17  
**Status**: Draft  
**Input**: User description: "Vamos criar um novo frontend feito para windows usando electron ou algo assim"

## User Scenarios & Testing

### User Story 1 - Instalar e abrir o aplicativo no Windows (Priority: P1)

O usuário baixa ou instala o pacote Windows, abre o aplicativo e vê a interface principal do AutoClaude sem usar o terminal.

**Why this priority**: Sem isso não há produto desktop entregável.

**Independent Test**: Instalação limpa em VM Windows; ícone na área de trabalho ou menu Iniciar abre a janela principal.

**Acceptance Scenarios**:

1. **Given** Windows 10/11 64-bit, **When** o usuário executa o instalador ou app portable, **Then** o aplicativo inicia sem erro de dependência crítica.
2. **Given** o app aberto, **When** o usuário fecha a janela principal, **Then** o processo encerra conforme política escolhida (quit vs tray).

---

### User Story 2 - Fluxo principal espelha ou substitui a CLI (Priority: P1)

O usuário cria sessão, vê lista de sessões e retoma trabalho — equivalente ao que hoje faz via `autoclaude` no terminal.

**Why this priority**: O valor do produto é a orquestração; a UI deve expor os mesmos fluxos principais.

**Independent Test**: Criar sessão com objetivo e caminho; lista mostra a sessão; retomar executa o pipeline.

**Acceptance Scenarios**:

1. **Given** app em estado inicial, **When** o usuário informa objetivo e diretórios permitidos, **Then** uma nova sessão é criada no banco local (ou via backend acordado).
2. **Given** sessões existentes, **When** o usuário seleciona retomar, **Then** a orquestração continua sem exigir CLI manual.

---

### User Story 3 - Configurações e debug (Priority: P2)

O usuário ajusta opções (ex.: debug de comandos Claude) pela UI, alinhado a `settings.json` / flags existentes.

**Why this priority**: Paridade com `autoclaude settings` e visibilidade para suporte.

**Independent Test**: Alternar flag e verificar persistência e efeito na próxima execução.

---

### Edge Cases

- Claude CLI não instalado ou fora do PATH — mensagem acionável.
- Múltiplos monitores / DPI alto — janela legível.
- Atualização do app com banco/settings existentes — migração ou compatibilidade documentada.

## Requirements

### Functional

1. **FR-001**: Empacotar aplicação desktop para Windows (instalador ou MSIX/AppX/portable — decisão em pesquisa).
2. **FR-002**: UI renderizada em runtime web (Electron ou alternativa justificada) embutido no host nativo.
3. **FR-003**: Integração com lógica existente via processo `AutoClaude.Cli`, biblioteca compartilhada ou API local — decisão em plano técnico.
4. **FR-004**: Documentar requisitos de sistema (versão Windows, espaço em disco).

### Non-functional

1. **NFR-001**: Tempo de cold start alvo a definir após PoC (registrar em plano).
2. **NFR-002**: Não armazenar segredos em claro em arquivos do pacote.

## Success Criteria

1. Build de release produz artefato instalável testável em Windows 11.
2. Pelo menos os fluxos P1 cobertos por teste manual documentado em `quickstart.md`.
3. Plano e pesquisa registram decisão Electron vs alternativas e integração com o repositório atual.

## Assumptions

- Repositório `autoclaude` permanece fonte da verdade para orquestração até decisão de extrair API.
- Equipe aceita manutenção de Node/Electron ou alternativa escolhida no CI.
