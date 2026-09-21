# Arquitetura — Torre Logística

> Estado: Fase 10 concluída (motor de alertas operacionais). Este documento cresce junto com as fases.
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
| `UnitTests` | UUIDv7, relógio, contrato de erros, correlação, regras de identidade, política de renovação, hash de senha, tipos de valor (endereço, telefone, placa, CNPJ, coordenada), regras dos cadastros, entrega (criação, alteração tudo ou nada, cancelamento, janela, código, tabelas de regra por status), rota (paradas, ordem, atribuição, planejamento, cancelamento) e transições da entrega em rota, máquina de estados (comando × status), execução da entrega e da rota, política de aceitação de posição GPS, estado da geofence (entrada, histerese, reentrada, posição antiga) e proximidade, classificação do SLA nas bordas dos limiares, composição da chegada prevista, histórico de previsões fotografado, explicação em texto e validação das opções de previsão, ciclo de vida do alerta (deduplicação, resolução, reabertura, decisão do operador), regras de alerta nas bordas, descrição e limites | 527 |
| `ArchitectureTests` | direção das dependências, simulador isolado, relógio, content root, domínio sem setter público, Application sem Npgsql, domínio sem NetTopologySuite, status só por comando da máquina de estados | 22 |
| `IntegrationTests` | API real contra PostgreSQL + PostGIS real: saúde, erros, borda, autenticação, renovação, reuso, prazos com relógio controlado, RBAC, isolamento entre tenants, gestão de contas, cadastros operacionais, entregas e timeline, código humano sob concorrência, montagem de rotas com regras entre rotas sob concorrência, execução pelo motorista, reatribuição concorrente com conclusão, ausência de endpoint genérico de status, ingestão de GPS (fora de ordem, duplicata, lote parcial, lotes simultâneos, métricas), geofence com pontos gerados no PostGIS (borda, duplicado, fora de ordem, reentrada, outro tenant, chegada simultânea), tempo real com cliente SignalR real (aviso, isolamento, reconexão, sessão revogada), previsão de chegada e SLA com relógio controlado e processador em segundo plano (Normal para Risco explicado, reavaliação sem evento, paradas anteriores, provedor que falha ou trava, sem provedor, histórico somente-inserção, aviso de risco), alertas (entrega problemática identificada pela lista, sem duplicar, offline que resolve e reabre, parado pela permanência no PostGIS, tentativas, resolução pelo operador, outra organização, ciclo de vida somente-inserção), leitura do motorista para a PWA (fluxo básico só pelas rotas do aplicativo, só o que é dele, reatribuída), concorrência otimista, geografia, auditoria, limite, logs | 394 |
| Frontend (3 aplicações) | casca, roteamento, conexão, ambiente, sessão do console (login, renovação serializada, logout); PWA do motorista: fluxo básico completo, navegação, sessão encerrada, erros e conflito, estados vazios, permissão de localização negada, sem internet, rastreador de GPS, UUIDv7 do aparelho e responsividade | 78 |

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

## Fase 4 — Rotas e paradas

### Modelo

```text
organizacoes ──< rotas ──< paradas >── entregas   (uma parada ativa por entrega)
                  │  ├──── motoristas / veiculos   (uma rota ativa por dia)
                  │  └──── hubs                    (saída, opcional)
                  └──< eventos_da_rota             (somente-inserção, garantido por trigger)
```

| Conceito | Regra central |
|---|---|
| `Rota` | dona das paradas; mudança estrutural só em montagem ou planejada; planejar exige parada, motorista, veículo e saída futura |
| `Parada` | associação rota–entrega com sequência; retirada fica inativa com motivo |
| `VersaoDaOrdem` | sobe a cada inclusão, retirada ou reordenação; reordenar exige a versão lida da rota |
| Regras entre rotas | entrega, motorista e veículo em no máximo uma rota ativa — índices únicos parciais |
| Entrega em rota | `Planejar`, `Atribuir`, `RetirarDaRota`; cancelar entrega em rota retira a parada |

### Operações

`POST /api/rotas`, `POST .../paradas`, `DELETE .../paradas/{entregaId}`, `PUT .../ordem`,
`PUT .../motorista`, `PUT .../veiculo`, `PUT .../saida`, `POST .../planejamento`,
`POST .../cancelamento`, `GET /api/rotas` (data, status, motorista), `GET /api/rotas/{id}` com a
sequência e `GET .../eventos`. Supervisor e administrador montam; operador consulta. Detalhes em
[ADR 0013](./adr/0013-rotas-e-paradas.md).

