# Modelo de custo e gate de arquitetura

> Documento da Fase 25. Ele existe para uma decisão ser tomada com número em vez de hábito — o ROADMAP é
> explícito: *não copiar automaticamente a arquitetura Lambda da Central Antifraude*.

## 0. Dois ambientes, duas restrições — leia isto primeiro

Este documento descreve a **arquitetura de produção**. Ela foi provisionada no Azure real, validada, e
**destruída por decisão explícita de custo**. O que muda é o destino, não o desenho.

| | Arquitetura de **produção** | Arquitetura de **demonstração** |
|---|---|---|
| Situação | ✅ desenhada, validada e exercitada contra o Azure real | ✅ desenhada e validada localmente; **nada provisionado** |
| Desenho | Azure Container Apps + PostgreSQL Flexible Server + Blob privado + identidade gerenciada | uma máquina Ampere A1 Always Free da Oracle, com quatro contêineres |
| Onde está escrita | `infra/` (Bicep) | `infra-demo/` (Terraform + compose) |
| Custo | US$ 35–42/mês, detalhado abaixo | **US$ 0,00/mês** — seção 11 |
| Para que serve | mostrar como o sistema seria operado de verdade | pôr a demonstração no ar para um avaliador |

> **A arquitetura de demonstração não é a recomendada para um cliente real.** Ela coloca banco,
> aplicação e proxy na mesma máquina, sem réplica e sem isolamento de falha. É a forma correta de
> publicar um portfólio com restrição de custo zero, e seria a forma errada de atender uma operação
> logística de verdade. Os dois diretórios existem lado a lado para que a diferença seja visível.

A restrição de custo zero é inegociável e chegou depois de o ambiente já estar parcialmente provisionado.
Por isso **nada do Azure continua no ar**: nenhum recurso da Torre Logística existe, e o custo recorrente
dela na assinatura é **zero**. A destruição está documentada no `ROADMAP.md`.

O resto deste documento continua válido como o modelo de custo da arquitetura de produção — e como o
registro de quanto ela custaria, que é justamente o número que motivou a restrição.

### O que o provisionamento real ensinou, e os números não diziam

| Descoberta | Efeito |
|---|---|
| East US é impossível nesta assinatura | duas restrições independentes: PostgreSQL bloqueado na região **e** uma Azure Policy de regiões permitidas |
| Regiões viáveis: `canadacentral`, `southafricanorth` | Canada Central custa **US$ 41,78/mês**, +1,83 sobre o plano |
| Container Apps cria ambientes "express" por padrão | ambiente express **recusa** identidade gerenciada do sistema para puxar do registro; a correção é identidade de usuário |



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

O trabalho periódico não depende de requisição nenhuma. Um serviço que dorme quando ninguém acessa **para
de avaliar SLA, de despachar webhook e de apagar rastro vencido**. A entrega que entraria em risco às 3h da
manhã só seria marcada quando alguém abrisse o console — e o alerta que chega tarde não é alerta, é
histórico.

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
| Tamanho | 0,25 vCPU / 0,5 GiB (medido, seção 6) | 0,25 vCPU / 0,5 GiB |

## 3. As quantidades permanentes

O que muda é preço unitário. O que não muda são estas quantidades — elas saem de medição, não de palpite,
e é com elas que qualquer calculadora dá o número do dia.

| Quantidade | Valor | De onde vem |
|---|---|---|
| Compute sempre ligado | **0,5 vCPU e 1 GiB** somados | 0,25/0,5 GiB em cada um dos dois processos |
| Horas por mês | 730 h = **2.628.000 s** por réplica | dois processos com `minReplicas: 1` |
| Banco | 1 instância `Standard_B1ms` (1 vCore, 2 GiB), **sempre ligada** | ADR 0034 |
| Armazenamento do banco | **64 GiB** provisionados | ~30 GB em regime + folga de crescimento |
| Retenção de backup | 7 dias | padrão do Flexible Server |
| Retenção de log | 30 dias | `main.bicep` |
| Imagens no registro | 2 imagens de ~200 MB | 196 MB (API) + 155 MB (workers) |
| Comprovantes | até 5 MB por entrega concluída com foto | ADR 0007 |
| Tráfego de entrada | ~17 GB/mês no pico da meta | entrada **não é cobrada** |

