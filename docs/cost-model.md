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

### E por isso são dois processos, não um

A primeira versão deste documento tratava API e trabalho de fundo como a mesma coisa. Estava errado: são
responsabilidades com ciclos de vida diferentes, e juntá-las amarrava as duas — a API não poderia escalar
sem duplicar o trabalho de fundo, e o trabalho de fundo não poderia parar sem derrubar a borda.

| | API | Workers |
|---|---|---|
| Por que fica viva | conexão persistente do console | os laços só existem enquanto o processo vive |
| Ingress | HTTPS público | **nenhum** |
| Tamanho | 0,5 vCPU / 1 GiB | 0,25 vCPU / 0,5 GiB |

O custo ocioso passa a ser de **dois** contêineres pequenos em vez de um médio. Na prática isso muda
pouco: o segundo é metade do primeiro, e o total de CPU reservada é **0,75 vCPU** — menos que uma única
instância de 1 vCPU que seria necessária se tudo dividisse o mesmo processo sob carga.

## 3. Onde o dinheiro vai

Em qualquer fornecedor, o custo desta aplicação tem quatro parcelas. A ordem importa: a primeira é a maior.

| Parcela | O que dirige | Quantidade medida |
|---|---|---|
| **Banco gerenciado** | instância sempre ligada + armazenamento + backup | ~30 GB em regime, crescendo ~1 GB/dia até o teto da retenção |
| **Compute** | duas instâncias pequenas sempre vivas | **0,75 vCPU e 1,5 GiB no total**: 0,5/1 GiB na API e 0,25/0,5 GiB nos workers |
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

### Escolhida: Azure Container Apps + PostgreSQL Flexible Server

Decisão de produto, tomada por quem paga a conta. O peso do desempate foi a diversificação do portfólio:
a Central Antifraude já é AWS, e o ROADMAP abre esta fase proibindo copiar aquela arquitetura.

O detalhamento está em [ADR 0034](./adr/0034-hospedagem.md), e a infraestrutura escrita — **validada e não
aplicada** — em [`infra/`](../infra/README.md).

## 5. O que já está construído e exercitado

As duas imagens foram construídas e postas para rodar nesta máquina, contra o banco de desenvolvimento
real — não são template.

| Prova | API | Workers |
|---|---|---|
| Tamanho | 196 MB | **155 MB** (imagem `runtime`: não há servidor HTTP) |
| Usuário | UID 1654, sem privilégio | UID 1654, sem privilégio |
| Portas publicadas | 8080 | **nenhuma** |
| Com banco real | `/health/ready` **200**; migrations aplicadas no arranque | fica vivo e assume os laços |
| Sem banco | `/health/live` **200**, `/health/ready` **503** | encerra de propósito (falha rápida) |
| Laços de fundo | **0 ocorrências** de "Falha na rodada" | é quem as registra |

A última linha é a prova da separação: a imagem anterior, com os laços dentro da API, enchia o log de
`Falha na rodada de despacho do outbox` quando o banco estava fora. A atual não registra nenhuma.

E a linha do `/health/live` é a resiliência da Fase 22 onde ela importa: num arranque em que o banco ainda
não subiu, o contêiner da API não entra em ciclo de reinício.

## 6. O que falta, e o que depende de autorização

| Item | Situação |
|---|---|
| Escolha do fornecedor | ✅ feita: Azure Container Apps + PostgreSQL Flexible Server |
| IaC | ✅ escrita e **validada**: `bicep build` e `bicep lint` sem achados; 12 recursos |
| `what-if` | ❌ exige assinatura autenticada e grupo de recursos existente — nenhum dos dois existe |
| Provisionamento | **exige aprovação explícita** — nenhum recurso foi criado |
| Deploy | **exige autorização explícita**, na mensagem que o pedir |
| Custo mensal real | sai da calculadora do Azure com as quantidades da seção 3 |
| Medição com latência de rede | `docs/performance.md` foi medido com aplicação e banco na mesma máquina |