## Fase 5 — Máquina de estados e concorrência

### Máquina de estados

`MaquinaDeEstadosDaEntrega` é a tabela comando → (status de origem → status resultante). Dentro da
`Entrega`, um único método privado muda `Status`, consultando a tabela; o que não está nela responde
`409`. Nada sai de Entregue ou Cancelada.

```text
Criada ─Planejar→ Planejada ─Atribuir→ Atribuída ─IniciarRota→ EmRota ─RegistrarChegada→ PróximaDoDestino
                                                                  │                          │
                                                                  ├──────── Concluir ────────┴→ Entregue
                                                                  └─ RegistrarTentativaFrustrada → TentativaFrustrada ─Reagendar→ Reagendada ─Planejar→ …
Cancelar: de Criada, Planejada, Atribuída, TentativaFrustrada, Reagendada
```

A rota ganhou `Iniciar` (planejada → em andamento, pelo motorista da rota) e `Concluir` (em
andamento → concluída, com todas as entregas resolvidas). A troca de motorista é aceita também em
andamento; as demais mudanças estruturais, não.

### Quem comanda

| Canal | Comandos |
|---|---|
| Motorista (`/api/motorista/...`) | iniciar e concluir rota; chegada, conclusão e tentativa sem sucesso da entrega |
| Console | reagendar entrega; reatribuir motorista da rota |

O motorista é resolvido pela conta da sessão. Entrega que foi dele e passou a outro responde
`409 entrega_reatribuida`; as demais que não são dele, `404`.

### Concorrência

Todo comando grava com a versão da linha lida. No cenário "operador reatribui enquanto motorista
conclui", quem grava primeiro prevalece e o outro recebe `409`. Comandos repetidos com o resultado
já aplicado respondem `200` sem novo evento. Detalhes em
[ADR 0014](./adr/0014-maquina-de-estados-e-concorrencia.md).

## Fase 6 — Ingestão de localização

### Modelo

```text
motoristas ──< posicoes          (histórico: capturada_em ≠ recebida_em; UPDATE recusado)
motoristas ─── posicoes_atuais   (uma linha por motorista; só avança, nunca regride)
rotas ──< posicoes               (a rota em execução na captura)
```

### Caminho de uma posição

```text
POST /api/motorista/posicoes (1 a 500)
  → motorista da sessão + janelas das rotas iniciadas por ele
  → transação + advisory lock do motorista
  → para cada item: PoliticaDeLocalizacao (recusa com motivo, ou Confiável/Imprecisa)
  → um comando SQL:
       INSERT posicoes ... ON CONFLICT DO NOTHING                       (duplicata)
       UPSERT posicoes_atuais ... WHERE (captura, sequência) < (nova)   (nunca regride)
  → commit → métricas → uma linha de log por lote
  → 200 com resultado por item
```

| Regra | Onde |
|---|---|
| Recusa: coordenada, precisão > 2 km, velocidade, direção, futuro > 2 min, mais de 7 dias, fora de rota | `PoliticaDeLocalizacao`, `PosicaoDoMotorista.Registrar` |
| Imprecisa (100 m a 2 km): só histórico | idem |
| Duplicata | índice único organização + motorista + evento |
| Fora de ordem: só histórico | `WHERE` do `UPSERT`, no banco |

Posição atual para qualquer perfil do console; histórico só para gestão, por até 24 h. Limite de
envio por motorista. Métricas `tracking.*` no medidor `TorreLogistica.Rastreamento`. Detalhes em
[ADR 0015](./adr/0015-ingestao-de-localizacao.md).

## Fase 7 — PostGIS e geofencing

### Caminho de uma posição que avança

```text
RegistrarPosicaoAsync → avançou a posição atual?
  └─ sim → entregas em execução do motorista com coordenada de destino
            → PostGIS: ST_Distance, ST_DWithin(300 m), ST_DWithin(350 m)   (geography)
            → EstadoDeGeofence.Avaliar
                 fora → dentro (≤ 300 m)  : EventoDeGeofence Entrada + Entrega.RegistrarProximidade
                 dentro → fora (> 350 m)  : EventoDeGeofence Saida
                 posição antiga            : ignorada
  → SaveChanges no fim do lote, na mesma transação
```

| Tabela | Conteúdo |
|---|---|
| `estados_de_geofence` | uma linha por entrega: dentro/fora, distância, última captura e sequência avaliadas, entradas |
| `eventos_de_geofence` | entradas e saídas com distância, raio e evento de localização; somente-inserção |