## 4. Região: East US era a escolha — e é impossível nesta assinatura

Comparação feita com os preços de varejo do mesmo dia, para os dois candidatos pedidos.

| Item | East US | Brazil South | Diferença |
|---|---:|---:|---:|
| PostgreSQL B1ms (hora) | US$ 0,017 | US$ 0,035 | **+106 %** |
| Armazenamento do banco (GB/mês) | US$ 0,115 | US$ 0,2185 | **+90 %** |
| Backup LRS além da franquia (GB/mês) | US$ 0,095 | US$ 0,095 | igual |
| Container Apps — vCPU ativo (s) | US$ 0,000024 | US$ 0,000024 | igual |
| Container Apps — vCPU ocioso (s) | US$ 0,000003 | US$ 0,000003 | igual |
| Container Apps — memória (GiB·s) | US$ 0,000003 | US$ 0,000003 | igual |
| Blob Hot LRS (GB/mês) | US$ 0,0208 | US$ 0,0326 | +57 % |
| Log Analytics — ingestão (GB) | US$ 2,30 | US$ 4,60 | **+100 %** |
| Container Registry Basic (dia) | US$ 0,1666 | US$ 0,1666 | igual |
| Saída para internet acima de 100 GB (GB) | US$ 0,08 | US$ 0,12 | +50 % |

O compute do Container Apps — que é a maior parcela variável — **custa o mesmo nas duas**. A diferença se
concentra exatamente onde o custo é fixo: o banco, que é a parcela que roda 730 horas por mês sem
interrupção. Brazil South custa **de 45 % a 50 % a mais no total mensal**, entre US$ 20 e US$ 49 conforme o
cenário.

Disponibilidade não desempata **entre essas duas** — mas desempatou contra East US quando o
provisionamento foi tentado de verdade:

> `az postgres flexible-server list-skus --location eastus`
> → *"Provisioning is restricted in this region. Please choose a different region."*

E uma segunda peneira, que só apareceu no `what-if`: a Azure Policy `Allowed resource deployment regions`
da assinatura permite apenas `canadacentral`, `eastus`, `eastus2`, `southafricanorth`, `southcentralus`.

Cruzando as duas, sobraram **`canadacentral`** (US$ 41,78/mês) e **`southafricanorth`** (US$ 45,54). O
provisionamento real usou Canada Central. A comparação abaixo com Brazil South continua válida como
raciocínio; o que mudou foi o conjunto de regiões disponíveis.

Latência desempata **a favor de Brazil South**, e por muito: de São Paulo são ~10–20 ms contra ~110–140 ms
para East US. Mas o que essa latência atrasa aqui é a percepção de um avaliador abrindo o mapa, não uma
decisão operacional — e 130 ms num console que atualiza por evento não é uma diferença que se note sem
cronômetro. Entre pagar ~US$ 20/mês por isso num ambiente de demonstração e não pagar, não pagar vence.

> A decisão vale **para o ambiente de portfólio**. Uma implantação comercial atendendo operação brasileira
> real inverteria o peso: aí a latência entra na jornada do motorista em rota, não na primeira impressão de
> um recrutador, e Brazil South passa a ser a escolha natural.

## 5. Estimativa mensal

