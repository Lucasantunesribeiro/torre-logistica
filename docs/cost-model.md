# Modelo de custo e gate de arquitetura

> Documento da Fase 25. Ele existe para uma decisão ser tomada com número em vez de hábito — o ROADMAP é
> explícito: *não copiar automaticamente a arquitetura Lambda da Central Antifraude*.
>
> **Nada foi provisionado.** Criar recurso com risco de cobrança exige aprovação, e a escolha do
> fornecedor é decisão de produto, não técnica reversível.

## 1. Os requisitos que decidem

Quatro vêm de medição própria (Fase 22), não de estimativa:

| Requisito | Valor | Origem |
|---|---|---|
| Pico de ingestão | 33 posições/s sustentadas | meta do ROADMAP, medida em `docs/performance.md` |
| CPU necessária no pico | baixa: p50 de 9,7 ms por requisição com a tabela cheia | medida |
| Crescimento do banco | **~1 GB por dia**, dos quais metade é índice | medido |
| Banco em regime | **~30 GB**, pela retenção de 30 dias | medido × política da Fase 20 |
| Conexão persistente | **obrigatória** — SignalR para o console | ADR 0017 |
| Trabalho periódico | outbox a cada 5 s, previsão a cada 1 min, alertas, retenção a cada 6 h | Fases 9, 10, 17, 20 |
| Banco | PostgreSQL **com PostGIS** | ADR 0002 |
| Objeto | privado, com URL assinada | ADR 0007 |

## 2. A descoberta que elimina metade das opções

O ROADMAP pede "scale-to-zero **ou** baixo idle cost". Para este sistema, **scale-to-zero não serve**.

Não é preferência: o processo que atende HTTP é o mesmo que hospeda os serviços de segundo plano. Um
serviço que dorme quando ninguém acessa **para de avaliar SLA, de despachar webhook e de apagar rastro
vencido**. A entrega que entraria em risco às 3h da manhã só seria marcada quando alguém abrisse o
console — e o alerta que chega tarde não é alerta, é histórico.

Some-se a isso a conexão persistente: dormir derruba todo console conectado.

**Conclusão do gate:** o caminho é **baixo custo ocioso com processo sempre vivo**, não escala a zero.

Duas saídas seriam possíveis no futuro, e nenhuma se justifica agora:

- separar os workers num processo próprio, deixando a API dormir — mas aí o processo dos workers é que
  fica sempre vivo, e passam a ser **dois** serviços a pagar em vez de um;
- mover o trabalho periódico para um agendador externo — mais partes móveis para economizar o custo de um
  contêiner pequeno.

## 3. Onde o dinheiro vai

Em qualquer fornecedor, o custo desta aplicação tem quatro parcelas. A ordem importa: a primeira é a maior.

| Parcela | O que dirige | Quantidade medida |
|---|---|---|
| **Banco gerenciado** | instância sempre ligada + armazenamento + backup | ~30 GB em regime, crescendo ~1 GB/dia até o teto da retenção |
| **Compute** | uma instância pequena sempre viva | 0,5 vCPU e 1 GB bastam para 33 req/s com p50 de 10 ms |
| **Tráfego** | telemetria de entrada e páginas de saída | ~17 GB/mês de entrada no pico da meta; saída menor |
| **Objeto** | comprovantes | até 5 MB por entrega concluída com foto |

Observabilidade e filas **não aparecem** porque não são serviços contratados: a telemetria só sai do
processo quando alguém aponta um coletor (ADR 0030), e a fila é o próprio PostgreSQL (ADR 0026).

### Por que não há tabela de preços aqui

Preço unitário de nuvem muda, varia por região e depende de compromisso de uso. Uma tabela cravada neste
documento estaria errada em poucos meses e — pior — alguém decidiria com base nela sem reconferir.

O que este documento fixa é o que **não** muda: as quantidades medidas acima. Com elas, a calculadora
oficial de qualquer fornecedor dá o número do dia, e a comparação continua válida.

## 4. Opções consideradas

### Descartadas

| Opção | Por quê |
|---|---|
| **AWS Lambda** | conexão persistente não cabe no modelo de invocação; o ROADMAP alerta explicitamente contra copiar esta escolha |
| **AWS App Runner** | suporte a WebSocket **não confirmado na documentação oficial**; para um requisito obrigatório, "não documentado" já basta para não escolher |
| **Aurora Serverless v2** | o mínimo de capacidade cobrado continuamente custa mais que uma instância pequena dedicada, para uma carga que não tem pico sazonal |
| **ECS Fargate + ALB** | o balanceador sozinho custa mais que todo o resto do compute deste projeto |
| **Kubernetes gerenciado** | plano de controle pago e complexidade operacional que um monólito modular não pede |

### Candidatas

| Opção | A favor | Contra |
|---|---|---|
| **Azure Container Apps** + PostgreSQL Flexible Server | WebSocket suportado; escala configurável com mínimo de 1 réplica (baixo ocioso); PostGIS disponível; TLS e domínio inclusos | ecossistema diferente do resto do portfólio, se ele for AWS |
| **AWS Lightsail Containers** + RDS PostgreSQL | preço fixo e previsível, que é exatamente o que um portfólio precisa; TLS incluso | menos recursos gerenciados em volta; Lightsail é um canto próprio da AWS |
| **AWS EC2 pequena** + RDS | controle total e custo baixo | manutenção do sistema operacional vira trabalho recorrente — o que um projeto de portfólio não deveria pagar |

O empate técnico é real: as três atendem WebSocket, PostGIS e processo sempre vivo. O desempate é de
**produto** — qual nuvem este portfólio quer demonstrar — e de quanto se aceita pagar por mês.

## 5. O que já está pronto, independente da escolha

A imagem do contêiner é neutra e **foi construída e testada**:

| Prova | Resultado |
|---|---|
| Imagem construída | 196 MB, Alpine (o projeto compila com `InvariantGlobalization`, então não carrega ICU) |
| Processo sem privilégio | roda como UID 1654, não root |
| `GET /health/live` com o banco inacessível | **200** — o processo está vivo, e a sonda não mente sobre isso |
| `GET /health/ready` com o banco inacessível | **503** — a dependência crítica está fora, e a sonda diz |
| Serviços de fundo sem banco | registram erro a cada rodada e **não derrubam o processo** |

Essa última linha é a resiliência da Fase 22 aparecendo onde ela importa: num arranque em que o banco
ainda não subiu, o contêiner não entra em ciclo de reinício.

## 6. O que falta, e o que depende de autorização

| Item | Situação |
|---|---|
| Escolha do fornecedor | **decisão de produto**, com implicação financeira e de lock-in |
| IaC | escrita depois da escolha; escrever antes seria adivinhar |
| Provisionamento | **exige aprovação explícita** — nenhum recurso foi criado |
| Deploy | **exige autorização explícita**, na mensagem que o pedir |
| Custo mensal real | sai da calculadora do fornecedor escolhido, com as quantidades da seção 3 |
| Medição com latência de rede | `docs/performance.md` foi medido com aplicação e banco na mesma máquina; em produção há rede no meio |
