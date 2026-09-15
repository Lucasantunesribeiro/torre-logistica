# ADR 0019 — Motor de alertas operacionais: regras tipadas, uma chave por problema e ciclo de vida com reabertura

- **Data:** 2026-09-15
- **Status:** aceita
- **Fase:** 10

## Contexto

A Fase 10 centraliza a detecção de problemas logísticos. O ROADMAP pede:

- os tipos RiscoDeAtraso, EntregaAtrasada, MotoristaOffline, ParadoTempoExcessivo, TentativasExcedidas e
  OcorrenciaCritica, com `DesvioRelevante` só se houver regra confiável e demonstrável;
- em cada alerta: tipo, severidade, entrega, motorista, evidência, abertura, resolução e estado;
- nada de centenas de alertas idênticos a cada avaliação, com deduplicação e ciclo de vida.

O critério de aceite é que o operador identifique uma entrega problemática sem analisar manualmente
todos os GPS.

As peças já existiam:

- a previsão com situação do SLA e explicação (Fase 9, ADR 0018);
- o processador em segundo plano, com reavaliação periódica ancorada no relógio;
- o histórico de posições com índice por motorista e captura (Fase 6);
- o contador de tentativas sem sucesso na entrega (Fase 5);
- o ponto pós-commit de avisos de tempo real (Fase 8).

## Decisão

### Regras tipadas, puras, num lugar só

`RegrasDeAlerta` (domínio) recebe fatos já calculados e devolve uma **constatação**: tipo, alvo, se a
condição vale e a evidência em JSON. Nenhum `if` de alerta fica espalhado pelos casos de uso
(CLAUDE.md, seção 27).

| Tipo | Condição | Severidade | Alvo |
|---|---|:---:|---|
| `RiscoDeAtraso` | previsão ativa em Risco | Média | entrega |
| `EntregaAtrasada` | previsão ativa Atrasada | Alta | entrega |
| `MotoristaOffline` | rota em andamento, com entrega pendente, sem posição há ≥ 10 min; sem nenhuma posição, conta desde a saída | Alta | motorista na rota |
| `ParadoTempoExcessivo` | rota em andamento, com entrega pendente, motorista não offline, permanência no raio de 100 m ≥ 15 min (≥ 30 min com entrega já próxima do destino, atendendo) | Média | motorista na rota |
| `TentativasExcedidas` | tentativas sem sucesso ≥ 2 e entrega nem entregue nem cancelada | Alta | entrega |
| `OcorrenciaCritica` | tipo e severidade (Crítica) definidos; **o produtor nasce com as ocorrências, na Fase 13** | Crítica | entrega |

**Permanência no PostGIS.** Parte-se da posição atual e dela para trás. A permanência é a sequência mais
recente de posições confiáveis, depois da última fora do raio, a no máximo o raio da posição atual
(`ST_DWithin` sobre `geography`). A duração vai da mais antiga dessa sequência até a atual. GPS que
oscila dentro do raio não quebra a permanência; um afastamento real, sim.

**`DesvioRelevante` não entra.** Desvio de rota exige o traçado planejado, e o provedor simulado
(ADR 0018) não produz traçado. Uma regra sem esse traçado alertaria por qualquer atalho legítimo, o que
não é confiável nem demonstrável.

**Evidência.** A evidência leva números, instantes e limites: minutos sem posição, minutos parado, raio,
posições no local, folga, motivo da previsão, a explicação da previsão (Fase 9), tentativas e último
motivo. Nunca leva coordenada nem dado do destinatário. O limite usado fica na evidência, e mudar a
configuração não reescreve por que um alerta abriu.

### Uma chave por problema

A chave é tipo mais alvo:

- `RiscoDeAtraso:entrega:{id}` para os alertas de entrega;
- `MotoristaOffline:rota:{id}:motorista:{id}` para os de execução.

