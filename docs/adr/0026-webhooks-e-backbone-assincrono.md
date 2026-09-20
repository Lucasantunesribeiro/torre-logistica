# ADR 0026 — Webhooks: outbox transacional, fila no PostgreSQL e desistência visível

**Status:** aceito — Fase 17
**Decisores:** time técnico
**Relacionados:** [0007](./0007-storage-de-comprovantes-fora-do-banco.md), [0017](./0017-tempo-real-da-operacao.md), [0021](./0021-operacao-offline.md), [0025](./0025-api-de-integracao.md)

## Contexto

O sistema precisa avisar terceiros quando algo acontece com a entrega. O modo de falha a eliminar é
específico e traiçoeiro: gravar a entrega como concluída, tentar publicar o aviso **fora** da transação, o
processo cair no meio — e ninguém nunca saber que o aviso não saiu. O critério de aceite da fase é
exatamente esse: perder webhook não pode ser um fracasso silencioso.

## Decisão

### O evento nasce no mesmo commit do fato

Um `SaveChanges` que grava um evento da entrega grava também a linha correspondente no `outbox`, na mesma
transação. Não há caminho em que o fato exista sem o aviso registrado, porque o recolhimento acontece
dentro do próprio contexto de persistência — nenhum caso de uso precisa lembrar de fazer isso.

O corpo do evento carrega só o que já está em mãos no momento da gravação (identificador, situação,
instante). Buscar código humano ou endereço exigiria consulta no meio do `SaveChanges`; o assinante que
precisa de detalhe consulta a API com a credencial dele.

### A fila é o PostgreSQL, não o SQS — por enquanto

O ROADMAP cita SQS. **Não** foi criado recurso de nuvem: a fila é uma tabela, reservada com
`FOR UPDATE SKIP LOCKED`, e os processadores são serviços em segundo plano do próprio host. É a mesma
escolha do ADR 0007 para o storage — construir contra a abstração, trocar o adaptador na Fase 25, quando a
infraestrutura for decidida com custo na mesa.

`SKIP LOCKED` é o que torna isso uma fila de verdade: duas instâncias pegam lotes diferentes em vez de
disputar as mesmas linhas, e nenhuma fica esperando a outra.

### Duas etapas, dois temporizadores

Despachar (transformar mensagem em entregas, por assinante) é rápido e acontece só no banco. Entregar
depende de rede alheia e pode levar dez segundos por tentativa. Separá-los evita que um assinante lento
atrase a saída dos eventos de todas as outras organizações.

### Arrendamento em vez de transação longa

A reserva empurra a disponibilidade para a frente e **libera o banco**; o POST acontece fora da transação.
Manter a transação aberta durante a chamada HTTP prenderia conexão e trava pelo tempo do assinante.

A consequência é aceita e documentada: se o processo morrer entre o POST e a gravação do resultado, o
assinante receberá o evento duas vezes. Por isso cada entrega leva o identificador do evento num cabeçalho
— a deduplicação final é dele, e o contrato diz isso.

**Foi essa semântica que corrigiu um erro nosso de modelagem:** havia um índice único em
`(entrega, número da tentativa)`, que transformava a retentativa legítima — arrendamento vencido, processo
reiniciado — em erro de gravação. O número é ordinal de histórico, não invariante.

### Desistir é um estado, não um sumiço

Esgotadas as tentativas, a entrega vai para `Falhada` e **continua visível**, com todo o histórico de
tentativas. É a nossa carta na fila morta, e o único caminho de volta é o reenvio manual, que passa por
uma pessoa e entra na auditoria.

O backoff cresce depressa (30 s, 2 min, 8 min, 32 min, 2 h, 2 h): assinante fora do ar demora minutos ou
horas para voltar, e insistir de segundo em segundo transforma a indisponibilidade dele em carga nossa.

### A assinatura cobre o carimbo, e o segredo é cifrado

O cabeçalho é `t=<unix>,v1=<hmac>`, e o que se assina é `t.corpo`. Sem o carimbo dentro da assinatura,
quem interceptasse uma entrega poderia reenviá-la para sempre e ela continuaria válida.

O segredo é guardado **cifrado com AES-GCM**, não hasheado: diferente de senha e de chave de API, ele
precisa ser recuperável para assinar cada entrega. AES-GCM porque autentica junto com cifrar — conteúdo
adulterado no banco falha em vez de decifrar em silêncio.

### O destino é conferido resolvido, não pelo texto

URL de webhook é cadastrada pelo cliente e o POST sai da nossa rede. Sem trava, quem cadastra
`http://169.254.169.254/` usa o nosso servidor como procurador para alcançar o que não alcança de fora. O
cliente resolve o host e recusa loopback, faixas privadas, link-local e multicast — e **não segue
redirecionamento**, porque um 302 para endereço interno contornaria a checagem.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| SQS agora | recurso pago, e a decisão de infraestrutura é da Fase 25; a abstração deixa a troca barata |
| Publicar direto no commit, sem outbox | é exatamente o modo de falha que a fase existe para eliminar |
| Fila em memória, como a de recálculo de previsão | reinício do processo perderia avisos; ali a perda é recalculável, aqui não |
| Transação aberta durante o POST | prende conexão e trava pelo tempo do assinante |
| Guardar o segredo como hash | impossível: é preciso recuperá-lo para assinar |
| Apagar a entrega que esgotou tentativas | silêncio é o fracasso que a fase proíbe |
| Assinar só o corpo | entrega capturada valeria para sempre em reenvio |

## Como a decisão é verificada

| Afirmação | Prova |
|---|---|
| Evento no mesmo commit | `OEventoNasceNoMesmoCommitDoFatoMesmoSemAssinante` — existe mesmo sem assinante |
| Chega assinado e identificável | `OEventoChegaAoAssinanteAssinadoEComIdentificadorParaDeduplicar`, conferindo o HMAC com o segredo emitido e recusando segredo errado |
| Despacho repetido não duplica | `DespacharDuasVezesNaoGeraEntregaRepetida` |
| Retry, backoff e desistência visível | `AssinanteQuebradoEhRetentadoEDepoisDesisteDeFormaVisivel` |
| Tempo limite conta como falha | `AssinanteQueNaoRespondeNoPrazoContaComoFalhaERetenta` |
| Reenvio manual | `ReenvioManualColocaDeVoltaNaFilaEEntrega` e `ReenviarEntregaQueNaoFalhouEhRecusado` |
| Só eventos assinados, e nada após revogar | `AssinaturaRecebeSoOsEventosQueEscolheuENadaDepoisDeRevogada` |
| Segredo protegido | `OSegredoNaoApareceEmConsultaNemNaAuditoria` — nem na resposta, nem na trilha, nem legível no banco |
| Histórico imutável | `HistoricoDeTentativaEhSomenteInsercao` |
| Tenant | `OutraOrganizacaoNaoEnxergaAssinaturaNemEntrega` |
| SSRF | `DestinoDeWebhookTestes` — loopback, privadas, link-local e metadados recusados |
