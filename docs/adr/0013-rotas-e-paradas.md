# ADR 0013 — Rotas e paradas: parada como associação, regras entre rotas no banco e ordem versionada

- **Data:** 2026-09-14
- **Status:** aceita
- **Fase:** 4

## Contexto

A Fase 4 cria a rota: o agrupamento de entregas que um motorista, num veículo, executa num dia.
O ROADMAP pede criar rota, incluir e retirar entregas elegíveis, ordenar paradas, atribuir
motorista e veículo, planejar saída e consultar a sequência — com quatro regras:

1. entrega não pode estar em duas rotas ativas;
2. rota concluída não aceita alteração estrutural;
3. motorista e veículo inativos não podem ser atribuídos;
4. a ordem das paradas é versionada ou registrada quando alterada.

A primeira regra, e a de motorista ou veículo em duas rotas no mesmo dia, **atravessam rotas**:
nenhuma rota sozinha consegue garanti-las.

## Decisão

### Parada é a associação rota–entrega

Uma parada, uma entrega. `paradas (rota_id, entrega_id, sequencia, ativa, removida_em,
motivo_da_remocao)` é a associação *RotaEntrega* do ROADMAP. Agrupar várias entregas do mesmo
endereço numa parada é otimização de roteirização, fora do escopo da v1 (CLAUDE.md, seção 5); se
vier, a parada passa a ter várias entregas sem mudar quem a referencia.

Retirar não apaga: a parada fica inativa, com instante e motivo (`DecisaoDoPlanejamento`,
`EntregaCancelada`, `RotaCancelada`). Uma entrega retirada pode voltar à mesma rota — ganha outra
parada.

### A rota é dona das paradas; a aplicação coordena rota e entrega

Incluir, retirar, reordenar e cancelar passam pelo agregado `Rota`, que numera, versiona a ordem e
registra o evento. Toda mudança toca a linha da rota, então a versão da linha (`xmin`) serializa
mudanças concorrentes **na mesma rota**.

A rota não altera a entrega — é outro agregado. `GestaoDeRotas` chama, na mesma gravação,
`Entrega.Planejar` (inclusão), `Entrega.Atribuir` (motorista definido ou trocado) e
`Entrega.RetirarDaRota` (retirada e cancelamento da rota). Os eventos das duas timelines entram no
mesmo commit.

### Regras entre rotas: conferência antes, índice parcial decide a corrida

| Regra | Índice único parcial |
|---|---|
| Entrega em no máximo uma rota ativa | `paradas (entrega_id) WHERE ativa` |
| Motorista em no máximo uma rota ativa por dia | `rotas (organizacao_id, motorista_id, data) WHERE motorista_id IS NOT NULL AND status IN (ativos)` |
| Veículo em no máximo uma rota ativa por dia | idem com `veiculo_id` |

A aplicação consulta antes para dar a mensagem (`entrega_em_outra_rota`, `motorista_ja_em_rota`,
`veiculo_ja_em_rota`); duas requisições simultâneas passam juntas pela consulta, e o índice recusa
a segunda. A violação é traduzida em `409` pelo nome da restrição. A condição de "ativa" no filtro
é gerada a partir do enum, para não divergir dele.

Na inclusão simultânea da mesma entrega em rotas diferentes há ainda uma segunda barreira: as duas
gravações alteram a mesma entrega, e a versão da linha dela recusa a segunda.

### Status da rota

| Status | Aceita mudança estrutural | Ocupa entregas, motorista e veículo | Cancelável |
|---|:---:|:---:|:---:|
| EmMontagem | ✅ | ✅ | ✅ |
| Planejada | ✅ | ✅ | ✅ |
| EmAndamento | — | ✅ | — |
| Concluida | — | — | — |
| Cancelada | — | — | — |

Nesta fase existem as transições para EmMontagem, Planejada e Cancelada; iniciar e concluir nascem
com a execução pelo motorista. "Mudança estrutural" é incluir, retirar, reordenar, atribuir e
planejar saída — tudo recusado com `409 rota_nao_editavel` fora dos dois primeiros status.

**Planejar** exige ao menos uma parada, motorista, veículo e saída futura. Rota planejada que
perde a última parada (inclusive por cancelamento da entrega) **volta para montagem**, com evento:
planejada sem parada não é rota.

**Cancelar** a rota inativa as paradas e devolve as entregas a `Criada`. Repetir não tem efeito.

### Ordem versionada e registrada

