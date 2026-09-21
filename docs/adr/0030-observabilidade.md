# ADR 0030 — Observabilidade: OTLP sem fornecedor, rastro que atravessa a fila e medidas de estado fotografadas

**Status:** aceito — Fase 21
**Decisores:** time técnico
**Relacionados:** [0017](./0017-tempo-real-da-operacao.md), [0018](./0018-previsao-de-chegada-e-sla.md), [0026](./0026-webhooks-e-backbone-assincrono.md), [0029](./0029-retencao-de-localizacao.md)

## Contexto

O critério de aceite da fase não é técnico: **explicar uma entrega problemática sem abrir o banco**. O caso
que mais dói é conhecido — o cliente diz que não foi avisado, e do lado de dentro "a mensagem saiu".

Entre a conclusão da entrega e a chamada ao assinante existe uma fila. Fila corta contexto: o trabalho
muda de processo, de thread e de instante, e a instrumentação automática não tem como saber que aquele
`POST` de agora é consequência daquela requisição de dez minutos atrás. Sem resolver isso, teríamos dois
rastros perfeitos e nenhuma resposta.

Há ainda uma segunda classe de pergunta que contador nenhum responde: *quantos agora?* Quantos motoristas
estão offline, quantas entregas correm risco, quanto espera no outbox.

## Decisão

### OpenTelemetry com OTLP, e nada de fornecedor embutido

Traces e métricas por OpenTelemetry; exportação por OTLP, o protocolo aberto. **Sem endereço configurado,
nada sai do processo** — os instrumentos continuam funcionando, e ninguém precisa subir coletor para rodar
o projeto na própria máquina.

Nenhum SDK de fornecedor entra no código. Trocar de ferramenta é trocar um endereço, não reinstrumentar o
sistema.

Cinco dependências novas, todas OSS e mantidas pela CNCF: hospedagem, instrumentação de ASP.NET Core, de
`HttpClient`, de runtime, e o exportador OTLP. O Npgsql publica a própria fonte de rastro desde a versão 7 —
o comando SQL entra no trace **sem pacote de instrumentação** e sem envelopar o provedor.

### Amostragem configurável, respeitando o pai

`ParentBasedSampler` sobre `TraceIdRatioBasedSampler`, proporção configurável (padrão 1).

A telemetria de GPS é o motivo: a 33 posições por segundo, amostrar tudo produz volume que custa mais que
o problema que ele ajudaria a achar. Respeitar o pai importa tanto quanto a taxa — um rastro que começou
amostrado continua inteiro, em vez de virar uma história com buracos no meio.

### O rastro atravessa a fila dentro da própria mensagem

A mensagem do outbox ganhou uma coluna `rastro`, que guarda o `traceparent` do W3C. Ela é preenchida
**dentro da transação que grava o fato** — o único momento em que ainda se sabe qual requisição produziu o
evento. A entrega de webhook herda esse valor da mensagem, e o `POST` ao assinante nasce com ele como pai.

Resultado: quem tem o identificador que o cliente recebeu chega ao webhook que falhou, sem saber que existe
fila no meio do caminho.

O campo é de diagnóstico, não de negócio: é anulável, nada no domínio depende dele, e um valor
irreconhecível faz o rastro começar do zero em vez de derrubar a entrega. Telemetria não quebra operação.

### O despacho do outbox tem rastro próprio

O lote junta eventos de origens diferentes. Pendurá-lo em um dos rastros de origem seria mentira: as outras
mensagens apareceriam dentro de uma operação com a qual não têm relação. O despachante conta a própria
história; o elo com a origem viaja na mensagem e reaparece na entrega.

### Medidas de estado são fotografadas, não consultadas na raspagem

Os *gauges* (`drivers.online`, `deliveries.at_risk`, `outbox.pending`, …) leem um retrato em memória,
atualizado por um serviço em segundo plano a cada 30 segundos.

Consultar o banco no instante em que o coletor raspa a métrica ligaria a saúde do sistema à frequência de
raspagem de quem observa — e um coletor mal configurado viraria carga. Quando uma leitura falha, o retrato
anterior continua publicado, com o erro no log: métrica velha informa mais que métrica zerada, porque zero
é um valor plausível e ninguém saberia que significa "não consegui medir".

As consultas do retrato ignoram o filtro de organização de propósito: a medida é do processo, não de um
tenant — não há sessão por trás dela, e o filtro, que falha fechado, devolveria zero para tudo.

### Log e rastro se apontam

`TraceId` e `SpanId` entram no escopo de log junto com o identificador de correlação. Sem isso, log e trace
seriam dois relatos do mesmo fato sem nada que os ligasse.

Os logs continuam no Serilog, não são exportados por OTLP. São dois pipelines maduros fazendo cada um o que
faz bem; unificá-los agora seria trocar um formato que funciona por outro, sem ganho para o critério de
aceite.

## Consequências

Existe agora uma coluna no banco que só serve para diagnóstico. É dívida aceita conscientemente: a
alternativa — uma tabela de correlação à parte — custaria uma junção em todo despacho para guardar a mesma
informação.

O retrato tem até 30 segundos de atraso. Para painel operacional isso é irrelevante; para alarme de
precisão em segundos, não serve — e o motor de alertas, que é quem precisa disso, não depende dele.

Sem coletor configurado, não há telemetria retida em lugar nenhum: o sistema é observável, mas só quando
alguém liga a observação. A escolha de coletor é da Fase 25, junto com hospedagem.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| SDK de fornecedor (Datadog, New Relic, Application Insights) | lock-in de código e custo por volume, para um projeto que precisa ficar barato no ar |
| Só logs estruturados, sem traces | não atravessam a fila: o webhook seria um relato solto |
| Correlação por campo próprio em vez de `traceparent` | reinventaria um padrão que as ferramentas já entendem |
| Tabela separada de correlação | junção em todo despacho para guardar a mesma coisa |
| Gauge consultando o banco na raspagem | a carga passaria a depender da configuração de quem observa |
| Zerar a métrica quando a leitura falha | zero é valor plausível; esconderia a falha de medição |
| Exportar logs por OTLP | trocaria um pipeline maduro por outro sem ganho para o critério de aceite |
| Amostragem fixa em 100% | a telemetria de GPS torna o volume caro em produção |

## Como a decisão é verificada

| Afirmação | Prova |
|---|---|
| A entrega problemática é rastreável ponta a ponta | `EntregaProblematicaEhRastreavelDoInicioAoWebhookQueFalhou`: o `traceparent` do cliente reaparece no span do webhook que respondeu 500 |
| O rastro atravessa a fila | o mesmo teste: conclusão e entrega de webhook compartilham o `TraceId`, com o outbox no meio |
| O SQL entra no rastro sem instrumentação escrita à mão | o mesmo teste exige uma atividade da fonte `Npgsql` no rastro |
| Log e trace se apontam | o mesmo teste procura o `TraceId` no texto registrado |
| O despacho não se pendura na origem errada | `DespachoDoOutboxTemRastroProprioSeparadoDaOrigem` |
| As medidas contam a operação real | `MedidasDeEstadoContamAOperacaoEmAndamento`: entrega em rota e motorista offline antes, um a menos depois da conclusão |
