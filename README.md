# Torre Logística

Plataforma B2B de operação logística em tempo real: acompanhamento de entregas entre a
saída para rota e a conclusão, com localização, ETA, SLA, geofencing, alertas,
ocorrências, prova de entrega e rastreamento público controlado.

> **Estado: Fase 19 — Indicadores e Analytics concluída.**
> Login em canais separados, isolamento entre organizações, cadastros, entrega com timeline
> somente-inserção, rota do dia, execução por máquina de estados, telemetria GPS, geofence do destino
> no PostGIS, tempo real do console, previsão de chegada com SLA explicável, motor de alertas, a PWA do
> motorista e, agora, a **operação sem conexão**: toda ação do motorista nasce no aparelho com
> identificador próprio, fica guardada sem internet e é aplicada exatamente uma vez quando a conexão volta —
> resposta perdida não duplica, e cancelamento feito enquanto o motorista estava offline não é
> sobrescrito. Agora também as **ocorrências da última milha**: motivo tipado para cada falha, ocorrência
> somente-inserção com severidade, hora, lugar e autor, e alerta crítico na torre quando a mercadoria ou a
> segurança entram no caminho. Agora também a **prova de entrega**: quem recebeu, quando, onde e a foto —
> com o arquivo fora do banco, subindo direto ao storage por URL assinada de curta duração e saindo de lá do
> mesmo jeito, nunca por link público. E agora o **rastreamento público**: o destinatário acompanha a
> encomenda por um link com token forte — do qual o banco guarda só o hash —, vendo a região aproximada do
> veículo em vez da rua, e nunca o motorista, a rota ou o endereço completo. E agora os **indicadores
> operacionais**: pontualidade, sucesso na primeira tentativa, atraso médio, tempo por parada e tempo em
> rota, com os recortes por motorista, cliente, rota e motivo — cada número acompanhado da pergunta que
> responde e da definição de como foi calculado, e vazio quando não há base, porque "0%" e "não houve
> entrega" são fatos opostos. A ordem está em [`ROADMAP.md`](./ROADMAP.md).

## Stack

| Camada | Tecnologia |
|---|---|
| Backend | C# / .NET 10, ASP.NET Core |
| Banco | PostgreSQL 17 + PostGIS 3.5, EF Core 10 |
| Frontend | React 19, TypeScript estrito, Vite 8, React Router, TanStack Query, Zod |
| Tempo real | SignalR (canal do console) |
| Testes | xunit.v3, Testcontainers, Vitest, Testing Library |
| Observabilidade | Serilog estruturado; OpenTelemetry na Fase 21 |

## Estrutura

```text
src/
  TorreLogistica.Domain/          regras e invariantes — sem infraestrutura
  TorreLogistica.Application/     casos de uso
  TorreLogistica.Infrastructure/  PostgreSQL/PostGIS, relógio, identificador
  TorreLogistica.Api/             borda HTTP
  TorreLogistica.Workers/         carga assíncrona
  TorreLogistica.Simulator/       cliente externo, sem acesso ao núcleo

apps/
  operacao/        console operacional   :5173
  motorista/       PWA do motorista      :5174
  rastreamento/    rastreamento público  :5175

tests/
  TorreLogistica.UnitTests/
  TorreLogistica.IntegrationTests/    PostGIS real via Testcontainers
  TorreLogistica.ArchitectureTests/

docs/
  adr/             decisões de arquitetura
  arquitetura/
  operacao/        ambiente local, armadilhas da máquina
  seguranca/       gestão de segredos
```

## Como executar

Pré-requisitos: .NET SDK 10.0.400, Node ≥ 22.22, Docker com contêineres Linux.

```bash
cp .env.example .env        # e troque TORRE_POSTGRES_SENHA
docker compose up -d        # PostgreSQL + PostGIS na porta 55432
npm install

dotnet dotnet-ef database update \
  --project src/TorreLogistica.Infrastructure \
  --startup-project src/TorreLogistica.Infrastructure

dotnet run --project src/TorreLogistica.Api   # http://localhost:5080
npm run dev:operacao                          # http://localhost:5173
```

O passo a passo completo, incluindo três armadilhas reais desta máquina de
desenvolvimento, está em
[`docs/operacao/ambiente-local.md`](./docs/operacao/ambiente-local.md).

## Testes

```bash
powershell -ExecutionPolicy Bypass -File scripts/testar.ps1   # backend
npm run verificar                                             # frontend
```

| Suíte | Provas |
|---|:---:|
| Unidade | 695 |
| Arquitetura | 22 |
| Integração (PostgreSQL + PostGIS real) | 530 |
| Frontend (3 aplicações) | 127 |

Integração usa PostgreSQL com PostGIS de verdade, por Testcontainers. Provedor em
memória não prova transação, constraint, índice nem geografia — que é justamente o que
este projeto precisa demonstrar.