A primeira entrada de uma entrega em rota executa `RegistrarProximidade` (Em rota → Próxima do
destino), equivalente e idempotente com a chegada manual. Conflito com comando do motorista refaz o
lote inteiro. Consulta em `GET /api/entregas/{id}/geofence`. Detalhes em
[ADR 0016](./adr/0016-geofence-de-destino.md).

## Fase 8 — Tempo real com SignalR

### Caminho de um aviso

```text
caso de uso → SaveChanges
  └─ TorreLogisticaDbContext recolhe: eventos de entrega que mudam status, sessões revogadas
     (e RegistrarPosicaoAsync recolhe a posição atual que avançou)
  → commit (ou gravação sem transação)
  → IPublicadorDeTempoReal (Application)
       └─ PublicadorDeTempoRealSignalR (Api) → grupo organizacao:{id}
            DriverPositionUpdated · DeliveryStatusChanged
       └─ sessões revogadas → RegistroDeConexoesDaOperacao derruba as conexões delas
```

| Peça | Regra |
|---|---|
| `/tempo-real/operacao` | política do console; grupo pela organização do token; nenhum método chamável pelo cliente |
| Token | cabeçalho, ou `access_token` na query string só neste caminho |
| Conexão | cai com a sessão revogada e no vencimento do token |
| Publicação | só depois do commit; melhor esforço, nunca derruba a operação |

Implementação na `Api`: a `Infrastructure` não pode depender de ASP.NET Core. Uma instância, sem
backplane. Detalhes em [ADR 0017](./adr/0017-tempo-real-da-operacao.md).

## Fase 9 — ETA e SLA

### Caminho de uma previsão

```text
posição atual avançou · entrega saiu, chegou, concluiu, falhou, foi reatribuída
  → TorreLogisticaDbContext, depois do commit → ISolicitacoesDeRecalculoDePrevisao
  → FilaDeRecalculoDePrevisoes (memória, pedidos repetidos colapsados)
  → ProcessadorDePrevisoes (BackgroundService hospedado pela API, um consumidor, tenant do pedido)
       └─ RecalculoDePrevisoes (Application), rota inteira:
            IProvedorDeRotas ── tempo limite / falha / ausente ──► contingência em linha reta (PostGIS)
            CalculadoraDeChegada + RegrasDeSla (Domain)
            previsoes_da_entrega (atual) + registros_de_previsao (somente-inserção)
       → commit → DeliveryRiskChanged, quando a situação muda
reavaliação periódica (TimeProvider) → enfileira as rotas em andamento: o tempo passa sem evento
```

| Peça | Regra |
|---|---|
| Chegada prevista | agora + deslocamento acumulado + atendimento das paradas pendentes antes; parada no destino usa a chegada registrada |
| Situação | pela folga até o fim da janela: Normal, Atenção (< 20 min), Risco (< 5 min ou depois da janela), Atrasada (janela encerrada) |
| Provedor | `IProvedorDeRotas`; nesta fase, o simulado (PostGIS × sinuosidade ÷ velocidade) |
| Histórico | inicial, mudança de situação, chegada que anda ≥ 2 min, encerramento; cada registro fotografa janela e limiares |
| Consulta | `GET /api/entregas/{id}/previsao`, com a explicação de cada mudança |

Parâmetros em `Torre:Previsao`, documentados e com os motivos em [ADR 0018](./adr/0018-previsao-de-chegada-e-sla.md).

## Fase 10 — Motor de alertas operacionais

```text
ProcessadorDePrevisoes, por rota (evento ou reavaliação periódica)
  → RecalculoDePrevisoes (previsão confirmada)
  → MonitoramentoOperacional (Application)
       fatos: previsões, entregas, posição atual, permanência no raio (PostGIS)
       RegrasDeAlerta (Domain) → uma constatação por tipo e alvo, valendo ou não
       AlertaOperacional.Constatar → abre · mantém sem evento · resolve · reabre · alerta novo
       alertas_operacionais (índice único parcial: um aberto por chave) + eventos_de_alerta (somente-inserção)
  → commit → AlertCreated · AlertResolved
```

