# ADR 0031 — Não particionar `posicoes`, e outras decisões que a medição decidiu

**Status:** aceito — Fase 22
**Decisores:** time técnico
**Relacionados:** [0015](./0015-ingestao-de-localizacao.md), [0026](./0026-webhooks-e-backbone-assincrono.md), [0029](./0029-retencao-de-localizacao.md), [0030](./0030-observabilidade.md)

## Contexto

O ROADMAP desta fase tem uma ordem embutida: **medir antes de otimizar**, e particionar `posicoes` apenas
se a medição justificar — *"não implementar particionamento por estética arquitetural"*.

Até aqui, três decisões de desenho tinham sido tomadas sem número: separar histórico de posição atual,
criar índice por `(motorista, captura)` e criar índice por `recebimento` para a retenção. Nenhuma estava
errada, mas nenhuma estava provada.

## Decisão

### Não particionar `posicoes`

Com um dia de operação na meta — 2.851.200 posições, 1 GB — as consultas que importam respondem por índice:
0,19 ms para o histórico de um motorista num período, 12 ms para apagar um lote de 5.000 na retenção.

Particionar resolveria um problema que não existe e não resolveria o que existe: o peso dos índices, que
continuaria igual, só que distribuído.

**O gatilho para rever** fica registrado: se a limpeza por retenção passar a não acompanhar a entrada — o
que aparece como o aviso *rodada encerrada no teto de lotes com dado vencido restante* —, particionar passa
a valer, porque aí o expurgo vira `DROP PARTITION` em vez de `DELETE` em lote.

### Não retentar `57P01`

O PostgreSQL devolve `57P01` quando encerra a conexão por comando administrativo — reinício, failover. O
provedor **não** classifica esse código como falha transitória, então a estratégia de nova tentativa, que
está ativa, não entra em ação.

Ampliar a classificação foi considerado e recusado: repetir escrita por conta própria custa mais do que um
erro isolado num reinício planejado. A primeira operação depois da queda falha; da segunda em diante o
processo se recupera sozinho. Isso fica documentado e **travado por teste** — que é a diferença entre
limitação conhecida e surpresa.

### O benchmark fica desligado por padrão

Benchmark dentro de suíte de regressão compete por CPU com o que roda antes e depois, mede errado e vira
teste instável — que alguém acaba desabilitando por um motivo que não tem nada a ver com desempenho. Ele
roda sob demanda, com `TORRE_CARGA=1`.

Isso **não** é teste desabilitado para deixar a suíte verde: o benchmark não guarda invariante de
comportamento, e o que ele produz — números com ambiente declarado — só vale quando alguém vai olhar.

### A carga se concentra em menos motoristas, de propósito

A meta são 500 motoristas a uma posição a cada 15 segundos. O benchmark produz a mesma taxa com 60
motoristas enviando com mais frequência, e isso é conservador: concentrar a taxa em menos chaves aumenta a
disputa no `UPSERT` condicional da posição atual, que é onde poderia engasgar. Criar 500 contas reais
custaria minutos de preparo para medir a mesma coisa num cenário mais fácil.

### Conexão de teste identificada por nome de aplicação

O teste que derruba o banco precisa matar só as conexões da API sob teste. Sem isso, ele atingiria as
conexões que a própria suíte usa para montar cenário, e o teste **seguinte** falharia por um estrago que
não causou — instabilidade introduzida pelo próprio teste de resiliência, que é o pior lugar para tê-la.

## Consequências

Não há tabela particionada para manter, e a retenção continua sendo `DELETE` em lote, que já é medido.

O `p99` de 245 ms sob concorrência fica como gargalo conhecido número um, sem correção nesta fase: investigar
exige as métricas de runtime da Fase 21 ligadas contra um coletor, o que depende da decisão de
infraestrutura da Fase 25. Corrigir no escuro seria exatamente o que esta fase existe para evitar.

Os números têm validade limitada ao ambiente: aplicação e banco na mesma máquina, sem latência de rede.
Está declarado no documento, e a Fase 25 terá de medir de novo.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Particionar por mês desde já | otimização sem medição; e não resolve o custo que a medição achou |
| Retentar `57P01` automaticamente | repetir escrita por conta própria custa mais que um erro isolado em reinício planejado |
| Benchmark na suíte de regressão | mede sob contenção e vira teste instável |
| Projeto separado só para benchmark | duplicaria a infraestrutura de teste para ganhar isolamento que a variável de ambiente já dá |
| 500 contas reais no benchmark | minutos de preparo para medir um cenário mais fácil que o atual |
| Remover índices para economizar espaço | cada um tem caso de uso real; remover paga com consulta mais lenta, e falta dado de uso |

## Como a decisão é verificada

| Afirmação | Prova |
|---|---|
| A ingestão sustenta a taxa alvo | `IngestaoSustentaATaxaDeEngenharia`: reprova abaixo de 95% da meta ou com qualquer posição recusada |
| O volume não degrada a ingestão | `PlanosDeConsultaSobVolumeDeUmDia`: p50 de 9,7 ms com 2,85 milhões de linhas |
| Os índices são realmente usados | o mesmo teste imprime os índices que cada plano escolheu |
| A queda do banco não deixa o processo quebrado | `ConexaoDerrubadaNaoDerrubaAOperacao` |
| A queda no despacho não perde nem duplica | `QuedaNoMeioDoDespachoNaoPerdeNemDuplicaEvento` |
| Os números do documento são reproduzíveis | `docs/performance.md` traz o comando e o ambiente |
