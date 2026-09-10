# ADR 0006 — Simulador como cliente externo, sem acesso ao núcleo

- **Data:** 2026-09-09
- **Status:** aceita
- **Fase:** 0 (estrutura); cenários na Fase 23

## Contexto

A demonstração pública precisa de uma operação logística acontecendo: motorista em
rota, posição mudando, ETA piorando, alerta nascendo, entrega concluída com
comprovante. O caminho mais rápido para isso é um seed que escreve direto nas tabelas.

O caminho mais rápido é também o que destrói o valor da demonstração. Um `UPDATE`
direto em `entregas` produz estado que o sistema real nunca produziria: entrega
concluída sem evento de timeline, posição atual sem histórico, geofence atravessado
sem evento de entrada. A tela fica convincente e não prova nada — e pior, esconde
defeito justamente no caminho que a produção usa.

## Decisão

`TorreLogistica.Simulator` é um projeto separado que **não referencia** `Domain`,
`Application` nem `Infrastructure`, e não referencia pacote de acesso a banco algum.

Ele fala com a aplicação exclusivamente por HTTP, como qualquer integrador externo,
definindo seus próprios contratos de requisição e resposta.

A restrição é estrutural, não uma regra escrita num documento: sem essas referências,
escrever no banco deixa de ser possível, não apenas proibido.

O simulador trata erro como cliente real trataria. Conflito, rate limit e
indisponibilidade são comportamento esperado e observável, não exceção a silenciar.
Nenhum atalho de demonstração passa por cima de regra de domínio.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Seed SQL direto | Gera estado impossível e não exercita caminho de produção; a demonstração passaria a mentir |
| Simulador dentro da API, com acesso ao contexto de persistência | A tentação de "só este atalho" ficaria a um `using` de distância, e em algum momento alguém aceita |
| Simulador referenciando `Domain` para reaproveitar enumerações | Abre a porta para referenciar `Infrastructure` em seguida; contrato próprio é o preço de a fronteira ser real |
| Gravar e reproduzir tráfego HTTP | Reproduz o passado e não reage ao estado atual; um conflito legítimo viraria falha do roteiro |

## Consequências

- Cada cenário de demonstração é também um teste de ponta a ponta dos contratos
  públicos da API.
- Contrato duplicado: o simulador mantém os próprios tipos de transporte. É o custo de
  a fronteira não ser apenas declarada. Divergência aparece como falha do simulador,
  que é exatamente onde um integrador real sentiria.
- A demonstração precisa de credencial válida e de respeitar rate limit, como
  integrador de verdade.
- Reset de cenário existe apenas em ambiente de demonstração e protegido (Fase 23).

## Como isto é verificado

`TorreLogistica.ArchitectureTests` reprova:

- qualquer referência de projeto no simulador;
- qualquer pacote de acesso a banco (`Npgsql`, `Microsoft.EntityFrameworkCore`, `Dapper`).

Na Fase 0 o simulador já exercita o único contrato publicado — o endpoint de prontidão —
provando que o caminho "simulador → HTTP → aplicação real" está de pé.
