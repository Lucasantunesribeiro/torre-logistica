# ADR 0014 — Máquina de estados em tabela, comandos nomeados e concorrência decidida pela versão da linha

- **Data:** 2026-09-14
- **Status:** aceita
- **Fase:** 5

## Contexto

A Fase 5 formaliza as transições da entrega e proíbe *last-write-wins* silencioso. O cenário
obrigatório é **"operador tenta reatribuir enquanto motorista conclui"**: só uma sequência válida
pode prevalecer, e o conflito precisa ter contrato explícito. O critério de aceite proíbe endpoint
genérico de status: toda mudança passa por caso de uso com nome.

Até a Fase 4 existiam criar, planejar, atribuir, retirar da rota e cancelar. Faltavam sair para
rota, chegar, concluir, registrar tentativa sem sucesso, reagendar — e quem executa isso é o
motorista, por outro canal.

## Decisão

### Uma tabela, uma fonte

`MaquinaDeEstadosDaEntrega` guarda, para cada `ComandoDaEntrega`, os status de origem e o status
resultante. O que não está na tabela é proibido.

| Comando | De | Para |
|---|---|---|
| Planejar | Criada, Reagendada | Planejada |
| Atribuir | Planejada, Atribuída | Atribuída |
| Reatribuir | Em rota, Próxima do destino | (mesmo status) |
| RetirarDaRota | Planejada, Atribuída | Criada |
| IniciarRota | Atribuída | Em rota |
| RegistrarChegada | Em rota | Próxima do destino |
| Concluir | Em rota, Próxima do destino | Entregue |
| RegistrarTentativaFrustrada | Em rota, Próxima do destino | Tentativa frustrada |
| Reagendar | Tentativa frustrada, Reagendada | Reagendada |
| Cancelar | Criada, Planejada, Atribuída, Tentativa frustrada, Reagendada | Cancelada |

Nada sai de Entregue ou Cancelada. Uma entrega concluída nunca volta para rota (CLAUDE.md, seção 10).

Dentro da `Entrega`, **um único método privado muda `Status`**: consulta a tabela e recusa com
`409` o que não está nela. As regras derivadas (pode cancelar, pode entrar em rota, está em rota não
iniciada) passaram a ser leituras da mesma tabela, em vez de listas próprias que poderiam divergir.

Concluir não exige chegada registrada: o registro da chegada pode ter falhado, e a entrega aconteceu.

### Rota

A rota ganhou `Iniciar` (Planejada → Em andamento, só pelo motorista da rota, com motorista e
veículo ativos) e `Concluir` (Em andamento → Concluída, só com todas as entregas das paradas
resolvidas: entregue, tentativa frustrada, reagendada ou cancelada). Concluir inativa as paradas —
é o que libera a entrega reagendada para outra rota, pelo índice parcial do ADR 0013.

A troca de motorista passou a ser aceita **também em andamento**: é a reatribuição operacional
(motorista que passou mal, veículo que quebrou). Paradas, ordem, veículo e saída continuam só em
montagem ou planejada.

### Comandos nomeados, nunca status

| Quem | Rota |
|---|---|
| Motorista | `POST /api/motorista/rotas/{id}/inicio` · `.../conclusao` |
| Motorista | `POST /api/motorista/entregas/{id}/chegada` · `.../conclusao` · `.../tentativa-frustrada` |
| Operação | `POST /api/entregas/{id}/reagendamento` |
| Supervisão | `PUT /api/rotas/{id}/motorista` (inclusive em andamento) |

Não existe `PATCH`, nem rota com "status" no caminho, nem campo `status` aceito em corpo — três
provas automatizadas, uma por forma de burlar.

### Idempotência pela máquina de estados

Comandos do motorista cujo resultado já está aplicado respondem `200` com o estado atual e **não
geram evento**: sair duas vezes, chegar duas vezes, concluir duas vezes, concluir a rota duas vezes.
É o que torna seguro o aplicativo repetir o envio quando a resposta se perdeu. A deduplicação por
identificador de operação gerado no cliente é assunto das fases offline (11 e 12).

### Concorrência: a versão da linha decide

Cada comando carrega a linha, aplica a regra e grava com a versão lida (`xmin`) como condição. Na
corrida do cenário obrigatório:

| Quem grava primeiro | O que o outro recebe |
|---|---|
| Motorista conclui | A reatribuição que já tinha lido a entrega recebe `409 conflito_de_versao`; se leu depois, segue e deixa a entrega concluída intocada |
| Operador reatribui | A conclusão que já tinha lido a entrega recebe `409 conflito_de_versao`; se leu depois, recebe `409 entrega_reatribuida` |

Nunca as duas prevalecem sobre a mesma entrega: ou ela foi entregue pelo motorista original e não
tem reatribuição, ou foi reatribuída e não tem conclusão do motorista original.

### Motorista resolvido pela sessão; contrato explícito para quem perdeu a entrega

O motorista vem da conta autenticada (associação da Fase 2). Rota ou entrega que não está com ele
responde `404`, idêntico a identificador inexistente — exceto a entrega que **já foi dele** e passou
a outro motorista: `409 entrega_reatribuida`. A distinção é feita pela timeline (evento de
atribuição com o motorista), para o aplicativo mostrar "esta entrega foi passada a outro motorista"
sem revelar a existência de entregas que nunca foram dele.

A entrega guarda o motorista responsável (`motorista_id`). Ele continua após o resultado, para que
o motorista possa repetir o próprio comando e para registro.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| `PATCH /entregas/{id}` com `status` | Proibido pelo critério de aceite; transição sem intenção |
| Biblioteca de máquina de estados | Uma tabela e um método resolvem, com teste de especificação; dependência sem ganho |
| Trava pessimista na entrega durante a conclusão | Serializaria leituras longas; a versão da linha resolve a corrida sem espera |
| Reatribuição sempre vence | É exatamente o *last-write-wins* que a fase proíbe |
| 404 para toda entrega fora do motorista | O motorista que perdeu a entrega não saberia o que houve |
| Deduplicação por identificador de operação agora | Pertence às fases offline; a idempotência de estado cobre o reenvio online |

## Consequências

- GPS (Fase 6) e geofence (Fase 7) chegam como novos chamadores de `RegistrarChegada` e ganham a
  mesma idempotência.
- Prova de entrega (Fase 14) vira pré-condição de `Concluir` sem mudar a tabela.
- Uma transição nova é uma linha na tabela e uma linha no teste de especificação — o teste de
  arquitetura falha se surgir método que muda status sem estar entre os comandos.

## Como isto é verificado

- `MaquinaDeEstadosDaEntregaTestes` — tabela comando × status (90 casos), nada sai de status final,
  todo status alcançável a partir de Criada.
- `ExecucaoDaEntregaTestes` e `ExecucaoDaRotaTestes` — caminho feliz, repetição sem evento,
  nove transições proibidas importantes sem mudar nada, reatribuição, início e conclusão da rota.
- `ComandosDeStatusTestes` (arquitetura) — `Status` sem setter público, nenhum método genérico de
  status, e métodos que produzem evento iguais aos comandos da máquina.
- `ExecucaoTestes` (PostgreSQL real) — nenhum `PATCH` nem rota de status e lista exata de comandos;
  execução completa com timelines coerentes; repetição e toque duplo sem duplicar efeito; transições
  proibidas com 409; cenário obrigatório em seis rodadas concorrentes e nas duas ordens
  determinísticas; isolamento do motorista; reagendamento.