| Peça | Regra |
|---|---|
| Tipos | RiscoDeAtraso, EntregaAtrasada, MotoristaOffline, ParadoTempoExcessivo, TentativasExcedidas; OcorrenciaCritica definida, produzida na Fase 13 |
| Deduplicação | chave tipo + alvo; um alerta aberto por chave, garantido no banco |
| Ciclo de vida | resolve sozinho; reabre em até 30 min; depois, alerta novo; resolução do operador não é desfeita enquanto a condição persiste |
| Consulta | `GET /api/alertas` priorizada por severidade e antiguidade, com descrição e evidência; resolução pelo operador |

Limites em `Torre:Alertas`; motivos em [ADR 0019](./adr/0019-motor-de-alertas-operacionais.md).

## Fase 11 — PWA do motorista

```text
apps/motorista (React, TanStack Query, Zod)
  sessão: /api/motorista/autenticacao — token em memória, renovação por cookie, cache limpo ao sair
  leitura: GET /api/motorista/rotas · /rotas/{id} · /entregas/{id}  (ConsultaDoMotorista, modelo próprio)
  comandos: início · chegada · conclusão (com confirmação) · tentativa com motivo · encerramento da rota
  GPS: RastreadorDeLocalizacao — watchPosition só com rota em andamento e app aberto
       → lote a cada 15 s, UUIDv7 e sequência do aparelho → POST /api/motorista/posicoes
```

| Tela | Caminho |
|---|---|
| Entrar | `/entrar` |
| Rota do dia e próxima entrega | `/` |
| Lista de paradas | `/rotas/{id}/paradas` |
| Detalhe da entrega (chegada, conclusão, mapa, ligar, ocorrências) | `/entregas/{id}` |
| Registrar ocorrência (tentativa sem sucesso com motivo) | `/entregas/{id}/ocorrencia` |

Uma coluna, ações com alvo de toque de 3,5rem, estado de conexão e de localização sempre visíveis. Sem
service worker nesta fase — offline é a Fase 12. Limitações reais de localização em navegador e decisões
em [ADR 0020](./adr/0020-pwa-do-motorista.md).

## Fase 12 — Offline, sincronização e idempotência

```text
PWA: ação do motorista
  → IndexedDB `operacoes` (ClientOperationId UUIDv7, tipo, payload, criadaEm, status, tentativas)
  → tela projeta a ação sobre a leitura (mesmas transições da API)
  → Sincronizador (abertura · online · visível · depois da ação · 30 s; um envio por vez, lotes de 100)
  → POST /api/motorista/sincronizacao
API: SincronizacaoDoMotorista, uma transação por operação, em ordem
  → fila por motorista (pg_advisory_xact_lock) → registro existe? devolve o mesmo desfecho (repetida)
  → senão: INSERT operacoes_do_cliente (Aplicada) + comando de ExecucaoPeloMotorista → commit junto
  → erro de domínio: registra Conflito/Recusada em transação própria · conflito de versão: reprocessa, TentarDeNovo
  → 200 { resultados: [{ operacaoDoClienteId, desfecho, repetida, codigo, mensagem }] }
PWA: Aplicada → Synced · Conflito → Conflict · Recusada → Failed · rede/5xx/401/429/TentarDeNovo → Pending
```

| Peça | Regra |
|---|---|
| Exatamente uma vez | registro único `(organização, motorista, operação)`, gravado na mesma transação do efeito; somente-inserção por trigger |
| Conflito durante o offline | a ação atrasada passa pela máquina de estados atual; cancelamento ou reatribuição prevalecem; conflito é definitivo e mostrado até "Entendi" |
| Validação | UUIDv7, tipo e motivo coerentes, criada há no máximo 7 dias e no máximo 2 min no futuro; lote de 1 a 100 |
| Leituras sem rede | cópia em IndexedDB com o instante; 401/404/409 não caem para a cópia |
| Sessão sem rede | identidade guardada (sem token) por 7 dias desde a última confirmação; saída sem rede é confirmada no servidor antes da próxima renovação |
| Posições | pendentes também no IndexedDB |
| Service worker | só a casca do aplicativo; nunca a API |

Decisões e limitações em [ADR 0021](./adr/0021-operacao-offline.md).

## Fase 13 — Ocorrências e tentativas de entrega

