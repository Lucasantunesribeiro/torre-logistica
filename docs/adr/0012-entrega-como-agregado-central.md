# ADR 0012 — Entrega como agregado central: código humano sem lacuna, endereço copiado, timeline numerada e regras em tabela

- **Data:** 2026-09-14
- **Status:** aceita
- **Fase:** 3

## Contexto

A Fase 3 cria a `Entrega`, núcleo da operação, antes de existir rota ou GPS. Tudo o que vier depois
— atribuição, saída para rota, ETA, SLA, prova de entrega — muda o estado dela e escreve na timeline.
Decisões tomadas agora viram contrato para as fases seguintes:

1. Como gerar o código humano (`ENT-2026-004821`) sem repetir sob concorrência?
2. A entrega aponta para o endereço do destinatário ou guarda o próprio?
3. Como garantir que a timeline conta a história inteira, em ordem, sem ser reescrita?
4. Onde ficam as regras de "o que pode mudar em qual status"?
5. Quem opera entregas no dia a dia?

## Decisão

### Código humano: contador por organização e ano, reservado na transação do insert

Tabela `sequencias_de_codigo (organizacao_id, serie, ano, ultimo_numero)`. A reserva é um
`INSERT … ON CONFLICT DO UPDATE … RETURNING` dentro da mesma transação que grava a entrega:

- a linha do contador fica travada até o commit, então duas criações simultâneas recebem números
  consecutivos, nunca o mesmo;
- se a gravação da entrega falhar, a reserva volta atrás junto — não há lacuna;
- sem transação aberta, a reserva recusa executar.

O índice único `(organizacao_id, codigo)` é a segunda barreira. O ano é o do instante **UTC** da
criação: fuso é configuração de apresentação (CLAUDE.md, seção 49), e o código não pode mudar de
ano conforme quem olha.

O custo é serializar a criação de entregas dentro de uma organização pelo tempo de um insert. Na
escala prevista (centenas de entregas por dia por organização) é imperceptível; a Fase 22 mede.

### Endereço copiado na criação

A entrega guarda o próprio endereço e a coordenada. Sem endereço no pedido, copia os do
destinatário **naquele instante**. Alterar o destinatário depois não muda entrega nenhuma: o
histórico de onde a entrega foi feita não pode ser reescrito por um cadastro.

Coordenada sem endereço é recusada (`coordenada_sem_endereco`): misturar endereço do destinatário
com ponto de outro lugar produziria destino incoerente.

### Timeline numerada, com status resultante, somente-inserção no banco

`eventos_da_entrega` tem `sequencia` (1, 2, 3… por entrega) e `status_resultante`. Só o agregado
cria eventos, e o contador `ultima_sequencia_de_evento` fica na linha da entrega — protegido pela
mesma versão (`xmin`) que protege o resto. Consequências:

- ordem estável mesmo com dois eventos no mesmo instante;
- "histórico consistente" é verificável: sequência sem lacuna e último `status_resultante` igual ao
  status atual;
- duas gravações concorrentes não produzem dois eventos com a mesma sequência (índice único).

Trigger recusa `UPDATE`, `DELETE` e `TRUNCATE`, como na auditoria (ADR 0010). `dados` do evento
registra nomes de campos e códigos de motivo — nunca endereço, nome ou observação.

### Regras em tabela, testadas para todos os status

`RegrasDaEntrega` concentra duas tabelas, cobrindo os nove status do CLAUDE.md (seção 10), inclusive
os que só se tornam alcançáveis nas fases seguintes:

| Campo | Pode mudar em |
|---|---|
| cliente | Criada |
| destinatário, endereço, coordenada, janela prometida | Criada, Planejada, Atribuída, Reagendada |
| observações | qualquer status não final |

Cancelamento é permitido em Criada, Planejada, Atribuída, Tentativa frustrada e Reagendada.
Com motorista em rota não: cancelar em movimento exige decidir o que fazer com a carga, assunto
das fases de rota e ocorrência.

