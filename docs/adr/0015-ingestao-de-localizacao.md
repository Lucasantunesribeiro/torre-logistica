# ADR 0015 — Ingestão de localização: histórico e posição atual separados, gravação atômica, política explícita e coleta mínima

- **Data:** 2026-09-14
- **Status:** aceita
- **Fase:** 6

## Contexto

A Fase 6 cria o caminho da telemetria GPS do motorista. As regras vêm do CLAUDE.md (seções 17 a 22)
e do ROADMAP:

- `capturedAt` e `receivedAt` não se fundem;
- histórico (`posicoes`) e posição atual (`posicoes_atuais`) são tabelas diferentes;
- a mesma posição reenviada produz um único efeito lógico (organização + motorista + evento);
- **evento antigo nunca regressa a posição atual** — regra absoluta, com teste permanente;
- coordenada inválida é recusada; precisão ruim não move o estado confiável;
- o motorista só envia para o próprio contexto;
- lote para recuperação offline, com resultado parcial.

## Decisão

### Contrato

`POST /api/motorista/posicoes` recebe `{ posicoes: [...] }`, de 1 a 500 itens. Cada item:

| Campo | Origem no ROADMAP |
|---|---|
| `eventoDeLocalizacaoId` | `LocationEventId`, gerado pelo aplicativo antes do envio |
| `latitude`, `longitude` | — |
| `precisaoEmMetros` | precisão |
| `capturadaEm` | `CapturedAt`, relógio do aparelho |
| `sequencia` | `Sequence`, contador do aparelho |
| `velocidadeEmMetrosPorSegundo`, `direcaoEmGraus` | opcionais |

O servidor grava `recebidaEm` (`ReceivedAt`) pelo próprio relógio. **Motorista e organização não
vêm no corpo**: saem da sessão. Um `motoristaId` no item é campo desconhecido e responde 400.

A resposta é 200 com `recebidas`, `aceitas`, `duplicadas`, `rejeitadas` e o resultado de cada item,
na ordem do envio — `Aceita` (com qualidade, `foraDeOrdem` e `atualizouPosicaoAtual`), `Duplicada`
ou `Rejeitada` (com motivo). Item ruim não derruba o lote.

### Política de aceitação

| Situação | Destino |
|---|---|
| Campo obrigatório ausente, coordenada fora da faixa ou `(0,0)`, precisão ≤ 0 ou > 2 km, velocidade negativa ou > 70 m/s, direção fora de [0, 360) | recusada |
| Capturada mais de 2 min no futuro (relógio do aparelho adiantado) | recusada `capturada_no_futuro` |
| Capturada há mais de 7 dias (horizonte de recuperação offline) | recusada `capturada_antiga_demais` |
| Fora da execução de uma rota do motorista (15 min antes do início até 5 min depois da conclusão) | recusada `fora_de_rota` |
| Precisão entre 100 m e 2 km | histórico, qualidade `Imprecisa`, sem mover a posição atual |
| Confiável, mas mais antiga que a posição atual | histórico, `foraDeOrdem`, sem regredir a posição atual |
| Mesmo evento do mesmo motorista | `Duplicada`, sem novo registro |

**Coleta mínima (CLAUDE.md, seção 55).** A janela da rota é o controle de servidor: fora da execução
de uma rota iniciada pelo próprio motorista, a localização dele não é necessária e não é guardada —
mesmo que o aplicativo a envie. A posição aceita guarda a rota a que pertence.

Os limites são constantes do domínio (`PoliticaDeLocalizacao`), testadas nas bordas. Torná-los
configuráveis por organização é decisão de produto que ainda não existe.

### Ordem: captura primeiro, sequência desempata

A posição atual avança quando `(capturada_em, sequencia)` da nova é maior que a vigente. A captura
manda porque a sequência recomeça quando o aplicativo é reinstalado; a sequência desempata leituras
no mesmo instante. Para o cenário obrigatório (101, 103, 102 com capturas em ordem) as duas coincidem.

### Gravação atômica no banco

Cada posição é **um comando SQL** com duas CTEs:

1. `INSERT INTO posicoes ... ON CONFLICT (organizacao_id, motorista_id, evento_de_localizacao_id) DO NOTHING RETURNING 1`
2. se inseriu e é confiável: `INSERT INTO posicoes_atuais ... ON CONFLICT (motorista_id) DO UPDATE ... WHERE (atual.capturada_em, atual.sequencia) < (nova.capturada_em, nova.sequencia)`

A comparação que protege a regra absoluta fica no `WHERE` do próprio `UPSERT`, dentro do banco:
não existe janela entre ler a posição atual e decidir gravar. Duplicata é decidida pelo índice
único, não por consulta prévia.

O histórico guarda a **qualidade** (confiável ou imprecisa). "Fora de ordem" é relativo à chegada e
só é conhecido no mesmo comando; vai na resposta e na métrica, não no registro.

### Lote numa transação, lotes do mesmo motorista em fila