```text
motorista: tentativa sem sucesso (muda status)
  → POST /api/motorista/entregas/{id}/tentativa-frustrada  (motivo tipado + descrição só em "Outro")
  → mesmo commit: status TentativaFrustrada + evento da timeline + ocorrencia (TentativaDeEntrega)
motorista: ocorrência que não muda status
  → POST /api/motorista/entregas/{id}/ocorrencia  (veículo, mercadoria, incidente, acesso)
operação: POST /api/entregas/{id}/ocorrencias · consulta: GET /api/ocorrencias · GET /api/entregas/{id}/ocorrencias
ocorrencias (somente-inserção): tipo · severidade · motivo tipado · observação · localização ·
             ocorrida_em × registrada_em · origem (motorista/operação) · autor
  → severidade Critica → RegrasDeAlerta.OcorrenciaCritica → alerta Critica, resolvido quando a entrega sai da operação
  → commit → IncidentCreated (sem texto e sem coordenada)
```

| Peça | Regra |
|---|---|
| Motivos tipados | DestinatarioAusente, EnderecoNaoLocalizado, RecusadaPeloDestinatario, LocalFechado, AcessoImpedido, ProblemaComVeiculo, ProblemaComMercadoria, Outro |
| Texto livre | complemento, nunca única estrutura; exigido só em tipo `Outro` ou motivo `Outro` (regra no domínio e `CHECK` no banco) |
| Severidade | do catálogo por tipo e motivo (mercadoria e incidente nascem críticos); quem registra pode informar outra |
| Rastro | o que, quando aconteceu, quando foi registrado, onde, quem registrou e por qual canal |
| Privacidade | a observação não entra na timeline (só `comDescricao`) nem na evidência do alerta; coordenada não vai ao tempo real |
| Offline | a tentativa vai pela fila do aparelho com a descrição; ela faz parte da identidade da operação |

Decisões e limitações em [ADR 0022](./adr/0022-ocorrencias-e-tentativas.md).

## Fase 14 — Prova de entrega

```text
aparelho: POST /api/motorista/entregas/{id}/comprovante/autorizacao  (tipo + tipo de conteúdo)
  → IObjectStorage.AutorizarEnvio → URL assinada curta (HMAC sobre operação, chave, tipo, tamanho, expiração)
aparelho: PUT /api/arquivos/{chave}?expiraEm&assinatura&tipoDeConteudo&tamanhoMaximo   (direto ao storage)
  → confere tipo e tamanho autorizados, corta no limite, calcula SHA-256
aparelho: POST /api/motorista/entregas/{id}/comprovante  (quem recebeu, observação, posição, arquivos)
  → confere cada objeto no storage → comprovantes + arquivos_do_comprovante + evento Entregue, no mesmo commit
console: GET /api/entregas/{id}/comprovante → metadados + URLs assinadas de leitura (curta)
```

| Peça | Regra |
|---|---|
| Binário | nunca no PostgreSQL; nunca pela API de negócio (limite de corpo de 1 MB continua valendo) |
| Metadados | tipo, chave, tipo de conteúdo, tamanho, SHA-256 e instante — vindos do storage, não do cliente |
| Unicidade | um comprovante por entrega; repetir o registro devolve o mesmo estado, sem duplicar prova |
| Integridade | `comprovantes` e `arquivos_do_comprovante` são somente-inserção por trigger |
| Autorização | registrar é do motorista da entrega; ler é do console; arquivo só por URL assinada no prazo |
| Política | `Torre:Comprovantes:ExigirNaConclusao` recusa conclusão sem prova (`422 comprovante_obrigatorio`) |
| Storage desta fase | disco local com URL assinada servida pela API; S3 ou compatível é decisão da Fase 25 |

Decisões e limitações em [ADR 0023](./adr/0023-prova-de-entrega.md), que implementa o [ADR 0007](./adr/0007-storage-de-comprovantes-fora-do-banco.md).

## Fase 15 — Rastreamento público

```text
console: POST /api/entregas/{id}/link-de-rastreamento          (entregas:operacao)
  → token forte de 32 bytes em Base64Url; o banco guarda só o SHA-256
  → revoga o link anterior e grava o novo na mesma transação (índice único parcial garante um ativo)
  → devolve o valor UMA vez; perdido, é reemitido, nunca recuperado

destinatário: GET /api/publico/rastreamento/{token}            (anônimo, limite por endereço)
  → formato implausível é descartado antes do banco
  → SHA-256 do apresentado → tokens_de_rastreamento → organização e entrega
  → entrega + marcos públicos + previsão + posição aproximada + comprovante,
    com IgnoreQueryFilters e comparação explícita com a organização DO TOKEN
```