Alteração é **tudo ou nada**: se um único campo que mudou não é editável no status atual, nada é
aplicado (`409 campo_nao_editavel`). Entrega final (Entregue, Cancelada) responde
`409 entrega_nao_editavel`.

### Comandos e concorrência

- Alteração (`PUT`) exige a `versao` lida — mesmo contrato dos cadastros (ADR 0011).
- Cancelar não exige versão e é idempotente: repetir não gera evento e mantém o primeiro motivo.
  Duas requisições disputando a mesma linha resultam em um vencedor; a outra recebe `409` e,
  ao repetir, encontra a entrega já cancelada.
- Não existe rota que receba status. `status`, `codigo` e `organizacaoId` no corpo são campos
  desconhecidos e respondem `400`.

### Janela prometida

`JanelaDeEntrega (inicio, fim)`, em UTC, truncada ao segundo, com no máximo 7 dias. Truncar evita
que reenviar a janela lida da API conte como alteração — o PostgreSQL guarda microssegundos, o
.NET, décimos de microssegundo. Na criação e ao mudar a janela, o fim precisa estar no futuro.

### Quem opera entregas

Nova política `entregas:operacao`: Administrador, Supervisor **e Operador** criam, alteram e cancelam.
Entrega é o trabalho do dia; a estrutura (cadastros) continua com Administrador e Supervisor.

Cliente ou destinatário informado na entrega é procurado nos conjuntos filtrados pelo tenant: de
outra organização responde `404`, idêntico a identificador inexistente. Referência nova precisa
estar ativa; a que já estava na entrega não é recobrada — inativar um cliente não trava as
entregas que ele já tem.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| `SEQUENCE` do PostgreSQL por organização | Uma sequence por tenant não escala, e sequence não volta atrás no rollback: lacunas |
| `MAX(numero) + 1` na aplicação | Duas criações simultâneas leem o mesmo máximo |
| Código derivado do UUIDv7 | Não é sequencial nem legível ao telefone |
| Entrega referenciando o endereço do destinatário | Alterar o cadastro reescreveria para onde entregas antigas foram |
| Timeline ordenada só por instante | Dois eventos no mesmo instante não têm ordem garantida |
| Status alterável por `PUT` | Contraria o CLAUDE.md, seção 9: transição sem intenção explícita |
| Regras em `if` dentro de cada método | A regra inteira deixa de ser legível e testável num lugar só |

## Consequências

- Planejar, atribuir, sair para rota e concluir (Fases 4 em diante) entram como métodos do agregado,
  consultando as mesmas tabelas de regra e escrevendo na mesma timeline.
- Alterar uma regra de edição ou cancelamento exige alterar a tabela **e** o teste que a especifica.
- O índice `(organizacao_id, status, prometida_ate)` existe em SQL na migration, não no modelo do
  EF Core: coluna de tipo complexo não entra em `HasIndex`.

## Como isto é verificado

- `RegrasDaEntregaTestes` — as duas tabelas como especificação, para os 9 status e os 6 campos, e
  um teste que falha se surgir status ou campo fora da tabela.
- `EntregaTestes`, `JanelaDeEntregaTestes`, `CodigoDaEntregaTestes` — criação, alteração tudo ou
  nada, cancelamento idempotente, motivo `Outro`, janela, formato do código.
- `EntregasTestes` (PostgreSQL real) — critério de aceite, 12 criações simultâneas com números 1 a 12,
  outra organização começando em 1, tentativa recusada que não consome número, endereço copiado,
  timeline recusando `UPDATE`/`DELETE`/`TRUNCATE`, isolamento em leitura, alteração, cancelamento e
  timeline, referência cruzada entre organizações, filtros, cancelamentos simultâneos com um único
  evento, alteração e cancelamento simultâneos mantendo a história coerente, e ausência de dado
  pessoal na timeline, na auditoria e no log.
- `AutorizacaoTestes` — novas linhas da matriz e mapa de rotas contra o roteamento real.
