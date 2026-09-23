# ADR 0035 — Demonstração de custo zero: uma máquina Always Free, e a arquitetura de produção intacta ao lado

**Status:** aceito — Fase 25. Desenhado, construído e validado localmente. **Nada provisionado na Oracle.**
**Decisores:** Lucas (decisão de produto e custo) e time técnico
**Relacionados:** [0034](./0034-hospedagem.md), [0002](./0002-postgresql-postgis.md), [0007](./0007-storage-de-comprovantes-fora-do-banco.md), [0009](./0009-autenticacao-e-sessao.md), [0017](./0017-tempo-real-da-operacao.md), [0029](./0029-retencao-de-localizacao.md), [0033](./0033-entrada-da-demonstracao.md)

---

## Contexto

O [ADR 0034](./0034-hospedagem.md) escolheu Azure Container Apps + PostgreSQL Flexible Server, e essa
arquitetura foi **provisionada de verdade** — 12 recursos criados, imagens enviadas, três limites de
plataforma descobertos que nenhum teste local acharia. Ela custava **US$ 41,78/mês**, dos quais US$ 25
eram fixos independentemente de alguém abrir o sistema.

A restrição financeira então mudou, e virou absoluta:

> A infraestrutura de demonstração deve ter custo recorrente de **US$ 0,00** e não pode consumir crédito
> de nenhum provedor.

O ambiente Azure foi destruído e a cobrança verificada como zerada. Restou a pergunta que este ADR
responde: **como publicar um sistema que trabalha quando ninguém olha, sem pagar nada por isso.**

### Por que isto é difícil, e não só uma questão de achar um plano grátis

Quase toda hospedagem gratuita existe para servir página. A Torre Logística precisa de cinco coisas que
os planos gratuitos habituais recusam, escondem ou cobram:

| Exigência | Por quê | O que os planos gratuitos costumam fazer |
|---|---|---|
| Processo sempre vivo | o outbox despacha a cada 5 s, a previsão reavalia a cada minuto, o motor de alertas roda | dormem após alguns minutos ociosos |
| Conexão persistente | o console recebe posição e alerta por SignalR (ADR 0017) | não suportam WebSocket, ou cobram por conexão |
| PostgreSQL **com PostGIS** | geofence e proximidade são `ST_DWithin` no banco (ADR 0002) | oferecem Postgres sem extensão, ou nenhum banco |
| **Dois** processos separados | a Fase 25 separou API e workers de propósito | um processo por aplicação |
| Disco que sobrevive | comprovantes e histórico de posição | sistema de arquivos efêmero |

Um plano gratuito de aplicação que atendesse a lista não existe. O que existe é uma **máquina virtual
gratuita** — e aí a lista inteira vira questão de configurar, não de negociar.

---

## Decisão

### Uma máquina Ampere A1 do nível Always Free da Oracle Cloud, com a pilha em contêineres

```text
                       internet
                          │
                    80 / 443 (tcp e udp)
                          │
   ┌──────────────────────▼──────────────────────────────────────┐
   │  VM.Standard.A1.Flex — 2 OCPU arm64, 4 GB, 50 GB            │
   │                                                             │
   │   ┌─────────┐   rede "borda"                                │
   │   │  Caddy  │◄──── TLS, redirecionamento, proxy, estáticos  │
   │   └────┬────┘                                               │
   │        │                                                    │
   │   ┌────▼────┐         ┌───────────┐     rede "interna"      │
   │   │   API   │◄───────►│PostgreSQL │◄────┐   (sem saída)     │
   │   └─────────┘         │ + PostGIS │     │                   │
   │                       └───────────┘  ┌──▼──────┐            │
   │                                      │ Workers │            │
   │                                      └─────────┘            │
   └─────────────────────────────────────────────────────────────┘
```

Quatro contêineres, duas redes, uma porta pública. O PostgreSQL **não tem `ports:`** — não existe porta
para alguém encontrar varrendo a internet, nem por erro de firewall. Os workers também não.

### O que NÃO mudou, e é o ponto principal

Esta é uma mudança de **hospedagem**, não de arquitetura. Nenhuma das decisões dos ADRs 0001 a 0034 foi
desfeita para caber no custo zero:

| Continua | Como |
|---|---|
| API e workers como **processos separados** | dois contêineres, duas imagens, `ComposicaoDoProcessoDeTrabalho` inalterada |
| SignalR real, WebSocket real | atravessa o Caddy; instância única, sem backplane (ADR 0017) |
| PostgreSQL + PostGIS real | `ST_DWithin` no banco, não cálculo geodésico em C# |
| Simulador como **cliente HTTP** | contêiner próprio, sem `ProjectReference` nenhuma — nem para o domínio |
| Comprovantes atrás de `IObjectStorage` | adaptador local, declarado; o de Blob continua no repositório e exercitado |
| Offline, geofence, ETA, SLA, alertas, rastreamento público | idênticos |