| Peça | Regra |
|---|---|
| Token | 32 bytes de entropia; só o hash é persistido, como nos tokens de renovação |
| Um por entrega | índice único parcial `revogado_em IS NULL`; emitir de novo derruba o link repassado adiante |
| Resposta neutra | malformado, desconhecido, expirado e revogado → o mesmo `404 rastreamento_nao_encontrado` |
| Autoridade | é o token, nunca a requisição: sem sessão, o filtro de tenant é substituído por comparação explícita |
| Posição | só em `EmRota` e `ProximaDoDestino`, arredondada em grade de 0,01° (~1,1 km) e descartada após 15 min |
| Fora da página | motorista, veículo, rota, outras entregas, logradouro, número, CEP, autor dos eventos e dados em JSON |
| Timeline | só marcos que dizem respeito à encomenda; planejamento, atribuição e troca de motorista ficam de fora |
| Comprovante | depois da conclusão, por URL assinada curta (ADR 0023); desligável em `Torre:RastreamentoPublico:ExporComprovante` |
| Limite | `limite-rastreamento-publico`, por endereço — a superfície não tem conta para particionar |

Decisões e limitações em [ADR 0024](./adr/0024-rastreamento-publico.md).

## Fase 16 — API de integração e importação

```text
console (administrador): POST /api/integracoes        → chave tlog.<identificador>.<segredo>, mostrada UMA vez
                         POST /api/integracoes/{id}/revogacao

ERP: POST /api/integracoes/v1/entregas                (Authorization: Bearer <chave>, Idempotency-Key)
  → chave conferida pelo identificador público + SHA-256 do segredo
  → idempotência por chave (com hash do corpo) e por identificador de origem
  → registro de idempotência e vínculo de origem no MESMO commit da entrega
ERP: GET  /api/integracoes/v1/entregas/{id}           (acompanha sem abrir o console)
ERP: POST /api/integracoes/v1/importacoes/previa      (confere o CSV sem gravar)
ERP: POST /api/integracoes/v1/importacoes             (chave por linha = hash do arquivo + número)
```

| Peça | Regra |
|---|---|
| Credencial | esquema próprio, sem sessão, sem perfil e sem canal; vale até ser revogada |
| Segredo | 32 bytes sorteados, guardados como SHA-256 — não com hasher de senha, que só puniria o integrador |
| Autor | integração não é pessoa: a timeline registra autor vazio e a auditoria guarda qual credencial agiu |
| Versão | `/v1/` no caminho: o ERP do cliente não atualiza junto com o nosso frontend |
| Replay | mesma chave e mesmo corpo devolvem 200 com a entrega anterior; corpo diferente é 409 |
| Fila reprocessada | mesmo identificador de origem com chave nova devolve 200, sem criar outra entrega |
| Idempotência somente-inserção | `requisicoes_de_integracao` e `referencias_externas_de_entrega` são protegidas por trigger |
| CSV | leitor próprio (sem dependência nova); prévia recusa o lote inteiro antes de gravar; reenvio do arquivo não duplica |
| Limite | `limite-integracao`, por credencial — um integrador afobado não atinge os outros |

Decisões e limitações em [ADR 0025](./adr/0025-api-de-integracao.md).

## Fase 17 — Webhooks e backbone assíncrono

```text
SaveChanges do fato ──┬── entrega/evento/auditoria
                      └── linha no outbox            (MESMA transação)

despachante (5 s): reserva outbox com FOR UPDATE SKIP LOCKED
  → uma entrega por assinatura interessada (única por assinatura+mensagem)
  → marca a mensagem despachada

entregador (5 s): reserva entregas, ARRENDA (adia) e libera o banco
  → POST assinado, fora da transação, com tempo limite de 10 s
  → registra tentativa; falhou, adia com backoff; esgotou, vai para Falhada

console (administrador): assina, revoga, consulta e REENVIA o que desistiu
```

| Peça | Regra |
|---|---|
| Outbox | gravado pelo próprio contexto de persistência: não há caso de uso que possa esquecer |
| Fila | tabela + `FOR UPDATE SKIP LOCKED`; SQS fica para a Fase 25, atrás da mesma separação do ADR 0007 |
| Arrendamento | a reserva adia a disponibilidade e libera o banco; processo que morre devolve a entrega sozinho |
| Entrega no mínimo uma vez | consequência aceita: o assinante deduplica pelo `X-Torre-Event-Id` |
| Backoff | 30 s, 2 min, 8 min, 32 min, 2 h, 2 h — soma que dá tempo de o assinante voltar do ar |
| Desistência | estado `Falhada` visível, com histórico completo; a volta é reenvio manual, por uma pessoa |
| Assinatura | `X-Torre-Signature: t=…,v1=…`, HMAC sobre `t.corpo` — o carimbo impede reenvio eterno |
| Segredo | cifrado com AES-GCM (precisa ser recuperável para assinar), nunca em log, consulta ou auditoria |
| SSRF | destino resolvido e conferido; loopback, privadas, link-local e metadados recusados; sem redirecionamento |
| Histórico | `tentativas_de_webhook` é somente-inserção por trigger |

