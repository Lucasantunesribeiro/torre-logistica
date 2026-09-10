# ADR 0005 — SignalR como direção de tempo real, atrás de abstração

- **Data:** 2026-09-09
- **Status:** aceita
- **Fase:** 0 (direção); implementação na Fase 8

## Contexto

O Mapa da Operação é a tela símbolo do produto: posição de motorista, mudança de
status, risco de atraso e alerta precisam aparecer sem que o operador aperte F5.

A decisão tem consequência pesada em infraestrutura. Conexão persistente elimina
compute serverless por invocação como opção principal de hospedagem da API — é por
isso que ela é registrada agora, na Fase 0, e não quando o tempo real for
implementado. Decidir depois significaria descobrir tarde que a hospedagem escolhida
não serve.

## Decisão

SignalR é a direção para tempo real, **atrás de uma abstração** do tipo
`IOperationRealtimePublisher`, declarada em `Application` e implementada em
`Infrastructure`.

O domínio não conhece SignalR. Nenhuma regra de negócio importa um `Hub`.

Sem backplane Redis no início. A arquitetura deve permitir acrescentá-lo — ou usar
serviço gerenciado — quando houver mais de uma instância, sem criar dependência
operacional permanente por hipótese de escala futura.

Tempo real é **transporte de notificação, nunca fonte de verdade**. O cliente que
perdeu mensagem recupera estado consultando a API; o banco continua autoritativo.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Polling a cada poucos segundos | Multiplica carga de leitura por cliente conectado e ainda entrega atualização atrasada numa tela cuja razão de existir é imediatismo |
| Server-Sent Events | Resolve servidor para cliente, que é o fluxo principal, mas reconexão, grupos e fallback já vêm resolvidos no SignalR — reimplementar isso não agrega |
| WebSocket na mão | Reconexão, heartbeat, multiplexação e fallback para long polling são exatamente o que o SignalR entrega testado |
| Serviço gerenciado de realtime desde já | Custo fixo e lock-in antes de existir o primeiro evento |

## Consequências

- A hospedagem da API precisa sustentar conexão longa: entra na comparação da Fase 25
  e exclui o modelo de função por invocação como compute principal.
- Uma instância só, no início. Escalar horizontalmente exige backplane — decisão
  adiada, mas não bloqueada.
- O domínio permanece testável sem subir hub algum, porque publica por abstração.
- Canal e grupo precisam respeitar tenant: motorista não assina canal administrativo e
  rastreamento público não usa canal interno. Requisito de teste da Fase 8.

## Como isto é verificado

Nesta fase, pela ausência: o teste de arquitetura reprova pacote `Microsoft.AspNetCore`
em `Domain` e em `Infrastructure`, o que impede SignalR de vazar para dentro quando a
implementação chegar.

Na Fase 8, por teste de isolamento entre organizações no canal de tempo real.
