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
| [0008](./0008-application-usa-ef-core-sem-repositorio.md) | Application usa o núcleo do EF Core, sem repositório | 1 | aceita |
| [0009](./0009-autenticacao-e-sessao.md) | Token curto, renovação rotativa em cookie, sessão conferida por requisição | 1 | aceita |
| [0010](./0010-multi-tenancy-e-isolamento.md) | Multi-tenancy por discriminador com filtro que falha fechado | 1 | aceita |
| [0011](./0011-cadastros-operacionais.md) | Cadastros operacionais: tipos próprios, PostGIS por conversão, versão por linha, inativação | 2 | aceita |
| [0012](./0012-entrega-como-agregado-central.md) | Entrega: código humano sem lacuna, endereço copiado, timeline numerada, regras em tabela | 3 | aceita |
| [0013](./0013-rotas-e-paradas.md) | Rotas: parada como associação, regras entre rotas por índice parcial, ordem versionada | 4 | aceita |
| [0014](./0014-maquina-de-estados-e-concorrencia.md) | Máquina de estados em tabela, comandos nomeados, concorrência pela versão da linha | 5 | aceita |
| [0015](./0015-ingestao-de-localizacao.md) | Ingestão de localização: histórico e posição atual separados, gravação atômica, coleta mínima | 6 | aceita |
| [0016](./0016-geofence-de-destino.md) | Geofence de destino: avaliada na ingestão, distância no PostGIS, histerese, estado por entrega | 7 | aceita |
| [0017](./0017-tempo-real-da-operacao.md) | Tempo real: aviso só depois do commit, grupo pela sessão, conexão que cai com a sessão | 8 | aceita |

## Regras

- ADR aceito não é editado para mudar a decisão. Escreva um novo e marque o antigo
  como **substituído por ADR NNNN**.
- Correção de erro de digitação e acréscimo de evidência podem ser editados no próprio
  arquivo.
- Numeração é sequencial e nunca reaproveitada, mesmo quando um ADR é revogado.