`dotnet test` não é usado: no SDK 10.0.400 com xunit.v3 4.0.0 ele encerra com "Zero
testes executados" enquanto o executável de teste roda tudo. O motivo está documentado
em [`docs/operacao/ambiente-local.md`](./docs/operacao/ambiente-local.md#por-que-não-dotnet-test).

## Decisões de arquitetura

| ADR | Decisão |
|---|---|
| [0001](./docs/adr/0001-monolito-modular.md) | Monólito modular com workers separados |
| [0002](./docs/adr/0002-postgresql-postgis.md) | PostgreSQL com PostGIS como banco principal |
| [0003](./docs/adr/0003-tres-aplicacoes-web.md) | Três aplicações web separadas |
| [0004](./docs/adr/0004-uuidv7-como-identificador.md) | UUIDv7 como identificador interno |
| [0005](./docs/adr/0005-signalr-como-direcao-de-realtime.md) | SignalR como direção de tempo real |
| [0006](./docs/adr/0006-simulador-externo.md) | Simulador como cliente externo |
| [0007](./docs/adr/0007-storage-de-comprovantes-fora-do-banco.md) | Comprovantes em storage de objeto |
| [0008](./docs/adr/0008-application-usa-ef-core-sem-repositorio.md) | Application usa o núcleo do EF Core, sem repositório |
| [0009](./docs/adr/0009-autenticacao-e-sessao.md) | Token curto, renovação rotativa em cookie, sessão conferida por requisição |
| [0010](./docs/adr/0010-multi-tenancy-e-isolamento.md) | Multi-tenancy por discriminador com filtro que falha fechado |
| [0011](./docs/adr/0011-cadastros-operacionais.md) | Cadastros com tipos de valor próprios, PostGIS atrás de conversão, versão por linha e inativação |
| [0012](./docs/adr/0012-entrega-como-agregado-central.md) | Entrega com código humano sem lacuna, endereço copiado, timeline numerada e regras em tabela |
| [0013](./docs/adr/0013-rotas-e-paradas.md) | Rotas com parada como associação, regras entre rotas por índice parcial e ordem versionada |
| [0014](./docs/adr/0014-maquina-de-estados-e-concorrencia.md) | Máquina de estados em tabela, comandos nomeados e concorrência decidida pela versão da linha |
| [0015](./docs/adr/0015-ingestao-de-localizacao.md) | Ingestão de localização: histórico e posição atual separados, gravação atômica e coleta mínima |
| [0016](./docs/adr/0016-geofence-de-destino.md) | Geofence de destino: avaliada na ingestão, distância no PostGIS, histerese e estado por entrega |
| [0017](./docs/adr/0017-tempo-real-da-operacao.md) | Tempo real da operação: aviso só depois do commit, grupo pela sessão, conexão que cai com a sessão |
| [0018](./docs/adr/0018-previsao-de-chegada-e-sla.md) | Previsão de chegada e SLA: cálculo explicável por rota, fora da requisição, contingência e histórico fotografado |
| [0019](./docs/adr/0019-motor-de-alertas-operacionais.md) | Motor de alertas: regras tipadas, uma chave por problema e ciclo de vida com reabertura |
| [0020](./docs/adr/0020-pwa-do-motorista.md) | PWA do motorista: leitura própria, ações grandes e GPS só em primeiro plano |
| [0021](./docs/adr/0021-operacao-offline.md) | Operação offline: fila no aparelho e registro da operação na mesma transação do efeito |
| [0022](./docs/adr/0022-ocorrencias-e-tentativas.md) | Ocorrências somente-inserção, com motivo tipado, severidade e alerta crítico |
| [0023](./docs/adr/0023-prova-de-entrega.md) | Prova de entrega: comprovante somente-inserção e arquivo só por URL assinada |
| [0024](./docs/adr/0024-rastreamento-publico.md) | Rastreamento público: token forte por hash, posição aproximada e resposta neutra |
| [0025](./docs/adr/0025-api-de-integracao.md) | API de integração: credencial de máquina, `/v1/` no caminho e idempotência em duas camadas |
| [0026](./docs/adr/0026-webhooks-e-backbone-assincrono.md) | Webhooks: outbox transacional, fila no PostgreSQL e desistência visível |
| [0027](./docs/adr/0027-console-operacional-e-mapa.md) | Console operacional: densidade sobre ornamento, e mapa sem fornecedor obrigatório |
| [0028](./docs/adr/0028-indicadores-operacionais.md) | Indicadores: agregação direta com índices, definição junto do número e vazio que não vira zero |

Cada ADR registra também **como a decisão é verificada** — decisão sem verificação volta
a ser desfeita por acidente.

## Documentação

- [`CLAUDE.md`](./CLAUDE.md) — governança técnica: arquitetura, invariantes, padrões, autonomia
- [`ROADMAP.md`](./ROADMAP.md) — fases, critérios de aceite e Security Gates
- [`docs/architecture.md`](./docs/architecture.md) — arquitetura em vigor
- [`docs/operacao/ambiente-local.md`](./docs/operacao/ambiente-local.md) — ambiente local
- [`docs/seguranca/gestao-de-segredos.md`](./docs/seguranca/gestao-de-segredos.md) — segredos
- [`docs/seguranca/matriz-de-autorizacao.md`](./docs/seguranca/matriz-de-autorizacao.md) — quem acessa o quê

## Segurança

Nenhum segredo é versionado. `.env.example` documenta apenas nomes de variável.

A CI roda varredura de segredos sobre o histórico completo, checagem de dependências
vulneráveis no backend e no frontend, e reprova a execução em qualquer achado.

Ver [`docs/seguranca/gestao-de-segredos.md`](./docs/seguranca/gestao-de-segredos.md).
