# Arquitetura — Torre Logística

> Estado: Fase 3 concluída (núcleo de entregas). Este documento cresce junto com as fases.
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
| `UnitTests` | UUIDv7, relógio, contrato de erros, correlação, regras de identidade, política de renovação, hash de senha, tipos de valor (endereço, telefone, placa, CNPJ, coordenada), regras dos cadastros, entrega (criação, alteração tudo ou nada, cancelamento, janela, código, tabelas de regra por status) | 280 |
| `ArchitectureTests` | direção das dependências, simulador isolado, relógio, content root, domínio sem setter público, Application sem Npgsql, domínio sem NetTopologySuite | 19 |
| `IntegrationTests` | API real contra PostgreSQL + PostGIS real: saúde, erros, borda, autenticação, renovação, reuso, prazos com relógio controlado, RBAC, isolamento entre tenants, gestão de contas, cadastros operacionais, entregas e timeline, código humano sob concorrência, concorrência otimista, geografia, auditoria, limite, logs | 255 |
| Frontend (3 aplicações) | casca, roteamento, conexão, ambiente, sessão do console (login, renovação serializada, logout) | 49 |

Os testes de integração usam PostgreSQL com PostGIS de verdade, por Testcontainers.
Provedor em memória não prova transação, constraint, índice nem geografia — que é
justamente o que este projeto precisa demonstrar.

## Fase 1 — Identidade e multi-tenancy

### Modelo

```text
organizacoes ──< usuarios ──< sessoes ──< tokens_de_renovacao
      └──────────────< eventos_de_auditoria   (somente-inserção, garantido por trigger)
```

| Conceito | Regra central |
|---|---|
| `Organizacao` | fronteira de isolamento; `slug` público identifica no login, mas não autoriza |
| `Usuario` | e-mail único **por organização**; perfil nunca atravessa canal (console ↔ motorista) |
| `Sessao` | a família de tokens de um login; prazo absoluto; revogada por logout, reuso, desativação ou troca de perfil |
| `TokenDeRenovacao` | só o hash SHA-256 é guardado; trocado a cada renovação |
| `EventoDeAuditoria` | trilha administrativa; `UPDATE`, `DELETE` e `TRUNCATE` recusados pelo banco |

### Autenticação

Token de acesso JWT de 15 minutos em memória no cliente; token de renovação opaco em cookie
`HttpOnly; Secure; SameSite=Strict` com `Path` restrito ao canal. Cada requisição autenticada
confere a sessão no banco — logout e revogação valem na hora. Renovação com detecção de reuso,
janela de tolerância para retry e trava de linha na sessão. Detalhes em
[ADR 0009](./adr/0009-autenticacao-e-sessao.md).

Console e PWA do motorista são **esquemas de autenticação diferentes**, com audiências
diferentes. Um token de um canal não autentica no outro.

### Tenant

O tenant vem exclusivamente da sessão. O contexto de persistência aplica filtro global por
organização em toda entidade de tenant, e sem sessão o filtro não enxerga nada. Recurso de outra
organização responde `404` idêntico ao de identificador inexistente. Campo desconhecido no JSON
é recusado. Detalhes em [ADR 0010](./adr/0010-multi-tenancy-e-isolamento.md).

### Acesso a dados

A Application usa o núcleo do EF Core diretamente, sem repositório. SQL específico do
PostgreSQL — trava de linha, detecção de violação de unicidade, transação com nova tentativa —
fica atrás de `IContextoDePersistencia` na Infrastructure. Ver
[ADR 0008](./adr/0008-application-usa-ef-core-sem-repositorio.md).

### Quem acessa o quê

[`docs/seguranca/matriz-de-autorizacao.md`](./seguranca/matriz-de-autorizacao.md), verificada
por teste contra o roteamento real.

## Fase 2 — Frota e estrutura operacional

### Modelo

```text
organizacoes ──< motoristas >── usuarios   (conta opcional; uma conta, um motorista)
             ──< veiculos
             ──< hubs            (localizacao geography(Point,4326), índice GiST)
             ──< clientes
             ──< destinatarios   (localizacao opcional, índice GiST)
```