Decisões e limitações em [ADR 0026](./adr/0026-webhooks-e-backbone-assincrono.md).

## Fase 18 — Console operacional e mapa

```text
/            Painel operacional — contadores e a fila do que precisa de gente
/mapa        Mapa da Operação (tela símbolo) — pontos ao vivo + lista sincronizada
/entregas    lista com filtro por status e busca por código
/entregas/:id  estado, previsão explicada, timeline, ocorrências, prova e link de rastreamento
/rotas       rotas do dia, com paradas pendentes na própria linha
/motoristas  lista e detalhe com a última posição conhecida
/alertas     abertos e resolvidos, com resolução que exige observação
/ocorrencias registro da última milha
/integracoes credenciais de máquina (chave mostrada uma vez)
/webhooks    assinaturas e fila de entregas, com reenvio do que desistiu
```

| Peça | Regra |
|---|---|
| Mapa | MapLibre GL; o **estilo vem de configuração** e, sem ele, os pontos aparecem sobre fundo neutro |
| Provedor de tiles | nenhum padrão embutido: padrão embutido é conta que alguém paga sem saber |
| Tempo real | aviso **invalida** a consulta e a API reconta; só posição entra direto no mapa |
| Sem tempo real | a tela continua correta, apenas mais lenta; a queda é anunciada, não disfarçada |
| Estado de tela | carregando, erro e vazio num componente só, para as onze telas errarem igual |
| Consultas do detalhe | independentes: falta de comprovante ou previsão não apaga o resto |
| UX | tabela densa, linha fina, cor só com significado; sem vidro fosco, gradiente ou cartão decorativo |
| Painel | sem gráfico — responde "há algo exigindo ação agora?"; série histórica é da Fase 19 |

Decisões e limitações em [ADR 0027](./adr/0027-console-operacional-e-mapa.md).

## Fase 19 — Indicadores operacionais

```text
GET /api/indicadores?de=&ate=     agregação do período; sem parâmetro, os últimos 30 dias
/indicadores                      a tela, no console
```

| Peça | Regra |
|---|---|
| Cálculo | agregação direta no PostgreSQL, sem projeção materializada, sem cache e sem job |
| Recorte | pelo **instante da conclusão**, não pela criação: "como foi a semana" é sobre o que aconteceu nela |
| Período | início inclusivo, fim exclusivo, sempre normalizado para UTC; teto de 186 dias, validado antes da consulta |
| Cancelada | fora do denominador — não é falha de pontualidade; aparece como contagem própria |
| Sem base | valor **nulo**, nunca zero: "0%" e "não houve entrega" são fatos opostos |
| Definição | campo da resposta da API, exibido embaixo do número — cálculo e explicação saem do mesmo lugar |
| Rota da entrega | a parada que a carregava **no instante da conclusão**; parada ativa não serve, porque concluir a rota desativa todas |
| Índices | `entregue_em` e `cancelada_em` parciais por organização; histórico de paradas por entrega |
| Gráficos | nenhum, além da barra proporcional atrás da quantidade; cada recorte tem a pergunta impressa acima |

Decisões e limitações em [ADR 0028](./adr/0028-indicadores-operacionais.md).

## Fase 20 — Retenção de localização

```text
Torre:Retencao:PosicoesBrutas      30 dias (piso de 1 dia)
Torre:Retencao:Intervalo           6 horas
Torre:Retencao:TamanhoDoLote       5.000 linhas por comando
Torre:Retencao:LotesPorRodada      20
```

| Peça | Regra |
|---|---|
| Alvo | só `posicoes` — o caminho percorrido; a projeção de posição atual e os eventos operacionais ficam |
| Corte | pelo **recebimento**, carimbo do servidor; captura é carimbo do aparelho, e aparelho é cliente |
| Mecânica | `DELETE` por `ctid` em lotes, cada um confirmando sozinho, com teto por rodada e aviso quando sobra |
| Alcance | atravessa organizações: o prazo é do sistema e a varredura é por idade |
| Serviço de fundo | sempre registrado; quem decide rodar é ele, lendo `IOptions` — no registro a configuração ainda não está completa |
| Métrica | `retention.positions.deleted` |

