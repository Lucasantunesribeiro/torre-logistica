# Torre Logística

Plataforma B2B de operação logística em tempo real: acompanhamento de entregas entre a
saída para rota e a conclusão, com localização, ETA, SLA, geofencing, alertas,
ocorrências, prova de entrega e rastreamento público controlado.

> **Estado: Fase 0 — Fundação Técnica concluída.**
> Ainda não há funcionalidade de negócio. O que existe é a base sobre a qual ela será
> construída: camadas com fronteira verificada por teste, PostgreSQL com PostGIS real,
> contrato de erros, correlação, endpoints de saúde e três aplicações web compilando.
> A ordem das próximas fases está em [`ROADMAP.md`](./ROADMAP.md).

## Stack

| Camada | Tecnologia |
|---|---|
| Backend | C# / .NET 10, ASP.NET Core |
| Banco | PostgreSQL 17 + PostGIS 3.5, EF Core 10 |
| Frontend | React 19, TypeScript estrito, Vite 8, React Router, TanStack Query, Zod |
| Tempo real | SignalR (direção — Fase 8) |
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
| Unidade | 43 |
| Arquitetura | 16 |
| Integração (PostGIS real) | 32 |
| Frontend (3 aplicações) | 40 |

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

Cada ADR registra também **como a decisão é verificada** — decisão sem verificação volta
a ser desfeita por acidente.

## Documentação

- [`CLAUDE.md`](./CLAUDE.md) — governança técnica: arquitetura, invariantes, padrões, autonomia
- [`ROADMAP.md`](./ROADMAP.md) — fases, critérios de aceite e Security Gates
- [`docs/architecture.md`](./docs/architecture.md) — arquitetura em vigor
- [`docs/operacao/ambiente-local.md`](./docs/operacao/ambiente-local.md) — ambiente local
- [`docs/seguranca/gestao-de-segredos.md`](./docs/seguranca/gestao-de-segredos.md) — segredos

## Segurança

Nenhum segredo é versionado. `.env.example` documenta apenas nomes de variável.

A CI roda varredura de segredos sobre o histórico completo, checagem de dependências
vulneráveis no backend e no frontend, e reprova a execução em qualquer achado.

Ver [`docs/seguranca/gestao-de-segredos.md`](./docs/seguranca/gestao-de-segredos.md).
