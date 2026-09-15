# ADR 0018 — Previsão de chegada e SLA: cálculo explicável por rota, fora da requisição, com contingência e histórico fotografado

- **Data:** 2026-09-15
- **Status:** aceita
- **Fase:** 9

## Contexto

A Fase 9 cria a previsão de chegada (ETA) e o controle da janela prometida. O ROADMAP pede:

- uma fronteira `IRoutingProvider`;
- um ETA determinístico que combine duração restante, paradas anteriores, tempo médio por parada e
  estado operacional;
- o histórico de mudanças relevantes, sem sobrescrever;
- a classificação Normal, Atenção, Risco e Atrasada, com limiares configuráveis e documentados.

O critério de aceite é que a aplicação explique por que uma entrega passou de Normal para Risco.

As peças já existiam:

- a entrega guarda a janela prometida (`JanelaDeEntrega`, início e fim em UTC, da Fase 3), ou seja, o
  `PromisedFrom`/`PromisedUntil` do ROADMAP;
- a rota guarda a ordem das paradas;
- a posição atual do motorista só avança (Fase 6);
- a geofence registra a chegada perto do destino (Fase 7);
- o contexto de persistência despacha efeitos depois do commit (Fase 8).

## Decisão

### Composição da chegada prevista

Para cada parada pendente de uma rota em andamento, na ordem da rota:

```text
chegada prevista = agora
                 + deslocamento acumulado desde a posição atual até a parada
                 + tempo de atendimento das paradas pendentes antes dela
```

| Estado operacional da parada | Chegada prevista | Efeito nas seguintes |
|---|---|---|
| Em rota, com coordenada e com posição do motorista | pela conta acima | soma o tempo por parada |
| Próxima do destino | a chegada registrada | soma só o atendimento que falta (tempo por parada menos o já decorrido) |
| Em rota, sem posição do motorista desde a saída | nenhuma (`SemPosicaoDoMotorista`) | soma o tempo por parada |
| Em rota, sem coordenada de destino | nenhuma (`DestinoSemCoordenada`) | soma o tempo por parada; o trajeto segue pelas paradas com coordenada |

Posição capturada antes da saída (menos a tolerância da política de localização) não serve de origem:
a posição de ontem, do fim de outra rota, não prevê a de hoje. Os instantes são truncados ao segundo.

Nada de média móvel, aprendizado ou fator escondido. Cada parcela fica gravada e aparece na explicação.

### Classificação contra a janela

A folga é o fim da janela menos a chegada prevista.

| Condição, na ordem | Situação | Motivo |
|---|---|---|
| agora > fim da janela | Atrasada | `JanelaEncerrada` |
| sem chegada prevista e fim − agora < limiar de atenção | Atenção | `SemPrevisaoComJanelaProximaDoFim` |
| sem chegada prevista | Normal | `SemPrevisao` |
| folga < 0 | Risco | `ChegadaPrevistaDepoisDaJanela` |
| folga < limiar de risco | Risco | `FolgaAbaixoDoLimiarDeRisco` |
| folga < limiar de atenção | Atenção | `FolgaAbaixoDoLimiarDeAtencao` |
| caso contrário | Normal | `FolgaSuficiente` |

"Abaixo" é estrito: folga igual ao limiar fica na situação mais branda. Chegar antes do início da
janela não é classificado nesta fase; isso é decisão de produto e continua registrada na janela.

### Limiares e parâmetros configuráveis — `Torre:Previsao`