O modelo de segurança completo — autoridades, isolamento, sessão, borda, privacidade de localização e o
que ainda **não** está coberto — está em [`security-model.md`](./security-model.md), e as decisões desta
fase em [ADR 0029](./adr/0029-retencao-de-localizacao.md).

## Fase 21 — Observabilidade

```text
HTTP → domínio → outbox (coluna rastro) → despachante → webhook
         └── o traceparent viaja com a mensagem e reaparece no POST ao assinante
```

| Peça | Regra |
|---|---|
| Exportação | OTLP; sem `Torre:Observabilidade:EnderecoOtlp`, nada sai do processo |
| Amostragem | `ParentBasedSampler` sobre razão configurável: rastro amostrado continua inteiro |
| Banco | fonte `Npgsql`, publicada pelo provedor — sem pacote de instrumentação |
| Trabalho de fundo | `outbox.despacho`, `webhook.entrega`, `retencao.limpeza`, na fonte `TorreLogistica.Operacao` |
| Fila | o `traceparent` é gravado na mesma transação do fato e herdado pela entrega de webhook |
| Despachante | rastro próprio: o lote junta origens diferentes, e pendurá-lo numa delas seria mentira |
| Medidas de estado | retrato em memória atualizado a cada 30 s; falha de leitura mantém o retrato anterior |
| Log | `TraceId` e `SpanId` no escopo, ao lado do `IdDeCorrelacao` |

Decisões e limitações em [ADR 0030](./adr/0030-observabilidade.md).

## Fase 22 — O que a medição decidiu

```text
33 posições/s sustentadas · p50 31,5 ms · p99 245 ms (concorrência, não volume)
2,85 M posições (um dia) · 1.023 MB, dos quais 528 MB de índice
histórico por motorista: 0,19 ms · limpeza de 5.000: 12 ms
```

| Decisão | Por quê |
|---|---|
| `posicoes` **não** é particionada | com um dia de volume, consulta e limpeza usam índice e respondem em milissegundos; particionar não resolveria o custo que a medição achou, que é o peso dos índices |
| Gatilho para rever | o aviso *rodada encerrada no teto de lotes* da retenção: aí o expurgo passaria a valer como `DROP PARTITION` |
| `57P01` não é retentado | o provedor não o classifica como transitório, e repetir escrita sozinho custa mais que um erro isolado num reinício planejado |
| O benchmark fica desligado | sob contenção com a suíte, ele mede errado e vira teste instável |

Números, ambiente e gargalos em [`performance.md`](./performance.md); decisões em
[ADR 0031](./adr/0031-performance-e-resiliencia.md).

## O que deliberadamente **não** existe ainda

A ocorrência não tem anexo de foto nem
fluxo próprio de tratamento — quem tem ciclo de vida é o alerta. A ocorrência que não muda status ainda não
passa pela fila offline: exige conexão. Não há alerta de desvio de rota — exige traçado
planejado, que o provedor simulado não produz. Não há notificação fora do console (e-mail, push). Não há provedor de rotas real — o
simulado não sabe de ruas nem de trânsito, e escolher fornecedor tem custo —, nem limiar de SLA por
organização ou cliente, nem classificação de chegada antes da janela. Não há backplane para mais de uma
instância. Não há geofence de hub, raio
configurável por organização nem detecção de salto impossível entre posições. Não há particionamento
temporal do histórico, nem coletor de telemetria escolhido — os instrumentos existem e a exportação
fica desligada até alguém apontar um endereço OTLP (Fase 25). Não há retenção da tabela de operações do aparelho. Não há fuso horário configurado por organização: datas de rota usam
UTC com um dia de tolerância. Não há tela de cadastro: o console lê a estrutura operacional, mas criar e alterar continua sendo trabalho da API. Não há convite nem conta com acesso a várias organizações — quem precisa de duas
organizações tem duas contas. Não há localização em segundo plano na PWA: o navegador não garante, e o
aplicativo avisa o motorista para mantê-lo aberto (ADR 0020).

`Workers` sobe, confere que o banco está alcançável e encerra se não estiver — sem job
registrado. `Simulator` exercita só o endpoint de prontidão.

Isso é intencional. A ordem das fases está em [`ROADMAP.md`](../ROADMAP.md).