**Os workers não voltaram para dentro da API.** Hospedá-los lá economizaria um contêiner e ~200 MB, e
desfaria exatamente o que a revisão da Fase 25 construiu.

### Por que a Oracle, e não outro nível gratuito

| Provedor | Por que não |
|---|---|
| AWS free tier | as 750 h de EC2 t2/t3.micro **expiram em 12 meses**; depois disso a demonstração começa a custar sem ninguém mexer nela |
| Azure free | mesma coisa — 12 meses, e o crédito de US$ 200 é justamente o que não se quer consumir |
| Google Cloud `e2-micro` | Always Free de verdade, mas 1 GB de memória (e 0,25 vCPU compartilhada). Quatro contêineres, um deles PostgreSQL, não cabem |
| Fly.io / Render / Railway | camada gratuita dorme ou foi descontinuada; e "dorme" é o requisito que este sistema não aceita |
| Oracle Ampere A1 | **2 OCPU e 12 GB Always Free**, sem prazo de validade, com disco persistente |

A palavra que decide é **Always**: o nível da Oracle não tem prazo. Os outros têm.

### O dimensionamento: 2 OCPU e 4 GB — e por que não 12 GB

O nível gratuito daria 12 GB. Pedimos 4, e não por modéstia:

| | Conta | Situação |
|---|---|---|
| OCPU | 2 × 730 h = **1.460** | de 1.500 gratuitas — folga de 40 h |
| Memória | 4 GB × 730 h = **2.920** | de 9.000 gratuitas |
| Disco | **50 GB** | de 200 gratuitos |

**A primeira razão é medição:** a pilha inteira — banco, API, workers e proxy — usa entre 350 e 470 MiB,
com pico de 346 MiB durante as seis histórias da demonstração. Memória alocada e não usada não acelera
nada, e 4 GB já deixam ~3,5 GB de folga para o sistema operacional e para a construção das imagens, que
é a operação mais faminta da máquina.

**A segunda razão pesa menos do que parecia antes da medição**, e vale registrar por quê. A Oracle
**recupera máquinas Always Free ociosas**, e um dos três critérios é *"Memory utilization is less than
20% (applies to A1 shapes only)"*, medido no 95º percentil ao longo de 7 dias.

```text
utilização de memória = usado / alocado

12 GB alocados, ~0,4 GB medidos  →   3 %
 4 GB alocados, ~0,4 GB medidos  →  10 %
                                     ↑ o limiar da política é 20 %
```

Pedir 4 GB em vez de 12 GB **triplica** a utilização medida — e ainda assim **não** ultrapassa os 20 %.
Esta é uma correção do que este documento afirmava antes da medição: a estimativa de ~1,7 GB de uso, da
qual saía uma utilização de 43 %, estava errada por mais de quatro vezes. A pilha medida usa entre
350 e 470 MiB (ver *Orçamento de memória*), e nenhum dos dois dimensionamentos sai da faixa de ociosa
pelo critério de memória.

Então o critério de memória **não** protege a máquina. O que resta é que os três critérios são
conjuntivos: basta CPU **ou** rede acima de 20 % para a máquina não ser considerada ociosa. Numa
demonstração com visitantes reais isso acontece às vezes — mas não é algo com que se possa contar.

**O risco de recuperação é real, e fica declarado como real.** A resposta a ele é tornar a
reconstrução barata, não evitar que ele exista.

### O que deliberadamente não entra

| Recurso | Está no Always Free? | Por que fica fora mesmo assim |
|---|---|---|
| Flexible Load Balancer | sim, um, a 10 Mbps | o Caddy na máquina já faz TLS, redirecionamento e proxy. Um balanceador seria um recurso a mais para monitorar **e** um teto de 10 Mbps para toda a demonstração |
| Object Storage | sim, 20 GB | os comprovantes são alguns megabytes num disco de 50 GB. O adaptador de objeto existe e está exercitado contra o Azure; aqui ele seria um recurso a mais sem ganho |
| NAT Gateway | **não** | existiria para dar saída a uma sub-rede privada. A sub-rede é pública, então não é preciso — e ele cobra |
| Segunda VCN | sim, até 2 | uma basta |

O critério foi o menor conjunto que atende: **Always Free ou não entra**, e dentro do Always Free,
*necessário ou não entra*.

---

## Consequências

### Uma máquina é um ponto único de falha, e isso é aceito

Se a máquina cair, a demonstração inteira cai — banco, API, workers e as três telas. Numa arquitetura
de produção isso seria inaceitável; numa demonstração de portfólio com custo obrigatoriamente zero, é o
preço declarado. A `infra/` continua ao lado mostrando como seria feito com dinheiro.

### A máquina pode ser recuperada pela Oracle

