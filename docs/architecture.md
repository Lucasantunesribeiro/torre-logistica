# Arquitetura — Torre Logística

> Estado: Fase 0 concluída (fundação técnica). Este documento cresce junto com as fases.
> As decisões por trás do que está aqui ficam em [`docs/adr/`](./adr/README.md).

## Visão geral

```text
┌──────────────┐  ┌──────────────┐  ┌──────────────────┐
│ apps/operacao│  │apps/motorista│  │apps/rastreamento │
│    :5173     │  │    :5174     │  │      :5175       │
└──────┬───────┘  └──────┬───────┘  └────────┬─────────┘
       │  HTTP + (futuro) WebSocket          │ HTTP público
       └─────────────┬───────────────────────┘
                     ▼
          ┌──────────────────────┐        ┌──────────────────────┐
          │ TorreLogistica.Api   │        │TorreLogistica.Workers│
          │ composition root     │        │ composition root     │
          └──────────┬───────────┘        └──────────┬───────────┘
                     │                               │
                     └───────────┬───────────────────┘
                                 ▼
                   ┌──────────────────────────────┐
                   │ TorreLogistica.Infrastructure│
                   └──────────────┬───────────────┘
                                  ▼
                   ┌──────────────────────────────┐
                   │ TorreLogistica.Application   │
                   └──────────────┬───────────────┘
                                  ▼
                   ┌──────────────────────────────┐
                   │ TorreLogistica.Domain        │
                   │ sem infraestrutura alguma    │
                   └──────────────────────────────┘

                   ┌──────────────────────────────┐
   PostgreSQL 17   │ TorreLogistica.Simulator     │
   + PostGIS 3.5   │ cliente HTTP externo         │
                   │ sem acesso ao núcleo         │
                   └──────────────────────────────┘
```

## Camadas e a direção das dependências

| Projeto | Papel | Pode depender de |
|---|---|---|
| `Domain` | regras, invariantes, abstrações de tempo e identificador | nada |
| `Application` | casos de uso e orquestração | `Domain` + abstrações de DI e log |
| `Infrastructure` | PostgreSQL/PostGIS, relógio, identificador, health check | `Application`, `Domain` |
| `Api` | borda HTTP | `Application`, `Infrastructure` |
| `Workers` | carga assíncrona | `Application`, `Infrastructure` |
| `Simulator` | cliente externo | **nada do núcleo** |

A regra não é confiada à disciplina de quem escreve: `TorreLogistica.ArchitectureTests`
lê os arquivos `.csproj` e os metadados dos assemblies e reprova o build quando a
direção é violada. A leitura do `.csproj` é intencional — uma referência declarada e
ainda não usada não aparece nos metadados compilados e escaparia de uma checagem feita
só em tempo de execução.

## O que existe na Fase 0

### Tempo: `IRelogio`

Nenhuma regra lê o relógio do sistema diretamente. `IRelogio` (em `Domain`) é a única
porta, e `RelogioDoSistema` (em `Infrastructure`) é sua única implementação, ancorada
no `TimeProvider` da plataforma.

Isso existe porque SLA, ETA, detecção de motorista offline, timeline e o simulador
precisam de tempo controlável. Um `DateTimeOffset.UtcNow` escondido numa regra torna o
comportamento impossível de testar e o cenário de demonstração impossível de reproduzir.

`AgoraUtc` aplica `ToUniversalTime()` de propósito: um `TimeProvider` pode devolver
instante com deslocamento diferente de zero, e deixar isso passar faria gravações
herdarem um fuso que o contrato proíbe. Um teste cobre exatamente esse caso.

Um teste de arquitetura varre o código de `Domain`, `Application`, `Infrastructure`,
`Api` e `Workers` procurando leitura direta do relógio, ignorando comentários e
documentação. Só `RelogioDoSistema.cs` está autorizado.

### Identificador: UUIDv7

`IGeradorDeIdentificador` produz UUIDv7 ancorado no `IRelogio`. `Uuid7` interpreta os
campos do identificador conforme a RFC 9562 — versão, variante e instante de criação.

Detalhe de consequência prática: **o identificador revela o instante de criação** com
precisão de milissegundo. Aceitável para identificador interno, e uma das razões para o
token de rastreamento público (Fase 15) ser aleatório e guardado como hash.

Ver [ADR 0004](./adr/0004-uuidv7-como-identificador.md).

### Erros: contrato único na borda

O domínio lança `ExcecaoDeDominio` com um **código estável** (para máquina) e uma
**categoria** (para escolher o status HTTP). A tradução para HTTP vive num único lugar,
`MapeamentoDeErrosDeDominio`, e em nenhum outro:

| Categoria | Status | Por quê |
|---|:---:|---|
| `RegraViolada` | 422 | A requisição foi entendida e o negócio a recusou. Entrada malformada continua sendo 400, da validação de modelo |
| `Conflito` | 409 | Colisão com o estado atual ou corrida perdida |
| `NaoEncontrado` | 404 | Não existe no contexto autorizado — inclusive recurso de outro tenant, cuja existência não deve ser revelada |

Categoria desconhecida cai em 500, nunca em 200. Há teste para isso.

Autenticação, autorização e rate limit **não** estão nessa lista de propósito: são
decisões de borda HTTP, e o domínio não conhece requisição.