**Preços consultados em 21/09/2026**, em dólar, pela [Azure Retail Prices
API](https://prices.azure.com/api/retail/prices) (`currencyCode=USD`), conferidos contra as páginas
oficiais de preço e as documentações de faturamento citadas abaixo. Preço unitário de nuvem muda: a data
acima é parte do número.

### Franquias aplicadas

| Franquia | Valor | Fonte |
|---|---|---|
| Container Apps | 180.000 vCPU·s, 360.000 GiB·s e 2 milhões de requisições por assinatura/mês | docs de billing do Container Apps |
| Log Analytics | 5 GB/mês por conta de cobrança; **31 dias** de retenção inclusos | página de preços do Azure Monitor |
| Backup do PostgreSQL | 100 % do armazenamento provisionado, ou seja **64 GB** | docs de backup do Flexible Server |
| Saída para internet | primeiros 100 GB/mês | tabela de banda |
| Sonda de saúde | requisições de health probe **não são cobradas** | docs de billing do Container Apps |

### O que separa "ativo" de "ocioso"

Vale metade da conta variável, então vale escrever: uma réplica só é cobrada na tarifa ociosa — **oito
vezes menor** em vCPU — quando está no mínimo de réplicas, não atende requisição HTTP, usa menos de 0,01
vCPU e recebe menos de 1.000 bytes por segundo. Memória custa igual nos dois estados.

Os workers acordam a cada 5 segundos para olhar o outbox. Cada rodada é curta, mas é exatamente esse tipo
de atividade que pode tirar a réplica do estado ocioso. O cenário C existe para cobrir a hipótese
pessimista de que ela nunca entre.

### Três cenários, em East US

| Componente | A — demo ociosa | B — uso moderado | C — limite conservador |
|---|---:|---:|---:|
| PostgreSQL B1ms (730 h) | 12,41 | 12,41 | 12,41 |
| Armazenamento do banco (64 GiB) | 7,36 | 7,36 | 7,36 |
| Backup acima da franquia | 0,00 | 0,00 | 3,42 |
| Container Apps — API + workers | 10,21 | 14,47 | 34,02 |
| Container Apps — requisições | 0,00 | 0,00 | 1,20 |
| Container Registry Basic | 5,07 | 5,07 | 5,07 |
| Blob dos comprovantes | 0,00 | 0,04 | 0,42 |
| Log Analytics | 0,00 | 0,00 | 23,00 |
| Key Vault | 0,06 | 0,60 | 3,00 |
| Saída para internet | 0,00 | 0,00 | 12,00 |
| **Total (US$/mês)** | **35,11** | **39,95** | **101,89** |

O que cada cenário supõe:

| | A | B | C |
|---|---|---|---|
| API em estado ativo | 0 % | 33 % (8 h/dia) | 100 % |
| Workers em estado ativo | 0 % | 25 % | 100 % |
| Requisições | 20 mil | 500 mil | 5 milhões |
| Log ingerido | 0,5 GB | 4 GB | 15 GB |
| Backup acumulado | 15 GB | 45 GB | 100 GB |
| Saída | 5 GB | 40 GB | 250 GB |

**Faixa esperada: US$ 35 a US$ 40 por mês.** O cenário C não é previsão — é teto: ele supõe as duas
réplicas nunca ociosas, 15 GB de log e 250 GB de saída ao mesmo tempo, o que uma demonstração de portfólio
não produz.

### Quem domina o custo

Nos cenários realistas, três parcelas somam **90 %** da conta, e todas as três são fixas:

1. **PostgreSQL** (US$ 19,77 fixos): instância mais 64 GiB. Não varia com uso nenhum — é a maior parcela.
2. **Container Apps** (US$ 10–14): as duas réplicas que nunca dormem. É o preço da decisão da seção 2.
3. **Container Registry** (US$ 5,07 fixos).

A única parcela que pode **explodir** é o Log Analytics: cada GB além de 5 custa US$ 2,30, e o cenário C
sozinho traz US$ 23. Ela é a que mais depende de decisão nossa, não de tráfego — nível de log e o que se
manda para o workspace. Vale medir na primeira semana.

Como o banco passou a ser a maior parcela, é nele que mora a próxima economia possível: 64 GiB é folga
para um regime de ~30 GB, e cair para 32 GiB devolveria US$ 3,68/mês. Não foi feito porque reduzir
armazenamento no Flexible Server **não é reversível** — só se cresce — e 2 GiB de índice a mais por dia de
operação consomem essa folga depressa.

## 6. Sizing da API: 0,25 vCPU / 0,5 GiB, e por quê

**A Fase 22 não respondia essa pergunta.** Ela mediu latência, plano de consulta e crescimento do banco —
**não** mediu CPU nem memória do processo da API. Usar aqueles números para justificar o sizing seria citar
uma medição que não existe. Então a medição foi feita agora.

### Como foi medido

A imagem `torre-logistica-api:separada` — a mesma que iria para o Azure — rodou contra PostgreSQL + PostGIS
real, com `docker run --cpus … --memory …` impondo exatamente o teto do Container App. Sobre ela, duas
cargas ao mesmo tempo:

- o **simulador** encenando as seis histórias da demonstração (ADR 0006), que é a carga de escrita real;
- **seis consoles simultâneos** lendo `/api/entregas`, `/api/alertas`, `/api/motoristas` e `/api/rotas` em
  rodízio, a 4 requisições por segundo somadas, durante 100 segundos.

### O que saiu

| | **0,25 vCPU / 0,5 GiB** | 0,5 vCPU / 1 GiB |
|---|---:|---:|
| Requisições atendidas | 2.215 | 2.219 |
| Erros | **nenhum** | nenhum |
| p50 | **8,0 ms** | 9,6 ms |
| p95 | **51,0 ms** | 52,5 ms |
| p99 | **102,1 ms** | 142,5 ms |
| Pior caso | 563,8 ms | 331,0 ms |
| Memória em uso | **181 MiB** (35 % do teto) | 184 MiB (18 % do teto) |
| Demonstração completa | 101 s, 6/6 histórias | 94 s, 6/6 histórias |
| Arranque até `/health/ready` 200 | 18 s | 9 s |
| Morto por falta de memória | **não** | não |

### A leitura

A memória usada é **a mesma nos dois** — 182 MiB — porque o processo usa o que precisa, não o que tem. Com
teto de 512 MiB isso deixa 2,8× de folga. Dobrar o teto não reduziu latência nenhuma: p50 e p95 são
indistinguíveis, e o p99 até piorou no tamanho maior, o que é ruído.

O único preço real da redução é o **arranque, que dobra** (9 s → 18 s), porque compilar o código na
primeira execução é o momento em que a CPU importa. Com `minReplicas: 1`, isso acontece em deploy e em
reinício de plataforma — não numa visita.

**Decisão: 0,25 vCPU / 0,5 GiB**, aplicado em `infra/main.bicep`. Economiza:

| | A | B | C |
|---|---:|---:|---:|
| API 0,5 vCPU / 1 GiB | 41,02 | 50,47 | 121,60 |
| **API 0,25 vCPU / 0,5 GiB** | **35,11** | **39,95** | **101,89** |
| Economia | 5,91 | 10,52 | 19,71 |

São 12 % a 21 % da conta, sem degradação mensurável na carga que a demonstração produz.

### Quando subir de volta

Voltar é uma linha em `infra/main.bicep`. Os gatilhos, medidos no ambiente publicado:

- p95 do console acima de ~150 ms com o simulador rodando;
- memória passando de ~380 MiB (75 % do teto), o que costuma vir de muitas conexões SignalR abertas;
- qualquer reinício por falta de memória.

O que esta medição **não** cobre: dezenas de conexões SignalR simultâneas (os seis clientes eram HTTP),
latência de rede entre aplicação e banco — aqui os dois estavam na mesma máquina — e o alvo de 33
posições/s da Fase 22, que é meta de ingestão, não carga de demonstração.

## 7. Opções consideradas

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
| **Azure Container Apps** + PostgreSQL Flexible Server | WebSocket suportado; mínimo de 1 réplica com tarifa ociosa; PostGIS disponível; TLS e domínio inclusos | ecossistema diferente do resto do portfólio, se ele for AWS |
| **AWS Lightsail Containers** + RDS PostgreSQL | preço fixo e previsível; TLS incluso | menos recursos gerenciados em volta; Lightsail é um canto próprio da AWS |
| **AWS EC2 pequena** + RDS | controle total e custo baixo | manutenção do sistema operacional vira trabalho recorrente |

### Escolhida: Azure Container Apps + PostgreSQL Flexible Server, em East US

Decisão de produto, tomada por quem paga a conta. O peso do desempate foi a diversificação do portfólio: a
Central Antifraude já é AWS, e o ROADMAP abre esta fase proibindo copiar aquela arquitetura.

O detalhamento está em [ADR 0034](./adr/0034-hospedagem.md), e a infraestrutura escrita — **validada e não
aplicada** — em [`infra/`](../infra/README.md).

### O registro de imagens, e a alternativa que não foi escolhida

O preflight encontrou uma inconsistência: o workflow de deploy publicava em `<registro>.azurecr.io` e o
template não criava registro nenhum. A infraestrutura descrita não bastava para o deploy descrito.

Corrigido com um **Container Registry Basic** (US$ 5,07/mês). A alternativa era publicar as imagens
públicas no `ghcr.io`, que custaria zero — nada de secreto vai na imagem, porque segredo vem do ambiente e
do cofre. Perdeu por US$ 5: manter registro privado e identidade gerenciada é um caminho a menos para
explicar e um segredo a menos para vazar, e o workflow já estava escrito assim.

## 8. O que já está construído e exercitado

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

## 9. Como desligar tudo e parar a cobrança

Enquanto o ambiente estiver de pé há custo fixo: contêiner e banco não dormem. Três níveis, do reversível
ao definitivo.

### Reduzir sem apagar (volta em minutos)

```bash
# Os dois processos param de existir como réplica; o ambiente e o banco continuam.
az containerapp update -g <grupo> -n <prefixo>-api     --min-replicas 0 --max-replicas 0
az containerapp update -g <grupo> -n <prefixo>-workers --min-replicas 0 --max-replicas 0
```

Zera a parcela de Container Apps (US$ 16–54). **Não** zera o banco, o armazenamento nem o registro — e o
sistema deixa de avaliar SLA, despachar webhook e apagar rastro vencido, que é exatamente o que a seção 2
diz não fazer em operação normal. Serve para pausar uma demonstração, não para operar.

### Parar o banco (até 7 dias)

```bash
az postgres flexible-server stop -g <grupo> -n <prefixo>-pg
```

Suspende a cobrança de compute do banco (US$ 12,41). Armazenamento e backup continuam cobrados, e o Azure
religa o servidor sozinho depois de 7 dias.

### Remover tudo (não tem volta)

```bash
az group delete --name <grupo> --yes
```

Apaga os 17 recursos de uma vez e encerra **toda** a cobrança. Três avisos:

1. **Os backups do banco vão junto** e não são recuperáveis.
2. **O cofre fica em soft delete por 7 dias** e o nome continua reservado nesse período; para reaproveitar
   o mesmo nome antes disso, use `az keyvault purge --name <prefixo>-cofre`. O cofre em soft delete não é
   cobrado.
3. Confira a fatura no dia seguinte: recurso apagado no meio do mês ainda gera linha proporcional.

Para checar antes de apagar o que exatamente será destruído:

```bash
az resource list -g <grupo> -o table
```

## 10. O que falta, e o que depende de autorização

| Item | Situação |
|---|---|
| Escolha do fornecedor | ✅ feita: Azure Container Apps + PostgreSQL Flexible Server |
| Região | ✅ **East US**, pela seção 4 |
| IaC | ✅ escrita e **validada**: `bicep build` e `bicep lint` sem achados; 17 recursos |
| Estimativa de custo | ✅ preços de 21/09/2026; faixa esperada US$ 35–40/mês |
| `what-if` | ❌ exige assinatura autenticada e grupo de recursos existente — nenhum dos dois existe |
| Provisionamento | **exige aprovação explícita** — nenhum recurso foi criado |
| Deploy | **exige autorização explícita**, na mensagem que o pedir |
| Medição com latência de rede | `docs/performance.md` foi medido com aplicação e banco na mesma máquina |

---

## 11. A demonstração: US$ 0,00, e como isso é garantido

A decisão está no [ADR 0035](./adr/0035-demonstracao-de-custo-zero.md); os artefatos e as provas, em
[`infra-demo/README.md`](../infra-demo/README.md). O que interessa a este documento é a conta.

### A conta de gratuidade, recurso a recurso

| Recurso | Quantidade usada | Franquia Always Free | Margem |
|---|---|---|---|
| Compute Ampere A1 — OCPU | 2 × 730 h = **1.460 OCPU-hora** | 1.500 OCPU-hora/mês | 40 h |
| Compute Ampere A1 — memória | 4 GB × 730 h = **2.920 GB-hora** | 9.000 GB-hora/mês | 6.080 GB-hora |
| Volume de inicialização | **50 GB** | 200 GB somados | 150 GB |
| VCN | **1** | 2 | 1 |
| Sub-rede, gateway, rotas, lista de segurança | 1 de cada | sem cobrança na OCI | — |
| IP público IPv4 | **1, efêmero** | sem cobrança na OCI, efêmero ou reservado | — |
| Tráfego de saída | irrelevante numa demonstração | 10 TB/mês | — |
| Compartimento, cotas, orçamento | 1 de cada | sem cobrança | — |

A linha apertada é a primeira: **1.460 de 1.500 OCPU-hora**, folga de 40 horas. É o que torna 2 OCPUs o
teto, e não uma preferência — 3 OCPUs dariam 2.190 OCPU-hora e a conta deixaria de ser zero. A validação
em `infra-demo/terraform/variables.tf` recusa o valor antes do `plan`.

### Por que o IPv4 não cobra aqui, e cobra na AWS

Vale registrar porque é contraintuitivo para quem vem da AWS: desde fevereiro de 2024 a AWS cobra
US$ 0,005 por hora por endereço IPv4 público, atribuído ou não — cerca de US$ 3,60/mês por endereço. A
Oracle não cobra por endereço IPv4, nem efêmero, nem reservado, nem reservado sem uso.

### Os três muros, em ordem de força

| | Mecanismo | O que faz | Onde está |
|---|---|---|---|
| 1 | Conta permanece no nível gratuito | a Oracle **recusa criar** recurso pago. Não é configuração, é o estado da conta | decisão de conta |
| 2 | Cotas de compartimento | limite **rígido**: pedir além faz o provisionamento **falhar** | `infra-demo/terraform/main.tf` |
| 3 | Orçamento com alerta em US$ 1 | apenas **avisa** por e-mail; não impede nada | `infra-demo/terraform/main.tf` |

A confusão entre 2 e 3 é comum e cara. **Orçamento na OCI não bloqueia.** Quem bloqueia é a cota. É por
isso que a política de cotas fecha famílias inteiras — banco gerenciado, balanceador, sistema de
arquivos — e reabre só o shape gratuito.

O princípio: **preferimos falha de provisionamento a cobrança.**

### O que a demonstração deixa de ter, e o que isso custaria

Não é gratuidade sem preço — é preço pago em outra moeda:

| Ausência | O que custaria em produção | Consequência na demonstração |
|---|---|---|
| Banco gerenciado | US$ 12,41/mês (B1ms + 64 GiB) | backup e atualização por conta de quem opera |
| Réplica e isolamento de falha | mais um de tudo | a máquina é ponto único de falha, declarado |
| Registro de contêiner | US$ 5,07/mês (ACR Basic) | as imagens são construídas na própria máquina |
| Storage de objeto | ~US$ 0,50/mês | comprovantes em disco local, com o adaptador declarado |
| Observabilidade gerenciada | US$ 2,30/GB ingerido | telemetria fica local; o endereço OTLP existe e está vazio |

Somado, é o mesmo sistema rodando por US$ 0,00 em vez de US$ 41,78 — com robustez operacional menor, e
isso está dito onde precisa estar dito.
