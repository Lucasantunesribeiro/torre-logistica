# ADR 0028 — Indicadores operacionais: agregação direta, definição junto do número e vazio que não vira zero

**Status:** aceito — Fase 19
**Decisores:** time técnico
**Relacionados:** [0012](./0012-entrega-como-agregado-central.md), [0013](./0013-rotas-e-paradas.md), [0018](./0018-previsao-de-chegada-e-sla.md), [0022](./0022-ocorrencias-e-tentativas.md), [0027](./0027-console-operacional-e-mapa.md)

## Contexto

O critério de aceite da fase é uma frase sobre gente, não sobre tecnologia: **o supervisor consegue
responder onde a operação está falhando**. Isso põe três perguntas de engenharia na mesa.

A primeira é de desempenho. Indicador é agregação sobre `entregas`, `paradas` e `ocorrencias` — tabelas que
crescem sem limite — e uma consulta ingênua a cada abertura de tela derruba o banco.

A segunda é de significado. Um número sozinho não informa: "pontualidade de 60%" depende inteiramente de
quem entra no denominador. Se duas pessoas discordam do que o número conta, elas não estão discutindo a
operação, estão discutindo o relatório.

A terceira é de honestidade. Um período sem entrega nenhuma e um período em que tudo atrasou produzem, numa
implementação descuidada, o mesmo `0%`.

## Decisão

### Agregação direta no PostgreSQL, com índices dedicados — sem projeção materializada

O painel é calculado no banco, na hora, por `GROUP BY`. Não há tabela de resumo, job de consolidação nem
cache.

Materializar seria otimizar no escuro: não existe medição que mostre a consulta lenta, e uma projeção
introduz o problema mais caro desta classe de sistema — dois lugares que contam a mesma coisa e divergem.
O que a fase faz, em vez disso, é **remover o motivo de materializar**: índices que cobrem exatamente o
recorte das consultas.

| Índice | Consulta que ele atende |
|---|---|
| `ix_entregas_organizacao_entregue_em` (parcial, `entregue_em IS NOT NULL`) | tudo que é conta de entrega concluída no período |
| `ix_entregas_organizacao_cancelada_em` (parcial, `cancelada_em IS NOT NULL`) | a contagem de canceladas |
| `ix_paradas_entrega_adicionada_em` | a rota que carregava cada entrega concluída |
| `ix_ocorrencias_organizacao_id_ocorrida_em` (já existia, da Fase 13) | ocorrências por motivo |

Os dois primeiros são parciais de propósito: num dia de operação a maior parte de `entregas` é fila em
aberto, e índice parcial não paga por linha que nunca será consultada.

O caminho para materializar, se a Fase 22 medir necessidade, fica aberto: a consulta está isolada em
`ConsultaDeIndicadores`, e uma projeção entraria atrás dela sem tocar em API nem em tela.

O período é **limitado a 186 dias** e validado antes de qualquer consulta. Sem teto, um parâmetro de query
string vira varredura da operação inteira — e quem descobre isso primeiro não costuma ser o supervisor.

### O recorte é pelo instante da conclusão, não pela criação

"Como foi a semana" é uma pergunta sobre as entregas que **aconteceram** naquela semana. Uma entrega criada
em janeiro e concluída em março é da operação de março.

### Cancelada não entra no denominador

Cancelamento não é falha de pontualidade — é decisão do cliente ou do planejamento. Contá-la como atraso
puniria a operação por algo que ela não fez, e um supervisor que age sobre esse número age errado. A
contagem de canceladas aparece **à parte**, porque também é informação.

### Sem base, o valor é vazio — nunca zero

Cada indicador carrega `valor`, `base` e `definicao`. Quando a base é zero, o valor é nulo, e a tela escreve
*sem dados no período*.

`0%` e "não houve entrega" são fatos opostos. Mostrar o mesmo símbolo para os dois faz o supervisor agir
sobre um problema que não existe — que é exatamente o contrário do que a fase se propõe.

A `base` fica visível sempre, e não só quando é zero: *atraso médio de 75 min* significa uma coisa sobre 2
entregas e outra sobre 200.

### A definição viaja com o número

`definicao` é campo da resposta da API, não texto do frontend. O cálculo e a explicação do cálculo saem do
mesmo lugar: se a regra mudar e a frase não, a divergência aparece no teste, que compara as duas.

Na tela, a definição fica **escrita embaixo do número** — não em tooltip, não em página de ajuda. É a
leitura literal da regra do ROADMAP: *sempre mostrar definição do indicador*.