A política existe e vale, e a medição mostrou que ela **alcança** esta máquina: com ~0,4 GB usados de
4 GB, a utilização de memória fica em torno de 10 %, abaixo do limiar de 20 %. As três condições são
conjuntivas, então basta CPU ou rede acima de 20 % para escapar — o que uma demonstração com visitantes
produz às vezes, e não de forma confiável.

**Não haverá tráfego artificial para contornar a política.** Gerar requisição sintética para parecer
ocupado é enganar o critério, e um sistema de portfólio que depende de enganar alguém para ficar de pé
não é um argumento a favor de quem o construiu. Se a máquina for recuperada, ela é reconstruída: o
Terraform e o compose estão versionados, e o procedimento está em `infra-demo/README.md`.

### O custo zero não depende de vigilância

Três camadas, em ordem de força:

1. **A conta permanece no nível gratuito.** Sem upgrade para Pay As You Go, a Oracle recusa criar
   recurso pago — não é configuração, é o estado da conta. Se algum recurso exigir esse upgrade, o
   recurso é rejeitado.
2. **Cotas de compartimento.** Limite rígido: pedir além faz o provisionamento **falhar**. É o que
   impede que um erro de digitação em `ocpus` vire fatura.
3. **Orçamento com alerta em US$ 1.** Apenas **avisa**. Não bloqueia nada.

A distinção entre 2 e 3 é a que mais se confunde, e é por isso que ela está escrita em três lugares:
aqui, em `main.tf` e em `variables.tf`. **Preferimos falha de provisionamento a cobrança.**

### Um preço técnico real: a imagem do PostGIS precisou ser construída

`postgis/postgis` publica **só amd64** — o README do projeto é explícito (*"Supported architecture:
amd64 (x86-64)"*) e o manifesto confirma. Ampere A1 é aarch64.

Imagens multiarquitetura de terceiros existem; a mais citada se descreve como *"Status: Experimental"* e
*"(test) Docker image"*. Banco de demonstração pública não roda sobre imagem marcada como teste pelo
próprio autor.

A saída foi **fazer o que o Dockerfile oficial faz** — instalar os pacotes PGDG sobre a imagem oficial
do PostgreSQL — construindo também para arm64. A diferença é a base: o oficial parte de
`postgres:17-bullseye`, e bullseye é Debian 11, cujo LTS terminou em agosto de 2026. Partimos de
bookworm, que traz PostGIS 3.6 em vez de 3.5.

Essa diferença de versão **não fica no escuro**: a suíte geoespacial de integração roda contra a imagem
construída, e o resultado está registrado em `infra-demo/README.md`.

### A demonstração e a produção não podem ser confundidas

Esta arquitetura **não é a recomendada para um cliente real**. Ela coloca banco, aplicação e proxy na
mesma máquina, sem réplica, sem backup gerenciado e sem isolamento de falha. É a forma correta de
publicar um portfólio com restrição de custo zero — e seria a forma errada de atender uma operação
logística de verdade.

`infra/` responde pela produção. `infra-demo/` responde pela demonstração. Os dois diretórios existem
lado a lado exatamente para que a diferença seja visível.

---

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Manter o Azure e pagar | a restrição de custo é absoluta |
| Azure com tudo desligado, ligado sob demanda | demonstração que exige aviso prévio não é demonstração |
| Container único com API + workers + banco | desfaz a separação de processos da Fase 25 e o ADR 0002 |
| SQLite com SpatiaLite | trocaria o banco que o sistema inteiro pressupõe por outro, e invalidaria as migrations, os índices GIST e a suíte de integração |
| Frontends em hospedagem estática gratuita separada | quatro domínios em dois provedores, CORS e cookie cross-site a mais para nada: o Caddy já serve arquivo estático |
| Balanceador gratuito da Oracle à frente | teto de 10 Mbps e um recurso a mais, para fazer o que o Caddy já faz |

---

## Como a decisão é verificada

As provas estão em `infra-demo/README.md`, com os comandos e as saídas. O resumo do que foi exigido
antes de aceitar esta decisão:

| Afirmação | Prova |
|---|---|
| Todo recurso está no Always Free | tabela recurso a recurso, com citação da documentação da Oracle |
| A pilha roda em arm64 | manifesto de cada imagem inspecionado e imagens construídas para `linux/arm64` |
| A pilha cabe em 4 GB | memória medida com a pilha de pé e durante as seis histórias |
| O comprovante sobrevive ao reinício | arquivo lido depois de reiniciar contêiner e depois de `down`/`up` |
| O SignalR atravessa o proxy | conexão estabelecida e reconexão exercitada através do Caddy |
| O Terraform está correto | `terraform fmt` e `terraform validate` limpos, com o provedor `oracle/oci` |
| Nada foi provisionado | não há recurso criado na Oracle; aplicar exige autorização |