Toda resposta de erro é `application/problem+json` e traz `codigo`, `traceId` e
`idDeCorrelacao`. Mensagem de exceção nunca vai para o corpo: texto de exceção carrega
caminho de arquivo, nome de tabela e trecho de configuração — material de
reconhecimento para quem estiver sondando a API. O detalhe fica no log, amarrado ao
identificador de correlação que o cliente recebeu.

### Correlação

Toda requisição recebe um identificador que amarra tudo o que aconteceu por causa dela.
Se o cliente mandou um `X-Correlation-Id` aceitável, ele é reaproveitado; caso
contrário, um UUIDv7 novo é gerado.

"Aceitável" é estreito de propósito: até 64 caracteres de `[A-Za-z0-9._-]`. O valor
volta no cabeçalho da resposta e entra em cada linha de log, então aceitar texto
arbitrário seria aceitar injeção de cabeçalho e de linha de log. Quebra de linha,
espaço e aspas são recusados, com testes para cada caso.

O cabeçalho é gravado em `Response.OnStarting`, não de imediato. O middleware de
tratamento de erro limpa a resposta antes de escrever o ProblemDetails, e um cabeçalho
gravado cedo desapareceria justamente na resposta de falha — a que mais precisa do
identificador.

### Saúde: `/health/live` e `/health/ready`

| Endpoint | Responde | Toca no banco |
|---|---|:---:|
| `/health/live` | o processo está funcional | não |
| `/health/ready` | dá para me mandar tráfego | sim |

A separação é operacional. Se `/live` consultasse o banco, uma indisponibilidade
momentânea dele faria o orquestrador matar e recriar processos saudáveis, convertendo
uma falha externa em derrubada geral.

O corpo traz apenas nome, estado e duração de cada verificação. Descrição e exceção
ficam de fora porque carregam host, banco e usuário da cadeia de conexão — e o
endpoint é anônimo. Há teste procurando essas pistas no corpo.

### Banco

PostgreSQL 17 com PostGIS 3.5. A extensão é declarada no modelo e entra pela migration
`Fundacao` — é parte do schema, não pré-requisito informal de ambiente.

Nomes físicos em `snake_case`; instantes em `timestamptz`, sempre UTC.

Migration no start existe apenas como conveniência de desenvolvimento e teste
(`Torre:BancoDeDados:AplicarMigrationsAoIniciar`). Em produção é passo explícito de
implantação: aplicar schema na subida faz instâncias concorrerem pela mesma alteração e
esconde a falha dentro do processo que deveria servir tráfego.

### Configuração

Tipada e validada na subida (`ValidateDataAnnotations` + `ValidateOnStart`).
Configuração inválida é erro de implantação e precisa aparecer enquanto ainda há quem
esteja olhando — não na primeira requisição do primeiro usuário.

A política de CORS é montada a partir das opções resolvidas pela injeção de
dependência, e não de uma leitura direta da configuração no momento de construir o
host. Ler cedo congelaria o valor antes de as demais fontes do ambiente entrarem em
vigor — foi exatamente o defeito que o teste de CORS pegou durante a Fase 0.

## Segurança de borda na Fase 0

| Item | Como está |
|---|---|
| Cabeçalhos | `nosniff`, `X-Frame-Options: DENY`, CSP `default-src 'none'`, `Referrer-Policy: no-referrer`, `Permissions-Policy` sem permissão, `Cross-Origin-Resource-Policy: same-origin` |
| Identificação do servidor | `Server` e `X-Powered-By` removidos; `AddServerHeader` desligado |
| CORS | fechado por padrão; sem origem configurada para o ambiente, nada é liberado |
| HSTS | ativo fora de desenvolvimento |
| Stack trace | nunca na resposta; teste procura a mensagem da exceção, o nome do tipo e `.cs:line` no corpo |
| Limite de corpo | 1 MB — arquivo de comprovante sobe direto ao storage (ADR 0007) |
| OpenAPI | publicado apenas em desenvolvimento; teste confirma 404 fora dele |
| Segredos | nenhum valor versionado; `.env.example` documenta só nomes |

Os cabeçalhos são aplicados em `Response.OnStarting` para sobreviverem à limpeza da
resposta feita pelo tratamento de erro. Há teste confirmando que eles também
acompanham a resposta de falha.

## Testes

| Suíte | O que prova | Quantos |
|---|---|:---:|
| `UnitTests` | UUIDv7, relógio, contrato de erros, validação de correlação | 43 |
| `ArchitectureTests` | direção das dependências, isolamento do simulador, uso do relógio, ancoragem do content root | 16 |
| `IntegrationTests` | API real contra PostGIS real: saúde, migration, geoespacial, erros, cabeçalhos, CORS, correlação | 32 |
| Frontend (3 aplicações) | casca, roteamento, estados de conexão, validação de ambiente | 40 |

Os testes de integração usam PostgreSQL com PostGIS de verdade, por Testcontainers.
Provedor em memória não prova transação, constraint, índice nem geografia — que é
justamente o que este projeto precisa demonstrar.

## O que deliberadamente **não** existe ainda

Nenhuma entidade de negócio, nenhum caso de uso, nenhuma autenticação, nenhum endpoint
além dos de saúde. A camada `Application` está registrada e vazia: caso de uso nasce
junto com a regra que o justifica.

`Workers` sobe, confere que o banco está alcançável e encerra se não estiver — sem job
registrado. `Simulator` exercita o único contrato que a API publica hoje.

Isso é intencional. A ordem das fases está em [`ROADMAP.md`](../ROADMAP.md).