### Cada recorte responde uma pergunta escrita na tela

A outra regra do ROADMAP — *não criar gráfico sem pergunta operacional clara* — virou estrutura: cada cartão
e cada tabela têm a pergunta impressa acima. "Para qual cliente a promessa está sendo quebrada?" acima do
SLA por cliente; "Qual rota concentra o atraso?" acima das entregas por rota.

O único elemento gráfico é uma barra proporcional atrás da quantidade, para achar o maior de relance. O
número exato está escrito ao lado. Não há série temporal, pizza nem medidor.

### A rota de uma entrega é a que a carregava quando ela foi concluída

Uma entrega pode passar por mais de uma rota: tentativa frustrada, reagendamento, e ela sai de novo noutro
dia. Somar a entrega nas duas rotas inflaria o recorte e apontaria a rota errada.

A tentação era usar a parada ativa — a base garante, por índice único parcial, que cada entrega está em no
máximo uma. **Não funciona:** concluir a rota desativa todas as paradas dela, inclusive as das entregas que
deram certo, e o recorte perderia justamente quem terminou o dia. A regra correta é histórica: a parada
adicionada antes da conclusão e ainda não removida naquele instante.

## Consequências

O painel é sempre consistente com o banco, porque não existe cópia. Em compensação, cada abertura de tela
paga o custo da agregação — aceitável no volume de referência do projeto, e medido na Fase 22.

O teto de 186 dias significa que comparar dois semestres exige duas chamadas. É uma limitação consciente:
o custo de tirá-la é a varredura irrestrita.

A `definicao` em português no corpo da API é dívida assumida: no dia em que houver segundo idioma, ela vira
chave de tradução. Hoje, chave sem tradutor seria complexidade sem uso.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Tabela de resumo atualizada por job | otimização sem medição, e dois lugares contando a mesma coisa |
| Vista materializada do PostgreSQL | mesmo problema, mais o atraso de atualização — número velho sem avisar que é velho |
| Cache da resposta por alguns minutos | o supervisor abre o painel justamente depois de agir; resposta velha é pior que resposta lenta |
| Definição só no frontend | a regra e a frase divergiriam na primeira mudança de cálculo, sem nada acusando |
| Definição em tooltip | o ROADMAP pede mostrar, e quem mais precisa da definição é quem não sabe que precisa |
| Mostrar `0%` quando não há base | faz o supervisor agir sobre problema inexistente |
| Contar cancelada como atraso | pune a operação por decisão que não foi dela |
| Recortar pela data de criação | responde "quantas entramos", não "como fomos" |
| Série temporal no painel | gráfico sem ação associada; o ROADMAP proíbe explicitamente |
| Usar a parada ativa para achar a rota | concluir a rota desativa as paradas, e as entregas concluídas sumiriam do recorte |

## Como a decisão é verificada

| Afirmação | Prova |
|---|---|
| O painel responde onde a operação falha | `PainelRespondeOndeAOperacaoEstaFalhando`: operação real com 2 atrasos, 1 tentativa frustrada reagendada e 1 cancelamento; confere os cinco indicadores e os quatro recortes |
| Cada número traz a própria definição | o mesmo teste compara a `definicao` de cada indicador com o texto exato |
| O recorte é pelo instante da conclusão | `PeriodoRecortaPeloInstanteDaConclusao`: início inclusivo, fim exclusivo |
| Fuso não muda o recorte | `MesmoInstanteEmFusosDiferentesDaOMesmoResultado` (integração) e `PeriodoDoIndicadorTestes` (unidade) |
| Cancelada fora do denominador | `CanceladaNaoEntraNaContaDePontualidade`: com ela na conta, a pontualidade cairia de 100% para 50% |
| Sem base, valor vazio | `SemBaseOValorEVazioENaoZero`: todos os indicadores nulos com base 0, e entrega sem chegada registrada fora do tempo por parada |
| Nenhum indicador atravessa organização | `IndicadoresNaoVazamEntreOrganizacoes`: duas operações simultâneas, números independentes |
| Período impossível ou largo demais é recusado | `PeriodoInvertidoOuLongoDemaisERecusado`: 422 com código próprio, antes de tocar no banco |
| A tela mostra número, base e definição | testes do console: cartão com definição visível, "sem dados no período" no indicador sem base |
| Cada recorte tem pergunta | teste do console que localiza as quatro perguntas na tela |