| Chave | Padrão | Para quê |
|---|---|---|
| `Provedor` | `simulado` | nome do provedor de rotas; `Nenhum` desliga |
| `TempoMedioPorParada` | 5 min | atendimento em cada parada |
| `FolgaParaAtencao` | 20 min | abaixo disto, Atenção |
| `FolgaParaRisco` | 5 min | abaixo disto, Risco |
| `MudancaRelevanteDaChegadaPrevista` | 2 min | quanto a chegada precisa andar, sem mudar a situação, para entrar no histórico |
| `TempoLimiteDoProvedor` | 3 s | espera máxima pelo provedor |
| `VelocidadeDeContingenciaEmMetrosPorSegundo` | 6 (≈ 21,6 km/h) | contingência em linha reta |
| `FatorDeSinuosidadeDeContingencia` | 1,4 | caminho real / linha reta, na contingência |
| `IntervaloDeReavaliacao` | 1 min | reavaliação periódica das rotas em andamento |
| `IntervaloMinimoEntreRecalculosPorPosicao` | 30 s | recálculos da mesma rota disparados por GPS |
| `ProvedorSimulado:VelocidadeMediaEmMetrosPorSegundo` | 8,5 (≈ 30,6 km/h) | velocidade urbana do provedor simulado |
| `ProvedorSimulado:FatorDeSinuosidade` | 1,3 | caminho por ruas / linha reta |

Configuração incoerente (risco ≥ atenção, velocidade zero, tempo limite zero) derruba a subida. Os
limiares, o tempo por parada e a fonte ficam gravados em cada previsão e em cada registro: mudar um
número amanhã não reescreve por que a entrega entrou em risco ontem. Os padrões são ponto de partida
para operação urbana e devem ser recalibrados com dados reais (Fase 19).

### `IProvedorDeRotas` — a fronteira `IRoutingProvider`

Na `Application`, responde **só** pelo deslocamento: dado N pontos, devolve N − 1 trechos
(duração e distância). Paradas, atendimento, janela e situação são do serviço interno. Trocar de
provedor não muda a composição nem a explicação.

A implementação desta fase é o **provedor simulado**. Ele usa a distância geodésica do PostGIS
(`ST_Distance` sobre `geography`) multiplicada pela sinuosidade e dividida pela velocidade média. É a
implementação controlada que o ROADMAP permite enquanto um provedor real exigiria conta e custo. Ele não
sabe de ruas nem de trânsito, e a previsão registra o nome `simulado`. Um provedor real (OSRM, Mapbox,
Google) entra implementando a interface, sem mudar mais nada; escolher fornecedor é decisão com custo e
fica para o usuário.

### Falha, demora ou ausência do provedor

| Situação | Detecção | Resultado |
|---|---|---|
| nenhum provedor com o nome configurado (inclusive `Nenhum`) | na resolução | contingência `ProvedorAusente` |
| provedor passa do tempo limite | `CancelAfter` + `WaitAsync`, que vale mesmo para provedor que ignora o cancelamento | contingência `TempoLimite` |
| provedor lança exceção | `catch` com log de aviso | contingência `FalhaDoProvedor` |
| provedor devolve quantidade errada de trechos | conferência | contingência `RespostaInvalida` |

A contingência calcula em linha reta pelo PostGIS, com velocidade e sinuosidade conservadoras. A
previsão continua existindo, com fonte `Contingencia` e o motivo gravado. A explicação diz "estimado
em linha reta porque o provedor de rotas não respondeu no tempo limite". Cada contingência é contada na
métrica `eta.provider.fallbacks`, por motivo. Nenhuma dessas falhas chega à requisição do motorista.

### Fora da requisição: gatilhos pós-commit, fila em memória e reavaliação periódica

O provedor pode levar segundos, e a ingestão de GPS e os comandos do motorista não podem esperar por ele.

1. **Gatilhos.** O contexto de persistência, no mesmo ponto pós-commit do ADR 0017, pede recálculo
   quando a posição atual de um motorista avança ou quando uma entrega sai para rota, chega, tem
   proximidade detectada, é concluída, tem tentativa sem sucesso ou é reatribuída. Tentativa desfeita
   não pede nada.