O lote grava numa transação. Se ela falhar, o aplicativo reenvia o lote inteiro e o que já tinha
sido confirmado antes volta como duplicata. No início da transação, um *advisory lock* por motorista
enfileira lotes simultâneos do mesmo aparelho: sem ele, dois lotes com posições em comum travariam um
ao outro (cada um esperando a posição inserida pelo outro, com a linha da posição atual já presa).
Telemetria de um aparelho é sequencial por natureza; motoristas diferentes seguem em paralelo.

### Leitura pelo console

| Rota | Quem | Limites |
|---|---|---|
| `GET /api/motoristas/{id}/posicao-atual` | Administrador, Supervisor, Operador | — |
| `GET /api/motoristas/{id}/posicoes?de=&ate=` | Administrador, Supervisor | período ≤ 24 h, até 2.000 posições, com indicador de truncado |

Histórico de localização é dado pessoal volumoso: fica com a gestão, sempre por período.

### Limite de requisição por motorista

`POST /api/motorista/posicoes` tem limite próprio, **por conta autenticada** (60 envios por minuto,
configurável). Por endereço seria errado: aparelhos de operadora móvel saem pelo mesmo IP público
(CGNAT), e um motorista abusivo bloquearia os vizinhos. Para isso o limitador passou a rodar depois
da autorização; os limites de login e renovação continuam por endereço e continuam antes do endpoint.

### Observabilidade

Medidor `TorreLogistica.Rastreamento`, nomes do CLAUDE.md (seção 51) no padrão OpenTelemetry:
`tracking.positions.received`, `.duplicate`, `.out_of_order`, `.inaccurate`, `.rejected` (dimensão
`motivo`, vocabulário fechado) e o histograma `tracking.ingestion.lag` em segundos. Nenhuma dimensão
por motorista ou organização. Log: **uma linha por lote**, com contagens e sem coordenada.

### Histórico e retenção

`posicoes` recusa `UPDATE` por trigger: o que foi capturado não é reescrito. `DELETE` continua
permitido de propósito — a limpeza por retenção (CLAUDE.md, seção 22) vai precisar dele.

**Particionamento temporal** não está ativo. Quando entrar, a chave única de idempotência não pode
incluir a partição sem mudar o significado da deduplicação; o caminho previsto é mover a
deduplicação para uma tabela estreita de eventos recebidos, com retenção própria. Nada nesta fase
impede isso.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Ler a posição atual e decidir na aplicação | Dois lotes simultâneos leem a mesma posição e regridem um ao outro |
| `ORDER BY capturada_em DESC LIMIT 1` no histórico | Proibido pelo CLAUDE.md, seção 18: não escala com milhões de linhas |
| Rejeitar o lote inteiro no primeiro item inválido | O aplicativo perderia as posições boas e reenviaria as ruins para sempre |
| Ordem só por sequência | Reinstalação do aplicativo recomeça a sequência |
| Ordem só por captura | Leituras no mesmo instante ficariam sem ordem estável |
| Aceitar posição fora de rota | Coleta de localização sem finalidade |
| Fila e worker para ingestão | Persistência síncrona com projeção local basta na escala de referência (500 motoristas, 33 posições/s); CLAUDE.md, seção 42 |
| Guardar "fora de ordem" no histórico | Só é conhecido no mesmo comando que grava; exigiria `UPDATE` num histórico que não aceita |
| Distância entre posições para detectar salto impossível | Cálculo geodésico é do PostGIS e da Fase 7 |

## Consequências

- A geofence (Fase 7) lê `posicoes_atuais`, que já tem índice GiST, e transições são avaliadas sobre
  posições confiáveis.
- O mapa da operação (Fase 8) abre por `posicoes_atuais`, sem varrer o histórico.
- A PWA (Fase 11) envia lotes com `eventoDeLocalizacaoId` gerado localmente e lê o resultado por item
  para limpar a fila.
- Detecção de salto impossível e ajuste dos limites por dado real ficam para as Fases 7 e 22.

## Como isto é verificado

- `PosicaoDoMotoristaTestes` — 14 recusas com motivo, qualidade pela precisão, bordas inclusivas das
  tolerâncias, janela de rota concluída, rotas sobrepostas, truncamento ao microssegundo.
- `PosicoesTestes` (PostgreSQL + PostGIS real) — 101/103/102 em envios separados e num lote só
  (histórico 101, 102, 103; atual 103), duplicata e reenvio de lote, lote parcial com doze itens,
  posição antiga e sequência reiniciada, relógio do aparelho, contexto do motorista (corpo, rota não
  iniciada, outro motorista, conta sem cadastro, canais, outra organização), seis lotes simultâneos
  com sobreposição, telemetria realista com recuperação offline e métricas, limites do histórico e
  tamanho de lote.
- `LimiteDeTelemetriaTestes` — 429 no terceiro envio, outro motorista do mesmo endereço segue.
- `AutorizacaoTestes` — novas linhas e mapa de rotas contra o roteamento real.
