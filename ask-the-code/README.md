# ask-the-code

Stack Docker Compose para um serviço interno de "pergunte ao código" com
**dois pipelines independentes** no mesmo orquestrador:

* `POST /ask/business` — Q&A sobre o **código-fonte** (regras de negócio).
  Lê um clone read-only do repo via `claude-code-mcp`.
* `POST /ask/diagnostics` — investigação operacional sobre **dados de
  clientes**: conversas, configs, audit logs, fluxos de chatbot no S3.
  Lê via 4 MCPs separados, sempre escopados a um `company_id`.

Ambos passam por:
1. rate-limit (Redis, por `user_id`),
2. input guard ([LLM Guard](https://github.com/protectai/llm-guard)
   `scan_prompt`),
3. agente LangChain ReAct (`ChatAnthropic` + tools MCP),
4. output guard em duas lanes:
   * **sanitize** — Secrets/PII (+ regex BR no diagnostics) trocados por
     `[REDACTED]`,
   * **block** — Toxicity/Bias/MaliciousURLs → dispara o **rewrite loop**
     (a LLM reescreve até `MAX_REWRITE_ATTEMPTS` vezes).

## Arquitetura

```
                          ┌─────────────────────────────────────────────┐
   POST /ask/business ───▶│ orchestrator :8000  (única porta exposta)   │
   POST /ask/diagnostics ▶│                                             │
                          │  rate-limit → input guard → ReAct agent →   │
                          │     output sanitize → output block →        │
                          │       [rewrite loop] → reply                │
                          └───────┬─────────────────────────────────────┘
                                  │ SSE
            ┌─────────────────────┴──────────────────────┐
            ▼                                             ▼
   business pipeline                       diagnostics pipeline
   ─────────────────                       ─────────────────────
   claude-code-mcp                         companies-mcp   postgres-mcp
   (read_file, grep, glob)                 (search_companies)  (list_conversations,
            │                                                   get_conversation,
            ▼                                                   list_config_keys,
   ┌─────────────┐                                              get_company_configs)
   │ volume :ro  │◀── repo-sync           mongo-mcp        s3-mcp
   └─────────────┘                        (search_audit_logs, (list_chatbot_flows,
                                           get_conversation_  download_chatbot_flow)
                                           messages,
                                           search_messages)
```

Apenas `orchestrator:8000` é publicado. Todos os MCPs e o Redis ficam na
rede Docker interna.

## Quando usar cada pipeline

| Pergunta típica                                            | Endpoint                |
|------------------------------------------------------------|-------------------------|
| "Como funciona o fluxo de login no código?"                | `/ask/business`         |
| "Em qual arquivo está a rotina X?"                         | `/ask/business`         |
| "Por que o chatbot da empresa Acme parou ontem às 14h?"    | `/ask/diagnostics`      |
| "Quais foram as últimas 10 conversas da empresa W?"        | `/ask/diagnostics`      |
| "Quais configurações estão ativas para a empresa K?"       | `/ask/diagnostics`      |
| "Quais fluxos a empresa Y tem cadastrados?"                | `/ask/diagnostics`      |

**Recomendação de permissão**: `/ask/business` pode ser exposto para todo
o time. `/ask/diagnostics` só para SRE, suporte sênior e engenharia
on-call — porque toca em dados de clientes.

## Princípios de segurança do pipeline de diagnóstico

1. **`company_id` obrigatório em toda tool**. Cada MCP valida que o valor
   é UUID, recusa string vazia / `*` / `%` / não-UUID.
2. **`company_id` resolvido uma única vez no orquestrador** via
   `companies-mcp.search_companies(query=company_hint)`. As outras tools
   só veem o GUID já resolvido — nunca o nome livre. Se múltiplos matches,
   a API devolve `{"requires_clarification": true, "companies": [...]}`.
3. **Read-only em tudo**. Connection strings de Postgres e Mongo usam
   usuário sem `INSERT/UPDATE/DELETE`. IAM do S3 com `s3:GetObject` e
   `s3:ListBucket` apenas no prefixo `flows/`.
4. **Queries parametrizadas e allowlist**. Nada de SQL livre — cada tool
   é um método pré-definido com argumentos tipados. `WHERE company_id =
   $1` sempre o primeiro predicado.
5. **Limites hard**: 100 conversas, 200 mensagens, 50 hits de busca, 1MB
   por flow do S3 (todos configuráveis via env).
6. **Output guard reforçado** no perfil `diagnostics`: Presidio com
   EMAIL_ADDRESS, PHONE_NUMBER, CREDIT_CARD, IP_ADDRESS, PERSON, LOCATION
   + regex BR (CPF, CNPJ, telefone) — tudo em modo sanitização (não
   bloqueio).
7. **System prompt fixa o `company_id`**. Após a resolução, o prompt diz
   ao LLM "use sempre este company_id, nunca consulte outra empresa".

## Decisões de implementação documentadas

* **MCP de código é Python customizado (FastMCP), não o `claude` CLI**.
  A doc pública do Claude Code descreve Claude Code como cliente MCP,
  não como server que re-publique Read/Grep/Glob. Implementar
  nativamente é mais simples, mais barato e mais fácil de prender ao
  `/repo`.
* **MCPs de diagnóstico em Python**. Mesma stack (`mcp` SDK +
  `FastMCP`). Cada MCP tem suas dependências específicas
  (`psycopg`/`pymongo`/`boto3`) — não tem dependências cruzadas.
* **Resolução de `company_id` fora do agente**. Não confiamos que a LLM
  vai escolher o `company_id` certo de um match ambíguo — devolvemos a
  ambiguidade pro caller decidir.
* **Logs de auditoria do diagnostics em nível `audit`**. Toda chamada
  `/ask/diagnostics` gera uma linha JSON com `event="answered"` ou
  `event="blocked"`, `user_id`, `company_id` resolvido, `tool_calls`
  (lista com tool name + args + tamanho do retorno), redações e
  rewrites. Plugue o log-driver do compose num SIEM com retenção
  ≥ 90 dias.
* **Sanitização ao invés de bloqueio para PII**. Em diagnóstico você
  quer ver o resto do contexto mesmo que apareça email/telefone de
  cliente. Toxicity/Bias/MaliciousURLs continuam bloqueando + rewriting.
* **Modelos do LLM Guard pré-baixados no `docker build`**. Runtime usa
  `HF_HUB_OFFLINE=1`. Para ligar `NoRefusal` ou outro scanner novo, o
  preload já baixa o modelo correspondente.
* **Confiança no `user_id` do cliente**. O orquestrador trata o
  `user_id` como id opaco para rate-limit e log. Em produção, ponha um
  reverse-proxy/SSO na frente que injete o id a partir do claim de
  autenticação. Nunca confie no `user_id` do navegador.

## Configuração

```bash
cp .env.example .env
# preencha pelo menos:
#   GIT_REPO_URL, GITHUB_TOKEN, ANTHROPIC_API_KEY
#   COMPANIES_DB_URL, OPERATIONS_DB_URL, MONGO_URL
#   AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, S3_BUCKET
```

Se você só quer o pipeline de código (sem diagnóstico), deixe
`ENABLE_DIAGNOSTICS=false` no `.env` e os 4 MCPs novos podem ficar fora
do `docker compose up` (use `--scale` ou crie um `docker-compose.override.yml`).

## Subir

```bash
docker compose build       # PRIMEIRO build é demorado (~15-30 min)
docker compose up -d
docker compose logs -f orchestrator
```

O primeiro build baixa os modelos do HuggingFace usados pelo LLM Guard
(~3-5 GB) durante a fase `preload_models.py` da imagem do orchestrator.
Eles ficam no layer; rebuilds incrementais são rápidos.

`/health` só responde 200 quando os scanners carregaram + os MCPs
conectaram + Redis respondeu.

## Testar

### Pipeline business (código)

```bash
curl -s http://localhost:8000/ask/business \
  -H 'Content-Type: application/json' \
  -d '{"user_id":"alice","question":"Onde está a função de login?"}' | jq .
```

### Pipeline diagnostics (dados operacionais)

Com hint resolvendo uma única empresa:

```bash
curl -s http://localhost:8000/ask/diagnostics \
  -H 'Content-Type: application/json' \
  -d '{
        "user_id":"sre.bob",
        "company_hint":"Acme Corp",
        "question":"Quais conversas falharam nas últimas 24h?"
      }' | jq .
```

```json
{
  "answer": "Encontrei 3 conversas com status=failed entre ...",
  "blocked": false,
  "metadata": {
    "company_id": "uuid-resolvido",
    "company_name": "Acme Corp",
    "tool_calls": [
      {"tool": "list_conversations", "args": {...}, "result_count": 3},
      {"tool": "get_conversation_messages", "args": {...}, "result_count": 47}
    ],
    "redactions": ["Sensitive", "Regex"],
    "rewrites": 0,
    "elapsed_ms": 8123
  },
  "request_id": "..."
}
```

Quando o hint casa com várias empresas:

```json
{
  "answer": null,
  "blocked": false,
  "reason": "multiple_companies_matched",
  "requires_clarification": true,
  "companies": [
    {"company_id": "uuid1", "name": "Acme Corp", "status": "active"},
    {"company_id": "uuid2", "name": "Acme Industries", "status": "active"}
  ]
}
```

O caller mostra a lista pro usuário; ao escolher, o caller refaz a
chamada com o `company_hint` mais específico (ou enriquece a `question`
para a LLM decidir).

Sem hint:

```bash
curl -s http://localhost:8000/ask/diagnostics \
  -H 'Content-Type: application/json' \
  -d '{"user_id":"sre.bob","question":"Pega as últimas conversas da Acme."}'
```

A LLM tem a tool `search_companies` disponível e o system prompt diz
"se a empresa não está clara, chame search_companies primeiro". A LLM
decide.

## Ajustes sem rebuild

* `BANNED_TOPICS` / `DIAGNOSTICS_BANNED_TOPICS` — listas CSV no `.env`.
* `*_THRESHOLD` — todos os thresholds dos scanners.
* `OUTPUT_BLOCK_REGEX_PATTERNS` / `OUTPUT_REDACT_REGEX_PATTERNS` — listas
  JSON. Os patterns regex aplicam aos dois pipelines.
* Limites de resultado (`MAX_CONVERSATIONS`, `MAX_MESSAGES`,
  `MAX_FLOW_BYTES`, etc.) — bastam recriar o MCP afetado.

```bash
docker compose up -d --force-recreate orchestrator
docker compose up -d --force-recreate postgres-mcp   # se mexeu nesse
```

## Adicionar uma nova tool MCP

Roteiro pra criar um quinto MCP server (ex: BigQuery):

1. Crie `bigquery-mcp/` com `Dockerfile`, `server.py` (FastMCP +
   `@mcp.tool()` para cada função), `requirements.txt`.
2. Sempre valide `company_id` (ou seu equivalente de tenant) como
   primeira coisa em cada tool. Aplique limites de resultado.
3. Adicione o serviço no `docker-compose.yml` (use o anchor `x-mcp-base`
   pra herdar hardening + network).
4. Em `orchestrator/mcp_client.py`, adicione o servidor ao
   `connect_diagnostics()` (ou crie um terceiro perfil se for um caso
   muito diferente).
5. Se o nome da tool nova colidir com algum existente, prefixe (ex:
   `bigquery_search_events`).
6. Documente no README qual tool atende qual tipo de pergunta.

## Observabilidade (Langfuse)

Preencha `LANGFUSE_HOST`, `LANGFUSE_PUBLIC_KEY`, `LANGFUSE_SECRET_KEY`
no `.env`. O callback handler é anexado a todas as chamadas LangChain
— você verá o ReAct loop completo (tool calls, mensagens, rewrites) por
sessão na UI do Langfuse. Sem essas vars, nada é enviado.

## Logs e auditoria

Tudo JSON-line em stdout. As linhas de `/ask/diagnostics` vão com
`level: "audit"` para facilitar separação:

```bash
docker compose logs -f orchestrator | jq -c 'select(.level=="audit")'
```

Campos por linha de auditoria: `ts`, `service`, `pipeline`,
`request_id`, `user_id`, `company_id`, `company_name`, `question`,
`tool_calls`, `redactions`, `rewrites`, `elapsed_ms`, `reason`.

**Retenção recomendada**: mínimo 90 dias num SIEM imutável. É a trilha
de quem consultou dados de qual cliente. Recomendado encaminhar via
`logging.driver: fluentd|gelf|awslogs` no compose.

## Limitações / pendências

* A LLM Anthropic recebe os trechos lidos pelo agente — contrate
  Zero Data Retention para produção sensível
  (https://www.anthropic.com/zdr).
* Sem auth no endpoint público. Coloque oauth2-proxy / Authelia /
  AWS ALB OIDC na frente.
* Sem TLS interno. Para multi-tenant, considere mTLS via sidecar.
* `TokenLimit` usa tokenizer aproximado do LLM Guard, não o do Claude.
  Dê folga.
* Rewrite custa chamadas extras à API. Pior caso: 1 ReAct + N rewrites
  + cada rewrite reroda o sanitize+block.
* Os schemas dos bancos têm defaults razoáveis (`conversations`,
  `company_configs`, `audit_logs`, `messages`). Adapte via env vars se
  os seus diferem (`CONVERSATIONS_TABLE`, `MONGO_*_COLLECTION` etc.).
* `mongo-mcp.search_messages` precisa de um índice `text` no campo
  `text` da coleção `messages`. Se não houver, ele cai num regex
  case-insensitive (mais lento e mais limitado).

## Rotação de credenciais

* `GITHUB_TOKEN`: gere PAT com `repo` (read), expire em 90 dias.
* `ANTHROPIC_API_KEY`: rotacione a cada 90 dias via console.
* `COMPANIES_DB_URL` / `OPERATIONS_DB_URL` / `MONGO_URL`: usuários
  separados (read-only) por MCP. Senhas em vault. Rotacione conforme
  política interna.
* `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY`: prefira IAM Role via
  IRSA/EC2 instance-profile em produção; sirva-se das vars só em dev.

## Como estender

* **SSO.** oauth2-proxy / Authelia na frente; reescreve `user_id`
  pelo claim de auth.
* **Slack.** Worker `slack-bolt` que escuta menções e faz `POST /ask/*`.
* **GPU.** Base `nvidia/cuda:12.4.1-cudnn-runtime-ubuntu22.04`,
  `torch+cu124`, `deploy.resources.reservations.devices` no compose.
* **SIEM.** `logging.driver` no compose apontando pro coletor.
* **Cache de respostas.** Hash `(question, company_id, repo_sha)` →
  resposta sanitizada com TTL em Redis. Respeite ACL por `user_id`.
* **Múltiplas regiões / tenants.** Um compose por região; ou parametrize
  os MCPs para vários DBs/buckets via env e um seletor no orquestrador.

## Estrutura

```
ask-the-code/
├── docker-compose.yml
├── .env.example
├── README.md
├── repo-sync/
│   ├── Dockerfile
│   └── sync.sh
├── claude-code-mcp/                  # business pipeline
│   ├── Dockerfile
│   ├── server.py
│   └── requirements.txt
├── companies-mcp/                    # diagnostics pipeline
│   ├── Dockerfile
│   ├── server.py
│   └── requirements.txt
├── postgres-mcp/                     # diagnostics pipeline
│   ├── Dockerfile
│   ├── server.py
│   └── requirements.txt
├── mongo-mcp/                        # diagnostics pipeline
│   ├── Dockerfile
│   ├── server.py
│   └── requirements.txt
├── s3-mcp/                           # diagnostics pipeline
│   ├── Dockerfile
│   ├── server.py
│   └── requirements.txt
└── orchestrator/
    ├── Dockerfile
    ├── app.py                        # FastAPI: /ask, /ask/business, /ask/diagnostics
    ├── pipelines/
    │   ├── __init__.py
    │   ├── _common.py                # PipelineResult + output-guard helper
    │   ├── business.py               # pipeline existente
    │   └── diagnostics.py            # NOVO
    ├── guards.py                     # multi-profile LLM Guard
    ├── rewrite.py
    ├── mcp_client.py                 # registry com 2 sets de tools
    ├── preload_models.py
    └── requirements.txt
```
