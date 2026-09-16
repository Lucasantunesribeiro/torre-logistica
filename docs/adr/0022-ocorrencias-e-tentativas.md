# ADR 0022 — Ocorrências e tentativas de entrega

- **Status:** aceita
- **Data:** 2026-09-15
- **Fase:** 13 — Ocorrências e Tentativas de Entrega

## Contexto

Até a Fase 12, a única falha modelada era a tentativa sem sucesso: um motivo tipado no agregado
`Entrega`, um contador e um evento na timeline. Isso não cobre a última milha real — veículo quebrado,
mercadoria avariada, acidente, rua interditada —, não guarda **onde** nem **quando** a falha aconteceu
(diferente de quando foi registrada), não guarda **quem** registrou, e não tem severidade: tudo pesa igual
para quem olha a torre.

O roadmap da Fase 13 pede motivos tipados (incluindo `ProblemaComVeiculo`, `ProblemaComMercadoria` e
`Outro`), ocorrências com tipo, severidade, observação, horário, localização e autor, tentativa produzindo
timeline, e a regra: **não permitir texto livre como única estrutura quando motivo tipado existe**. O tipo
de alerta `OcorrenciaCritica`, definido na Fase 10, esperava esta fase para ter produtor.

## Decisão

### Ocorrência é entidade própria, somente-inserção

- Tabela `ocorrencias`, com trigger que recusa `UPDATE`, `DELETE` e `TRUNCATE` — como a timeline da entrega
  e o ciclo de vida dos alertas. Ocorrência é fato: corrigir é registrar outra, e as duas ficam.
- Campos: entrega, rota e motorista (quando havia), tipo, severidade, motivo tipado (só na tentativa),
  observação, localização (`geography(Point,4326)`), `ocorrida_em`, `registrada_em`, origem e autor.
- `ocorrida_em` e `registrada_em` são coisas diferentes, como `capturedAt` e `receivedAt` do GPS: a ocorrência
  feita sem conexão chega depois, e o horizonte é o mesmo da operação offline (7 dias, com 2 min de tolerância
  de relógio adiantado).

### Vocabulário fechado primeiro; texto é complemento

- `MotivoDeTentativaFrustrada` ganhou `ProblemaComVeiculo`, `ProblemaComMercadoria` e `Outro`. Os cinco
  motivos anteriores **não foram renomeados**: eles já estão gravados em `eventos_da_entrega`, em
  `operacoes_do_cliente` e nas filas dos aparelhos. `EnderecoNaoLocalizado` e `RecusadaPeloDestinatario`
  cobrem o "EnderecoNaoEncontrado" e o "Recusado" do roadmap sem migração de dados nem quebra de cliente.
- A descrição só é **exigida** quando o vocabulário não diz o que houve: tipo `Outro` ou motivo `Outro`. Fora
  daí ela é opcional e complementar — nunca a única estrutura. A regra está no domínio e também como
  `CHECK` no banco.

### Tentativa e ocorrência no mesmo commit

- A tentativa sem sucesso muda o status da entrega, então continua sendo comando do motorista
  (`POST /api/motorista/entregas/{id}/tentativa-frustrada`). Ela grava, **na mesma transação**, o novo status,
  o evento da timeline e a ocorrência do tipo `TentativaDeEntrega`. Repetir não gera evento nem ocorrência
  nova (idempotência pela máquina de estados, como nas outras ações).
- As ocorrências que **não** mudam status têm rota própria — `POST /api/motorista/entregas/{id}/ocorrencia`
  para o motorista e `POST /api/entregas/{id}/ocorrencias` para a operação. Cada rota recusa o que é da outra,
  com código explícito.
- Consulta: `GET /api/ocorrencias` (filtros por entrega, rota, motorista, tipo, severidade e período) e
  `GET /api/entregas/{id}/ocorrencias`.

### Severidade vem de catálogo, e a crítica vira alerta

- O catálogo define a severidade padrão por tipo e motivo: mercadoria e acidente nascem **críticos**, veículo
  **alto**, tentativa e acesso **médios**, `Outro` **baixo**. Quem registra pode informar outra severidade;
  sem informar, vale a do catálogo.
- `RegrasDeAlerta.OcorrenciaCritica` fecha o ciclo aberto na Fase 10: a entrega com ocorrência crítica e ainda
  em aberto abre alerta `Critica`; o alerta resolve sozinho quando a entrega sai da operação (entregue,
  cancelada ou reagendada). A ocorrência não "some" — quem termina é a entrega.

### Privacidade

- A observação **não** entra na timeline (que é permanente e não poderia ser apagada) nem na evidência do
  alerta: a timeline registra `comDescricao: true`, e o alerta diz que há descrição. O texto vive na
  ocorrência, atrás da autorização de quem consulta.
- Coordenada da ocorrência é guardada, mas não vai para o aviso de tempo real (`IncidentCreated`), que leva só
  identificadores, tipo, severidade e instante.

### Offline

- A fila do aparelho (Fase 12) passou a carregar a descrição da tentativa: a operação
  `RegistrarTentativaFrustrada` tem `observacao`, ela entra no registro de `operacoes_do_cliente` e faz parte
  da identidade do pedido — mesma operação com outra descrição é `operacao_divergente`, não herda o desfecho.

## Alternativas consideradas

- **Ocorrência como tipo de evento da timeline.** A timeline é somente-inserção e sem dado pessoal; ocorrência
  tem texto do motorista, coordenada e severidade. Misturar as duas obrigaria a enfraquecer uma das regras.
- **Renomear os motivos para os nomes exatos do roadmap.** Exigiria migração de dados gravados como texto em
  três tabelas e quebraria aparelhos com fila pendente, sem ganho semântico.
- **Texto livre obrigatório em toda ocorrência.** É o que o roadmap proíbe: vira o campo que ninguém lê e que
  não dá para agregar.
- **Severidade fixa por tipo, sem poder mudar.** A mesma quebra de veículo pode ser crítica numa rota de
  medicamento e alta numa rota comum; o padrão do catálogo resolve o caso comum sem impedir o julgamento.
- **Alerta de ocorrência crítica com janela de tempo.** Preferimos amarrar ao ciclo da entrega: enquanto a
  entrega estiver aberta, o problema continua de pé.

## Consequências

- A falha de entrega passa a ter rastro completo: o que, quando, onde, quem registrou e com que gravidade.
- A torre recebe alerta crítico no momento em que o motorista registra mercadoria avariada ou incidente.
- A tabela `ocorrencias` cresce com a operação; retenção entra com as políticas de retenção do roadmap.
- O aplicativo passa a exigir descrição em "Outro motivo" — inclusive sem conexão, antes de guardar na fila.

## Limitações conhecidas

- Não há anexo de foto na ocorrência: prova de entrega e upload são da Fase 14.
- Não há fluxo de tratamento da ocorrência (atribuir, encerrar com parecer): o alerta é que tem ciclo de vida.
- A ocorrência que não muda status ainda não passa pela fila offline do aparelho — o motorista precisa de
  conexão para registrá-la; a tentativa sem sucesso, que é a crítica para a rota, já vai pela fila.