2. **Fila em memória** (`Channel` limitado, pedidos repetidos colapsados enquanto esperam). Um
   consumidor por instância recalcula em série, **no tenant da organização do pedido**, com um contexto
   de persistência criado para esse tenant. Pedidos por posição da mesma rota respeitam 30 s entre si.
   Pedidos por mudança de entrega e pela reavaliação não esperam.
3. **Reavaliação periódica.** Um temporizador ancorado no `TimeProvider` lista as rotas em andamento,
   e as que ainda têm previsão ativa, e as enfileira. Ela cobre duas coisas: **o tempo passa sem
   evento** (a chegada prevista escorrega e a janela se aproxima, e é assim que uma entrega vira
   Atrasada) e **pedido perdido** (a instância caiu com a fila cheia). É a única leitura sem tenant, e só
   de identificadores (`IgnoreQueryFilters()` explícito).

O recálculo é **da rota inteira**, porque a chegada de uma parada depende das anteriores. Entregas que
saíram da execução têm a previsão encerrada no mesmo cálculo.

Sem outbox: a previsão se recalcula a partir do estado confirmado. Perder um pedido atrasa a previsão
até a próxima reavaliação, sem corromper nada.

### Estado atual e histórico fotografado

- `previsoes_da_entrega` — uma linha por entrega, sobrescrita a cada cálculo: situação, motivo, chegada
  prevista, folga, composição, fonte, provedor, motivo da contingência, janela, limiares, captura da
  posição usada, instante do cálculo e versão da linha.
- `registros_de_previsao` — **somente-inserção por trigger** (`insufficient_privilege` em `UPDATE`,
  `DELETE` e `TRUNCATE`), com índice único `(entrega_id, sequencia)`. Cada registro é uma fotografia
  completa: situação e chegada **anteriores** e novas, regra, composição, fonte, janela e limiares.

Um cálculo entra no histórico quando:

| Tipo | Quando |
|---|---|
| `Inicial` | primeira previsão da entrega, ou primeira de uma nova execução |
| `SituacaoAlterada` | a situação mudou (prevalece sobre mudança de chegada) |
| `ChegadaPrevistaAlterada` | a chegada andou ≥ mudança relevante, apareceu, sumiu ou trocou de fonte |
| `Encerrada` | a entrega saiu da execução, com o status em que saiu |

Sem esse filtro, cada posição GPS escreveria uma linha de histórico. Cálculo mais antigo que o vigente
não reescreve o presente.

### Explicação

`GET /api/entregas/{id}/previsao` (Administrador, Supervisor, Operador) devolve a previsão atual e até
200 registros mais recentes. Cada um vem com uma **explicação montada a partir da fotografia**, por
exemplo:

> Passou de Normal para Risco porque a folga até o fim da janela prometida caiu para 3 min, abaixo do
> limiar de risco de 5 min. A chegada prevista soma 17 min de deslocamento (10,0 km, pelo provedor de
> rotas simulado) e nenhuma parada antes desta. A posição do motorista usada foi capturada 1 h 40 min
> antes do cálculo.

A explicação traz só durações e distâncias, nenhum horário de relógio: fuso é assunto de apresentação.
Os instantes vão em campos próprios, e o console os mostra no fuso de quem olha. Não traz coordenada
nem dado do destinatário.

### Tempo real

A mudança de situação vira `DeliveryRiskChanged` pelo ponto pós-commit, com entrega, situação anterior
e nova, motivo, chegada prevista, folga, sequência no histórico e instante. A primeira previsão só avisa
se já nascer fora de Normal. Mudança de chegada sem mudança de situação não avisa; o console consulta a
API.

### Concorrência