`rotas.versao_da_ordem` sobe a cada inclusão, retirada ou reordenação. O evento
`ParadasReordenadas` guarda a ordem anterior, a nova (só identificadores de entrega) e a versão.

Reordenar exige a `versao` lida da rota: é a única operação que **substitui** algo que outra
pessoa pode ter mudado desde a leitura — reordenar sobre uma lista velha desfaria uma inclusão
alheia. As demais são comandos cujo resultado é exatamente o pedido e não exigem versão.

A nova ordem precisa ser uma permutação exata das paradas ativas (`422 ordem_invalida`).

Não há restrição única `(rota_id, sequencia)` no banco: renumerar várias linhas numa gravação
violaria a restrição no meio do caminho. A sequência é garantida pelo agregado, sob a versão da
linha da rota.

### Entrega em rota

| Transição | De | Para |
|---|---|---|
| `Planejar` | Criada, Reagendada | Planejada |
| `Atribuir` | Planejada, Atribuída | Atribuída (repetida registra a troca de motorista) |
| `RetirarDaRota` | Planejada, Atribuída | Criada |

Cancelar uma entrega que está numa rota que ainda não saiu retira a parada na mesma gravação. Pelas
tabelas da Fase 3, entrega planejada já não troca de cliente; destino e janela ainda mudam até a
saída.

### Data e saída

`data` é o dia operacional informado pelo console, como a organização o chama. Ainda não há fuso
configurado por organização (CLAUDE.md, seção 49), então as validações usam UTC com um dia de
tolerância: a data vai de ontem até 60 dias à frente, e a saída planejada precisa ser futura e cair
a no máximo um dia da data. Quando o fuso da organização existir, as duas validações ficam exatas.

### Quem monta

Administrador e Supervisor montam e atribuem (`operacao:gestao`); Operador consulta. Entrega,
motorista, veículo e hub de outra organização respondem `404`, idêntico a identificador inexistente.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Tabela `rotas_entregas` separada das paradas | Duas tabelas para a mesma associação 1:1, sem ganho hoje |
| Apagar a parada ao retirar | Perde de onde a entrega saiu e por quê |
| Restrição única adiável `(rota_id, sequencia)` | Exige restrição de exclusão ou `DEFERRABLE`; a versão da rota já serializa |
| Só conferência na aplicação para "entrega em duas rotas" | Duas inclusões simultâneas passam juntas |
| `PUT` da rota inteira com paradas | Mistura mudanças com regras e efeitos diferentes e apaga a intenção na timeline |
| Versão exigida em toda operação | Comando de inclusão ou atribuição não sobrescreve nada; exigir versão só geraria conflito falso |
| Fuso por organização agora | Configuração de organização é outra frente; a tolerância de um dia não permite estado errado |

## Consequências

- A execução da rota (fases seguintes) entra como `Iniciar` e `Concluir` no agregado, mudando o
  status para `EmAndamento` e `Concluida` — as tabelas de regra já dizem o que isso bloqueia.
- Duas pessoas incluindo entregas **diferentes** na mesma rota ao mesmo tempo: uma recebe
  `409 conflito_de_versao` e repete. A inclusão aceita várias entregas numa chamada justamente para
  a montagem não depender de muitas chamadas pequenas.
- Mudar o índice de ocupação de motorista (por exemplo, permitir duas rotas no mesmo dia em turnos)
  exige migration e revisão deste ADR.

## Como isto é verificado

- `RotaTestes`, `RegrasDaRotaTestes` e `EntregaEmRotaTestes` — numeração, inclusão tudo ou nada,
  retirada com renumeração e motivo, permutação, atribuição recusando inativo e outra organização,
  requisitos do planejamento, volta para montagem, cancelamento, tabelas de status e transições da
  entrega.
- `RotasTestes` (PostgreSQL real) — critério de aceite do supervisor, entrega em duas rotas,
  inclusões simultâneas da mesma entrega em cinco rotas com um vencedor, atribuições simultâneas do
  mesmo motorista no mesmo dia com um vencedor, liberação por cancelamento, retirada, reordenação
  com versão, planejamento incompleto, rota cancelada recusando mudança estrutural, cancelamento de
  entrega em rota planejada, isolamento em todas as operações, cadastros de outra organização,
  timeline recusando `UPDATE`/`DELETE`/`TRUNCATE`, filtros e corpos malformados.
- `AutorizacaoTestes` — novas linhas da matriz e mapa de rotas contra o roteamento real.