O mesmo motorista offline em outra rota é outro problema. Um **índice único parcial**
`(organizacao_id, chave) WHERE estado = 'Aberto'` garante no banco um único alerta aberto por problema,
mesmo com duas avaliações ao mesmo tempo.

### Ciclo de vida

| Estado atual | A condição vale | A condição não vale |
|---|---|---|
| Aberto | atualiza a última evidência e a última constatação; **sem evento** | resolve (`Automatica`), com evento |
| Resolvido pela regra | reabre se voltar em até 30 min da resolução (mesmo alerta, `Reaberturas`+1); depois disso, **alerta novo** | nada |
| Resolvido pelo operador, condição ainda presente | nada: a decisão humana não é desfeita no próximo ciclo | registra que a condição sumiu |
| Resolvido pelo operador, condição tinha sumido | reabre (dentro da janela) ou alerta novo | nada |

A reabertura dentro da janela representa o mesmo problema oscilando, como o GPS que volta e some. O
operador vê a contagem em vez de uma pilha de alertas. Depois da janela, é outro episódio.

Alerta aberto da rota que nenhuma regra constatou (motorista trocado, entrega retirada) é constatado
como ausente e resolve.

O ciclo de vida fica em `eventos_de_alerta`, **somente-inserção por trigger**, com quem resolveu e a
observação. O alerta atual fica em `alertas_operacionais`, com versão da linha.

### Onde o motor roda

No processador em segundo plano do ADR 0018, **logo depois do recálculo da previsão da mesma rota**,
porque risco e atraso são lidos da previsão recém-confirmada. Os gatilhos são os mesmos:

- a posição atual avançou;
- a entrega mudou na execução;
- a reavaliação periódica a cada minuto, que é quem percebe motorista offline, justamente um problema
  de **ausência** de evento.

A reavaliação também lista as rotas com alerta aberto, e é assim que o alerta de entrega se resolve depois
que a rota encerra ou a entrega é cancelada. Tudo roda no tenant da organização do pedido.

### Operador

| Rota | Quem | O quê |
|---|---|---|
| `GET /api/alertas` | Administrador, Supervisor, Operador | filtro por estado, tipo, severidade, entrega e motorista; do mais severo ao menos severo, o mais antigo primeiro; com descrição, evidência, código da entrega, nome do motorista e código da rota |
| `GET /api/alertas/{id}` | idem | alerta e ciclo de vida |
| `POST /api/alertas/{id}/resolucao` | Administrador, Supervisor, Operador | resolve com observação opcional (até 280); repetir não gera evento |

Não há criação nem alteração de alerta pela API: o motor abre e resolve pelas regras.

A **descrição** é montada da evidência, por exemplo:

- "Motorista sem enviar posição há 11 min (limite 10 min), com 1 entrega(s) pendente(s).";
- "Entrega em risco de atraso. Risco porque a chegada prevista fica 3 min depois do fim da janela
  prometida. …".

É o que dispensa abrir o histórico de GPS.

### Tempo real

`AlertCreated` sai na abertura e na reabertura, com alerta, tipo, severidade, entrega, motorista, rota,
se é reabertura, sequência e instante. `AlertResolved` sai na resolução, com a forma. Esse segundo evento
foi acrescentado aos nomes do ADR 0017: sem ele, o console não saberia tirar o alerta da tela sem
recarregar. Nenhum dos dois leva evidência; o detalhe vem da API.

### Limites configuráveis — `Torre:Alertas`

| Chave | Padrão |
|---|---|
| `TempoSemPosicaoParaOffline` | 10 min |
| `TempoParadoParaAlerta` | 15 min |
| `TempoParadoAtendendoParadaParaAlerta` | 30 min |
| `RaioDeImobilidadeEmMetros` | 100 |
| `LimiteDeTentativas` | 2 |
| `JanelaDeReabertura` | 30 min |

A subida valida os valores: atendimento não pode ser menor que o limite geral, raio de 10 a 2.000 m e
tentativas de 1 a 20.

