# Decisões de arquitetura (ADR)

Cada arquivo registra uma decisão tomada, o contexto que a tornou necessária, as
alternativas recusadas e — principalmente — **como a decisão é verificada**. Decisão
sem verificação volta a ser desfeita por acidente no primeiro `using` conveniente.

Use [`0000-modelo.md`](./0000-modelo.md) como ponto de partida para um ADR novo.

| ADR | Decisão | Fase | Status |
|---|---|:---:|---|
| [0001](./0001-monolito-modular.md) | Monólito modular com workers separados | 0 | aceita |
| [0002](./0002-postgresql-postgis.md) | PostgreSQL com PostGIS como banco principal | 0 | aceita |
| [0003](./0003-tres-aplicacoes-web.md) | Três aplicações web separadas | 0 | aceita |
| [0004](./0004-uuidv7-como-identificador.md) | UUIDv7 como identificador interno | 0 | aceita |
| [0005](./0005-signalr-como-direcao-de-realtime.md) | SignalR como direção de tempo real | 0 / 8 | aceita |
| [0006](./0006-simulador-externo.md) | Simulador como cliente externo | 0 / 23 | aceita |
| [0007](./0007-storage-de-comprovantes-fora-do-banco.md) | Comprovantes em storage de objeto | 0 / 14 | aceita |

## Regras

- ADR aceito não é editado para mudar a decisão. Escreva um novo e marque o antigo
  como **substituído por ADR NNNN**.
- Correção de erro de digitação e acréscimo de evidência podem ser editados no próprio
  arquivo.
- Numeração é sequencial e nunca reaproveitada, mesmo quando um ADR é revogado.
