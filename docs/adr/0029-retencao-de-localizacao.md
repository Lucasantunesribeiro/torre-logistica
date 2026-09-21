# ADR 0029 — Retenção de localização: prazo configurável, corte pelo recebimento e limpeza em lotes

**Status:** aceito — Fase 20
**Decisores:** time técnico
**Relacionados:** [0015](./0015-ingestao-de-localizacao.md), [0016](./0016-geofence-de-destino.md), [0020](./0020-pwa-do-motorista.md), [0024](./0024-rastreamento-publico.md)

## Contexto

O histórico bruto de posições é a tabela que mais cresce do sistema — na meta de referência do projeto,
500 motoristas a uma posição a cada 15 segundos produzem cerca de 2,8 milhões de linhas por dia — e é
também o dado mais sensível que ele guarda: o deslocamento de uma pessoa real, minuto a minuto.

Até aqui esse histórico não tinha prazo. O `CLAUDE.md` (seção 22) já proibia retenção infinita, e a
migration da Fase 7 deixou o `DELETE` liberado no gatilho append-only exatamente para este momento. Falta
decidir **o que apagar, por qual critério de tempo e com que mecânica**.

## Decisão

### Prazo é configuração, com piso

`Torre:Retencao:PosicoesBrutas`, padrão **30 dias**.

O prazo não é constante de código porque não é decisão de quem programa: a finalidade legítima de guardar
o rastro dura o tempo de auditar uma contestação de entrega, e esse tempo é do negócio. Trinta dias cobrem
a contestação típica sem virar arquivo permanente de deslocamento de pessoas.

Existe um **piso de um dia**. Retenção zerada por erro de digitação apagaria a posição no instante
seguinte ao envio e cegaria a torre sobre a operação em curso — o piso transforma o engano em
comportamento previsível em vez de incidente.

### O corte é pelo recebimento, não pela captura

`recebida_em` é carimbo do servidor; `capturada_em` é carimbo do aparelho, e aparelho é cliente. Com o
corte na captura, um relógio errado — ou um cliente mal-intencionado — decidiria por nós quando o dado sai
ou fica: carimbo antigo apagaria cedo demais, carimbo futuro prolongaria a guarda além do prazo.

Isso exigiu índice novo (`ix_posicoes_recebida_em`): o índice existente é `(motorista_id, capturada_em)` e
não serve a um filtro que não menciona o motorista.

### Só o rastro vence

| Dado | Vence? | Por quê |
|---|:---:|---|
| `posicoes` (histórico bruto) | sim | é o caminho percorrido — o rastro da pessoa |
| `posicoes_atuais` (projeção) | não | uma linha por motorista; apagá-la cegaria o mapa sem ganho de privacidade |
| Eventos de geofence, chegada, conclusão | não | explicam a operação e somem com a entrega, não com o rastro |

### Limpeza em lotes, com teto por rodada

`DELETE` por `ctid`, em lotes de 5.000 (`TamanhoDoLote`), no máximo 20 lotes por rodada
(`LotesPorRodada`), a cada 6 horas (`Intervalo`).

Um `DELETE` único sobre meses de telemetria seguraria bloqueio e encheria o log de transação: a limpeza
viraria o incidente em vez de evitá-lo. Cada lote confirma sozinho — uma interrupção no meio deixa o resto
para a próxima rodada em vez de desfazer tudo.

Quando a rodada termina no teto com dado vencido restante, o serviço registra **aviso**. Se isso repetir,
a limpeza não está acompanhando o volume de entrada, e é melhor descobrir por um aviso do que por disco
cheio.

### A limpeza atravessa organizações

O prazo é do sistema e a varredura é por idade. Filtrar por organização faria a rodada percorrer o mesmo
índice uma vez por tenant para chegar ao mesmo conjunto de linhas. Isso tem consequência nos testes, e
está registrado: um teste de retenção precisa operar numa faixa de tempo que só ele ocupa.

### O serviço decide rodar em tempo de execução

O registro sempre adiciona o `BackgroundService`; quem decide rodar é o próprio serviço, lendo
`IOptions` no início. Ler a opção **no registro** parece equivalente e não é: naquele momento a
configuração do host ainda não está completa, e uma chave que chega depois é ignorada em silêncio.

Foi exatamente o que aconteceu durante esta fase — ver "Como a decisão é verificada".

## Consequências

Depois de 30 dias não é possível reconstituir o trajeto de uma entrega antiga. Isso é a intenção, não um
efeito colateral: quem precisar de prova além desse prazo precisa dela em evento operacional (chegada,
geofence, conclusão), que continua guardado.

Particionamento temporal não está ativo. Não é necessário no volume atual, e nada no desenho o impede
depois — a limpeza por lote continua válida sobre tabela particionada, e um dia pode virar `DROP
PARTITION`, que é mais barato.

O índice novo tem custo de escrita na tabela de maior volume de inserção do sistema. É um índice btree
sobre coluna monotônica, cujo custo de manutenção fica na página mais à direita; é o preço de não varrer
a tabela inteira a cada rodada.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Corte pela captura | o cliente decidiria o prazo de guarda pelo relógio dele |
| Prazo fixo no código | prazo de retenção é decisão de negócio, e muda sem recompilar |
| Sem piso de prazo | um zero na configuração apagaria a operação em curso |
| `DELETE` único por rodada | bloqueio longo e log de transação inflado; a limpeza viraria o incidente |
| Apagar também a posição atual | cegaria o mapa sobre o motorista parado, sem ganho real de privacidade |
| Arquivar em armazenamento frio antes de apagar | o ROADMAP dispensa multi-tier; e mover o dado não é minimizá-lo |
| Particionar por tempo agora | otimização sem medição, e o `DROP PARTITION` só compensa em volume que ainda não existe |
| Limpeza por organização | mesma varredura repetida por tenant, sem ganho |

## Como a decisão é verificada

| Afirmação | Prova |
|---|---|
| O rastro vencido sai | `ApagaORastroVencidoESemCegarATorre` |
| A torre não fica cega | o mesmo teste: depois do expurgo, a posição atual ainda responde |
| O recente fica | o mesmo teste: posição enviada após o expurgo sobrevive à rodada seguinte |
| Dado no prazo de outra organização não é alcançado | `NaoApagaOQueAindaEstaNoPrazoDeOutraOrganizacao` |
| O lote é respeitado e o teto avisa | `ParaNoTetoDaRodadaEAvisaQueRestouTrabalho` |
| O piso protege a operação do dia | `PrazoAbaixoDoPisoNaoApagaAOperacaoDoDia` |

**Defeito encontrado pela própria fase:** o primeiro desenho decidia ligar o processador lendo a
configuração no momento do registro. Com isso, `LimparEmSegundoPlano=false` não tinha efeito, e a rodada de
fundo apagou as posições de um teste que a dava por desligada. O mesmo padrão estava no processador de
webhooks desde a Fase 17 — `ProcessarEmSegundoPlano` também era inócuo — e foi corrigido junto.