### Concorrência

Numa instância, as avaliações são seriais. Entre instâncias, ou contra a resolução do operador, decidem:

- a versão da linha;
- o índice único de alerta aberto;
- o índice único da sequência de eventos.

A avaliação perdedora é descartada, e a próxima parte do estado confirmado. A resolução do operador que
perder a corrida recebe `409 conflito_de_versao`.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Alerta novo a cada avaliação em que a condição vale | É exatamente o spam que o ROADMAP proíbe (R11) |
| Deduplicar só na aplicação | Duas avaliações simultâneas passam juntas pela checagem; o índice parcial decide |
| Reabrir sempre o mesmo alerta, para sempre | Um problema de ontem e um de hoje viram uma linha só, e o tempo aberto perde sentido |
| Nunca reabrir | GPS oscilando gera um alerta por oscilação |
| Reabrir alerta resolvido pelo operador enquanto a condição persiste | Desfaria a decisão humana a cada minuto |
| Regras em worker separado | O aviso sai do processo do hub (ADR 0017), e a previsão já é recalculada ali |
| `DesvioRelevante` por distância à linha reta entre paradas | Alerta atalho legítimo; sem traçado planejado não é confiável |
| Evidência com a coordenada | O alerta é visto por toda a operação; a localização exata fica no histórico, com autorização própria |
| `OcorrenciaCritica` sem ocorrência, simulada | Seria alerta sem fato real; nasce com as ocorrências |

## Consequências

- As ocorrências (Fase 13) só precisam produzir a constatação de `OcorrenciaCritica`: tipo, severidade,
  chave, ciclo de vida, API e aviso já existem.
- O mapa do console (Fase 18) consome `AlertCreated`/`AlertResolved` e a lista priorizada.
- Os indicadores (Fase 19) têm abertura, resolução, forma e reaberturas por tipo.
- Com mais de uma instância, cada uma avalia as mesmas rotas: o resultado continua certo, pelos índices e
  pela versão, mas o trabalho se repete (Fase 25, com o backplane).

## Como isto é verificado

- `CicloDeVidaDoAlertaTestes`:
  - abertura só com condição;
  - a mesma condição 47 vezes sem evento novo;
  - resolução automática;
  - reabertura dentro da janela e alerta novo depois;
  - resolução do operador que não reabre enquanto a condição persiste e reabre quando ela some e volta;
  - observação longa e chave errada recusadas;
  - catálogo de severidade e alvo.
- `RegrasDeAlertaTestes`:
  - offline nas bordas de 9 e 10 min, sem posição desde a saída, sem pendência ou fora de andamento;
  - parado com limite geral e de atendimento, e offline suprimindo o parado;
  - tentativas até a entrega ser cancelada;
  - risco e atraso só com previsão ativa.
- `DescricaoDoAlertaTestes`: texto de cada tipo e validação dos limites.
- `AlertasTestes` (API, processador em segundo plano, PostgreSQL + PostGIS reais, relógio controlado):
  - **critério de aceite:** numa rota com duas entregas, o operador lista os abertos e encontra só a
    problemática, com código, descrição exata e evidência sem coordenada, e recebe `AlertCreated`;
  - a mesma condição em três avaliações: um alerta e um evento;
  - motorista offline abre, resolve quando a posição volta, reabre dentro da janela e vira alerta novo
    depois dela;
  - resolução pelo operador, que não reabre com a condição presente, e repetição sem evento;
  - tentativas excedidas abre e resolve quando a entrega é cancelada, pela reavaliação;
  - parado abre pela permanência calculada no PostGIS (três posições, 16 min) e resolve quando o
    motorista anda;
  - alerta de outra organização invisível, 404 idêntico a inexistente e resolução recusada;
  - ciclo de vida recusa `UPDATE`, `DELETE` e `TRUNCATE`.
- `AutorizacaoTestes`: as três rotas no mapa e na matriz por perfil.