| Conceito | Regra central |
|---|---|
| `Motorista` | recurso operacional, separado da conta que autentica; inativo não recebe nova atribuição |
| `Veiculo` | placa única na organização (antiga ou Mercosul); inativo não inicia nova rota |
| `Hub` | nome único na organização, sem diferença de acento ou caixa; endereço e coordenada obrigatórios |
| `Cliente` | contratante; CNPJ opcional (numérico ou alfanumérico), único na organização |
| `Destinatario` | endereço, contato mínimo e instruções; coordenada opcional |

### Tipos de valor

`Endereco` (Brasil: CEP, UF), `Telefone` (E.164), `PlacaDeVeiculo`, `Cnpj`,
`CoordenadaGeografica` e `TextoNormalizado` vivem no domínio, sem biblioteca externa. A
Infrastructure converte a coordenada no `Point` do NetTopologySuite — (longitude, latitude) — e o
endereço vira colunas da própria tabela.

### Alteração e concorrência

Todo cadastro expõe `versao` (coluna de sistema `xmin`). `PUT` exige a versão lida; divergência
ou gravação simultânea respondem `409 conflito_de_versao`. Ativar, inativar e associar conta são
comandos idempotentes. Não há `DELETE`: inativação preserva o que entregas futuras referenciarem.

### Auditoria e dado pessoal

Cada criação, alteração, ativação, inativação e associação de conta gera evento de auditoria
com **nomes** dos campos alterados, nunca valores — a trilha é somente-inserção e dado pessoal
gravado ali não poderia ser apagado.

### Quem acessa

Leitura: Administrador, Supervisor e Operador (`operacao:leitura`). Gestão: Administrador e
Supervisor (`operacao:gestao`). Detalhes em [ADR 0011](./adr/0011-cadastros-operacionais.md).

## Fase 3 — Núcleo de entregas

### Modelo

```text
organizacoes ──< entregas >── clientes
                    │    └──── destinatarios   (endereço e coordenada copiados na criação)
                    └──< eventos_da_entrega    (somente-inserção, garantido por trigger)
organizacoes ──< sequencias_de_codigo          (contador por organização, série e ano)
```

| Conceito | Regra central |
|---|---|
| `Entrega` | status muda só por operação (`Criar`, `Cancelar`); alteração exige versão e é tudo ou nada |
| `CodigoDaEntrega` | `ENT-AAAA-NNNNNN`, sequencial por organização e ano UTC, reservado na transação do insert |
| `JanelaDeEntrega` | início e fim em UTC, truncados ao segundo, no máximo 7 dias, fim no futuro |
| `EventoDaEntrega` | sequência sem lacuna por entrega e status resultante; nunca carrega dado pessoal |
| `RegrasDaEntrega` | tabelas "campo × status" e "cancelável por status", cobrindo os 9 status |

### Operações

`POST /api/entregas`, `PUT /api/entregas/{id}` (com `versao`), `POST /api/entregas/{id}/cancelamento`,
`GET /api/entregas` com filtros (status, cliente, destinatário, código, período da janela) e
`GET /api/entregas/{id}/eventos`. Não há rota que receba status nem `DELETE`. Operador cria,
altera e cancela (`entregas:operacao`). Detalhes em
[ADR 0012](./adr/0012-entrega-como-agregado-central.md).

## O que deliberadamente **não** existe ainda

Nenhuma rota, atribuição ou posição: chegam a partir da Fase 4. Os status Planejada, Atribuída,
Em rota, Próxima do destino, Entregue, Tentativa frustrada e Reagendada existem nas tabelas de
regra, mas nenhuma operação leva a entrega até eles ainda. As regras "motorista inativo não
recebe atribuição" e "veículo inativo não inicia rota" existem no domínio e passam a ser chamadas
na Fase 4. Não há tela de cadastro — o console operacional é da Fase 18. Não há convite nem conta com acesso a várias organizações — quem precisa de duas
organizações tem duas contas. A PWA do motorista ainda não tem tela de login; o endpoint existe
e é testado, a interface é da Fase 11.

`Workers` sobe, confere que o banco está alcançável e encerra se não estiver — sem job
registrado. `Simulator` exercita só o endpoint de prontidão.

Isso é intencional. A ordem das fases está em [`ROADMAP.md`](../ROADMAP.md).