Numa instância, os recálculos são seriais. Entre instâncias, a versão da linha da previsão e o índice
único do histórico decidem. O cálculo perdedor é descartado com log informativo, e o próximo gatilho
recalcula. As tabelas de previsão são separadas das entregas: recalcular nunca disputa a versão da
linha da entrega com o motorista.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Calcular na requisição de GPS ou do comando | O provedor pode levar segundos; a ingestão e o aplicativo do motorista ficariam reféns dele |
| Calcular no host de workers | O aviso de tempo real sai do processo que hospeda o hub (ADR 0017, sem backplane) |
| Outbox para os pedidos de recálculo | A previsão se recalcula do estado confirmado; a reavaliação periódica cobre o pedido perdido |
| Guardar só a previsão atual | CLAUDE.md, seção 25: sem histórico não há como explicar o risco nem medir a qualidade da previsão |
| Registrar todo cálculo | Uma linha por posição GPS, sem informação nova |
| Histórico só com os valores, sem janela e limiares | Mudar a configuração reescreveria a explicação do passado |
| Distância em linha reta calculada em C# | CLAUDE.md, seção 16: cálculo geodésico é do PostGIS |
| Contratar provedor real agora | Custo e conta externa exigem autorização; o contrato fica pronto para ele |
| Limiares por organização ou cliente | Decisão de produto que ainda não existe; limiares já gravados em cada registro |
| IA ou aprendizado para o ETA | CLAUDE.md, seção 24: determinístico e explicável na v1 |

## Consequências

- Os alertas (Fase 10) têm evidência pronta: situação, regra, folga e composição de cada mudança.
- Os indicadores (Fase 19) podem medir a qualidade da previsão comparando cada registro com a chegada
  real, e recalibrar os padrões.
- O mapa do console (Fase 18) consome `DeliveryRiskChanged` e a consulta de previsão.
- Mais de uma instância da API recalcula as mesmas rotas na reavaliação. O resultado continua certo,
  pela versão da linha e pelo índice único, mas com trabalho repetido. Uma trava por rota ou um líder de
  reavaliação entram junto com o backplane (Fase 25).
- A reavaliação lê no máximo 5.000 rotas por ciclo. Medir na Fase 22.

## Como isto é verificado

- `RegrasDeSlaTestes`: cada limiar dos dois lados da borda, chegada depois da janela, janela
  encerrada, sem previsão longe e perto do fim, limiares incoerentes.
- `CalculadoraDeChegadaTestes`: soma de deslocamento e paradas anteriores, parada já no destino com
  atendimento restante e esgotado, sem posição, parada sem coordenada no meio, trajeto incompleto.
- `PrevisaoDaEntregaTestes`: registro inicial, mudança pequena sem histórico, mudança relevante com
  valor anterior, mudança de situação, cálculo antigo ignorado, encerramento único e reinício,
  fotografia imutável, contingência exige motivo.
- `ExplicacaoDaPrevisaoTestes` e `OpcoesDePrevisaoTestes`: texto exato de Normal para Risco, cada motivo
  de contingência, Atrasada e Encerrada, configuração incoerente.
- `PrevisaoTestes` (API, processador em segundo plano, PostgreSQL + PostGIS reais, relógio controlado):
  - critério de aceite: posição a 10 km vira Normal com chegada exata ao segundo; 100 minutos depois,
    **sem evento**, a reavaliação periódica passa a Risco com a explicação exata; o histórico anterior
    fica idêntico; no fim da janela, Atrasada;
  - duas paradas: deslocamento, parada anterior e tempo por parada; chegada registrada; conclusão
    encerra a primeira e adianta a segunda;
  - `DeliveryRiskChanged` no console conectado, sem aviso para a primeira previsão Normal;
  - previsão de outra organização responde 404 igual a inexistente;
  - histórico recusa `UPDATE`, `DELETE` e `TRUNCATE`.
- `ProvedorDeRotasIndisponivelTestes`: provedor que falha cai na contingência; provedor que trava
  não segura a ingestão de GPS e cai na contingência no tempo limite.
- `SemProvedorDeRotasTestes`: `Provedor = Nenhum` usa a contingência e diz por quê.
- `AutorizacaoTestes`: a rota nova no mapa, com a política de leitura da operação.
