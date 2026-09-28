# `infra-demo/` — a demonstração pública, por US$ 0,00

> **Isto não é a arquitetura de produção.** A de produção está em [`infra/`](../infra/): Azure Container
> Apps, PostgreSQL gerenciado, storage de objeto privado e identidade gerenciada — desenhada, aplicada
> contra o Azure real e destruída por decisão de custo.
>
> O que está aqui é a forma de pôr a Torre Logística no ar **sem gerar um centavo de custo recorrente**.
> Ela coloca banco, aplicação e proxy na mesma máquina, sem réplica e sem isolamento de falha. É a
> resposta certa para um portfólio com restrição de custo zero, e seria a resposta errada para uma
> operação logística de verdade.
>
> A decisão está no [ADR 0035](../docs/adr/0035-demonstracao-de-custo-zero.md).

---

## O que sobe

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
   │   │   API   │◄───────►│PostgreSQL │◄────┐  (sem saída)      │
   │   └─────────┘         │ + PostGIS │     │                   │
   │                       └───────────┘  ┌──▼──────┐            │
   │                                      │ Workers │            │
   │                                      └─────────┘            │
   └─────────────────────────────────────────────────────────────┘
```

| Arquivo | O que é |
|---|---|
| `terraform/` | a máquina, a rede, o compartimento, as cotas e o orçamento |
| `terraform/cloud-init.yaml` | o que a máquina faz sozinha na primeira inicialização |
| `docker-compose.demo.yml` | os quatro contêineres, as duas redes e os tetos de memória |
| `Caddyfile` | TLS, redirecionamento, proxy do SignalR, arquivos estáticos e cabeçalhos |
| `postgis/Dockerfile` | PostgreSQL 17 + PostGIS **para arm64** — a imagem oficial não publica essa arquitetura |
| `web/Dockerfile` | as três aplicações web compiladas e servidas pelo Caddy |
| `.env.demo.example` | modelo do arquivo de ambiente; o real vive na máquina, fora do Git |

Os workers continuam sendo um **processo à parte**, com imagem própria e sem porta nenhuma. Hospedar os
laços de fundo dentro da API economizaria um contêiner e desfaria a separação que a Fase 25 construiu.

---

## Gate de custo zero

Regra: **Always Free ou não entra.** E, dentro do Always Free, *necessário ou não entra*.

### Recurso a recurso

| Recurso | Quanto | Franquia Always Free (documentação da Oracle) | Sobra |
|---|---|---|---|
| Compute Ampere A1 — OCPU | 2 × 730 h = **1.460 OCPU-hora** | *"the first 1,500 OCPU hours … per month for free … VM.Standard.A1.Flex"* | 40 h |
| Compute Ampere A1 — memória | 4 GB × 730 h = **2.920 GB-hora** | *"9,000 GB hours per month"* | 6.080 GB-hora |
| Volume de inicialização | **50 GB** | *"a total of 200 GB of Block Volume storage, and five volume backups"* | 150 GB |
| VCN | **1** | *"Free Tier tenancies … can have up to 2 virtual cloud networks (VCNs)"* | 1 |
| Sub-rede, gateway de internet, tabela de rotas, lista de segurança | 1 de cada | componentes de VCN não são cobrados na OCI | — |
| IP público IPv4 | **1, efêmero** | a OCI não cobra por endereço IPv4 — efêmero ou reservado, em uso ou não | — |
| Tráfego de saída | irrisório | *"10 TB per month of outbound data"* | — |
| Compartimento, política de cotas, orçamento | 1 de cada | não são recursos cobrados | — |

**A linha apertada é a primeira.** 1.460 de 1.500 OCPU-hora deixa 40 horas de folga no mês. É isso que
faz 2 OCPUs ser o teto e não uma preferência: 3 OCPUs dariam 2.190 OCPU-hora e a conta deixaria de ser
zero. `terraform/variables.tf` recusa o valor antes do `plan`.

### O que ficou de fora, e por quê

| Recurso | Está no Always Free? | Por que não entra |
|---|---|---|
| Flexible Load Balancer | sim, **um**, a 10 Mbps | o Caddy já faz TLS, redirecionamento e proxy na própria máquina. Seria um recurso a mais para monitorar **e** um teto de 10 Mbps para toda a demonstração |
| Object Storage | sim, 20 GB | os comprovantes são alguns megabytes num disco de 50 GB. O adaptador de objeto existe e está exercitado contra o Azure |
| NAT Gateway | **não** | serviria para dar saída a uma sub-rede privada; a sub-rede aqui é pública. E ele cobra |
| Monitoring / Alarms extras | franquia mensal | alarme que ninguém lê é ruído com risco de estourar franquia |
| Segunda VCN | sim | uma basta |

### Se algum recurso exigir upgrade para Pay As You Go, ele é rejeitado

Nenhum dos itens acima exige. Caso alguma mudança futura passe a exigir, a regra é recusar o recurso —
não fazer o upgrade da conta.

---

## Guarda-corpos financeiros: o que BLOQUEIA e o que só AVISA

A confusão entre os dois é comum e cara, então está escrita em três lugares (aqui, em `main.tf` e em
`variables.tf`):

| | Mecanismo | O que faz | Limite rígido? |
|---|---|---|---|
| 1 | **Conta permanece no nível gratuito** | a Oracle recusa criar recurso pago. Não é configuração: é o estado da conta | **sim**, e é o mais forte |
| 2 | **Cotas de compartimento** (`oci_limits_quota`) | pedir além da cota faz o provisionamento **falhar** | **sim** |
| 3 | **Orçamento com alerta em US$ 1** (`oci_budget_budget`) | manda e-mail quando houver gasto | **não** — só avisa |

**Orçamento na OCI não bloqueia nada.** Quem bloqueia é a cota. Por isso a política de cotas fecha
famílias inteiras — banco gerenciado, balanceador, sistema de arquivos — e reabre apenas o shape
gratuito, nas quatro cotas que importam (núcleo e memória, por domínio de disponibilidade **e**
regionais; definir só uma das duas deixa a outra no limite de serviço padrão).

As declarações ficam todas numa política só porque a precedência depende disso. A documentação da
Oracle: *"Within a policy, quota statements are evaluated in order. A later statement supersedes an
earlier statement that targets the same resource"* — e, se estivessem em políticas separadas, valeria
a outra regra: *"If several policies target the same resource, the most restrictive policy applies"*,
e os `set` nunca abririam a exceção depois do `zero`.

> **O que `terraform validate` não verifica:** as declarações de cota são texto livre, validado pelo
> serviço da Oracle apenas no `apply`. Elas estão escritas conforme a documentação atual (família
> `compute-core`, cotas `standard-a1-core-count` / `standard-a1-memory-count` e as variantes
> `-regional-count`), mas **isso não foi exercitado contra a conta real** — nada foi provisionado.
> No primeiro `apply`, confirme a criação da política antes de criar a máquina.

**O princípio: preferimos falha de provisionamento a cobrança.**

---

## Risco de recuperação por ociosidade

A Oracle recupera máquinas Always Free ociosas. A política, citada da documentação:

> *"Oracle will deem virtual machine and bare metal compute instances as idle if, during a 7-day period,
> the following are true: CPU utilization for the 95th percentile is less than 20%; Network utilization
> is less than 20%; Memory utilization is less than 20% (applies to A1 shapes only)"*

As três condições são **conjuntivas**: todas precisam ser verdadeiras por 7 dias.

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

**Não há e não haverá tráfego artificial para contornar a política.** Gerar requisição sintética para
parecer ocupado é enganar o critério, e um portfólio que depende disso para ficar de pé não recomenda
quem o construiu.

### Se a máquina for recuperada

Tudo está versionado, então reconstruir é um procedimento, não um resgate:

```bash
cd infra-demo/terraform
terraform apply                                  # máquina, rede, cotas e orçamento
# aponte os quatro registros A para o novo IP (saída `ip_publico`)
ssh ubuntu@<novo-ip>
sudo install -m 600 /dev/null /etc/torre-logistica/.env.demo
sudo nano /etc/torre-logistica/.env.demo         # os segredos, do seu gerenciador
git clone <repositório> ~/torre-logistica && cd ~/torre-logistica
docker compose -f infra-demo/docker-compose.demo.yml \
  --env-file /etc/torre-logistica/.env.demo up -d --build
docker compose -f infra-demo/docker-compose.demo.yml \
  --env-file /etc/torre-logistica/.env.demo --profile roteiro run --rm simulador
```

**O que se perde:** o banco e os comprovantes, porque moram no disco da máquina recuperada. Para uma
demonstração isso é aceitável — o simulador reencena o roteiro com a mesma semente, e a mesma semente
conta a mesma história. Não é aceitável para produção, e é uma das razões de `infra/` existir.

---

## Endurecimento da máquina

| Medida | Onde | Observação |
|---|---|---|
| Administração por conta sem privilégio | imagem da Canonical (`ubuntu`) | criar mais uma conta não acrescentaria segurança e exigiria levar a chave pública para dentro do cloud-init |
| Só chave SSH, sem senha | `cloud-init.yaml` → `sshd_config.d/99-torre.conf` | arquivo separado porque atualização do pacote sobrescreve o `sshd_config` principal |
| Sem login de root | idem | |
| SSH só de uma faixa declarada | `main.tf` → lista de segurança | `cidr_de_administracao` não tem padrão: `0.0.0.0/0` funcionaria e deixaria todo varredor da internet batendo na porta |
| Só 80 e 443 públicas | `main.tf` + `cloud-init.yaml` | dois muros: a lista de segurança da VCN filtra antes de o pacote chegar, e o iptables da máquina fecha de novo caso alguém afrouxe a lista no console |
| Banco não exposto | `docker-compose.demo.yml` | o serviço `banco` não tem `ports:` e vive numa rede `internal: true`, sem saída |
| Workers não expostos | idem | sem `ports:`, e a imagem usa `runtime`, não `aspnet` |
| Socket do Docker não publicado | `cloud-init.yaml` | continua sendo socket de arquivo; não há porta TCP. Quem alcança o socket do Docker é root na máquina |
| Atualização de segurança automática | `cloud-init.yaml` | só o repositório de segurança, com reinício às 04:30 quando exigido — e a pilha volta sozinha por `restart: unless-stopped` |
| Segredos fora do Git e fora da imagem | `/etc/torre-logistica/.env.demo`, 600 | o cloud-init **não** escreve segredo: metadado de instância é legível por qualquer processo da máquina e fica no estado do Terraform |
| Troca de 2 GB | `cloud-init.yaml` | não é para a operação, é para a compilação; `swappiness=10` mantém a troca como rede de segurança |

### Cabeçalhos e política de conteúdo

O `Caddyfile` aplica HSTS, `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`,
`Cross-Origin-Opener-Policy` e uma CSP por aplicação. Duas escolhas que merecem nota:

- **`connect-src` lista a API em `https:` e em `wss:`.** Uma política que cobrisse só `https:`
  derrubaria o tempo real sem derrubar o resto: o mapa carrega e simplesmente não se move.
- **A PWA do motorista recebe `Permissions-Policy: geolocation=(self), camera=(self)`.** Ela precisa
  das duas; o console e o rastreamento público, não.

---

## A imagem do PostGIS, e por que ela precisa existir

A imagem usada em desenvolvimento e nos testes, `postgis/postgis:17-3.5`, **não roda na máquina da
demonstração**. O README do projeto PostGIS é explícito — *"Supported architecture: amd64 (x86-64)"* —
e o manifesto confirma.

Imagens multiarquitetura de terceiros existem. A mais citada se descreve como *"Status: Experimental,
under active development"* e *"(test) Docker image"*. Banco de demonstração pública não roda sobre
imagem que o próprio autor marca como teste.

`postgis/Dockerfile` faz o que o Dockerfile oficial faz — instalar os pacotes PGDG sobre a imagem
oficial do PostgreSQL — construindo também para arm64. A diferença é a base: o oficial parte de
`postgres:17-bullseye`, e bullseye é Debian 11, cujo LTS terminou em **agosto de 2026**. Partimos de
bookworm, cujo repositório PGDG oferece PostGIS **3.6**, não 3.5.

Essa diferença de versão não fica no escuro: ver *Equivalência geoespacial*, nas evidências.

---

## Como provisionar

> Nada disto foi executado. Não há recurso criado na Oracle, e criar exige autorização explícita.

### Passo 0 — autenticar na Oracle

Sem isto, nada abaixo funciona: a CLI e o provedor do Terraform leem o mesmo `~/.oci/config`, e sem
ele a mensagem é direta — `Could not find config file at ~/.oci/config`.

Dois caminhos, e eles escrevem perfis **diferentes** no mesmo arquivo:

| | `oci session authenticate` | `oci setup config` |
|---|---|---|
| Como | abre o navegador; você entra como entraria no console | gera um par de chaves e você cola a pública no console |
| Guarda | um token de sessão | uma chave privada no disco |
| Expira | sim (`oci session refresh` renova) | não |
| No Terraform | `metodo_de_autenticacao = "SecurityToken"` | `metodo_de_autenticacao = "ApiKey"` (padrão) |

Declarar o método errado produz um erro que fala de **chave ausente**, e não do método — por isso a
variável existe.

```bash
oci session authenticate --region <sua-região-de-origem> --profile-name DEFAULT
```

### Passos 1 a 4 — conferir e criar

```bash
cd infra-demo/terraform
cp terraform.tfvars.example terraform.tfvars
$EDITOR terraform.tfvars             # OCID da tenancy, região de ORIGEM, chave pública, seu IP

terraform init
terraform validate
terraform plan                       # leia a saída `conferencia_de_gratuidade`
terraform apply
```

### Os quatro nomes, e o momento certo de criar os registros

| Hostname | Serve |
|---|---|
| `operacao.torre.lucasafvr.com.br` | console operacional |
| `motorista.torre.lucasafvr.com.br` | PWA do motorista |
| `rastrear.torre.lucasafvr.com.br` | rastreamento público |
| `api.torre.lucasafvr.com.br` | API e a conexão persistente do SignalR |

Os quatro são subdomínios de **`lucasafvr.com.br`**, que é o domínio registrável. Não é estética: o
cookie de sessão é `SameSite=Strict` (ADR 0009), e só é "mesmo site" o que compartilha o domínio
registrável — nomes de domínios diferentes derrubariam a sessão sem nenhum erro visível.

A ordem importa e não é negociável:

1. `terraform apply` cria a máquina e devolve `ip_publico`;
2. os **quatro registros A** apontam para esse IP;
3. **só então** o compose sobe.

Subir o compose antes do DNS não quebra nada de forma permanente, mas o Caddy pede o certificado no
instante em que sobe, a autoridade certificadora confere o DNS naquele momento, e cada tentativa
falha consome parte do limite semanal de emissão da Let's Encrypt para esses nomes.

### Se aparecer "Out of host capacity"

É comum e **não** significa erro no plano: a capacidade gratuita de Ampere A1 é disputada, e a recusa
costuma ser de um domínio de disponibilidade específico. Preencha `dominio_de_disponibilidade` com
outro e tente de novo.

---

## Como implantar e atualizar

```bash
ssh ubuntu@<ip>
cd ~/torre-logistica && git pull
docker compose -f infra-demo/docker-compose.demo.yml \
  --env-file /etc/torre-logistica/.env.demo up -d --build
```

As imagens são construídas **na própria máquina**, e isso é uma escolha: um registro de contêiner seria
mais um recurso, mais um caminho de credencial e — no Azure — US$ 5,07/mês. O preço pago é o tempo de
build numa máquina de 2 OCPUs, que é exatamente para o que existem os 2 GB de troca.

**Mudança de domínio exige reconstruir a imagem `web`**, não só reiniciar: o endereço da API é embutido
no pacote JavaScript no momento da build.

### Encenar o roteiro

```bash
docker compose -f infra-demo/docker-compose.demo.yml \
  --env-file /etc/torre-logistica/.env.demo --profile roteiro run --rm simulador
```

O simulador roda, encena as seis histórias contra a API real e sai. Ele vive atrás de um `profile` para
que subir a pilha não dispare a encenação por acidente.

---

## Como validar localmente

A pilha inteira roda na máquina de quem desenvolve, sem Oracle e sem domínio:

```bash
cp infra-demo/.env.demo.example /caminho/fora/do/repo/.env.demo.local
# edite: TORRE_ESQUEMA_PUBLICO=http, TORRE_DOMINIO_API=localhost,
#        TORRE_DOMINIO_CONSOLE=console.localhost (e os outros dois), e preencha os segredos

docker compose -f infra-demo/docker-compose.demo.yml \
  --env-file /caminho/fora/do/repo/.env.demo.local up -d --build

curl -H "Host: console.localhost" http://localhost/
```

`TORRE_ESQUEMA_PUBLICO=http` desliga o ACME. O `Caddyfile` exercitado é o mesmo — roteamento,
cabeçalhos, retorno para `index.html` e proxy do SignalR —, e o que fica de fora é apenas a emissão do
certificado.

---

## Demonstração × produção, lado a lado

| | `infra/` — produção | `infra-demo/` — demonstração |
|---|---|---|
| Ferramenta | Bicep | Terraform + Docker Compose |
| Nuvem | Azure | Oracle Cloud |
| Compute | Container Apps, dois aplicativos | uma VM, quatro contêineres |
| Banco | PostgreSQL Flexible Server gerenciado | PostgreSQL em contêiner, na mesma máquina |
| Backup do banco | gerenciado, 7 dias | **nenhum** |
| Comprovantes | Blob privado + SAS por identidade gerenciada | disco local + URL assinada pela aplicação |
| Segredos | Key Vault com RBAC | arquivo `600` na máquina |
| TLS | da plataforma | Caddy, via ACME |
| Isolamento de falha | serviços separados | **nenhum**: uma máquina |
| Escala | trocar `maxReplicas` (depois do backplane) | vertical, até o teto do nível gratuito |
| Custo | US$ 41,78/mês | **US$ 0,00/mês** |

O que **não** muda entre as duas: o código, o domínio, as migrations, a separação API/workers, o
SignalR, o PostGIS, o simulador como cliente HTTP e as seis histórias.

---

# Evidências

Tudo abaixo foi executado nesta máquina, com a pilha real. O que não foi executado está dito como não
executado — **nada foi provisionado na Oracle**.

## Gate ARM64

### O manifesto de cada imagem, inspecionado

```text
postgis/postgis:17-3.5              SEM ARM64   linux/amd64
postgis/postgis:17-3.5-alpine       SEM ARM64   linux/amd64
postgres:17-bookworm                ARM64 OK    linux/386, amd64, arm/v7, arm64/v8, ppc64le
caddy:2-alpine                      ARM64 OK    amd64, arm/v6, arm/v7, arm64/v8, ppc64le, riscv64, s390x
node:22-alpine                      ARM64 OK    amd64, arm, arm64, s390x
mcr…/dotnet/sdk:10.0-alpine         ARM64 OK    amd64, arm/v7, arm64
mcr…/dotnet/aspnet:10.0-alpine      ARM64 OK    amd64, arm/v7, arm64
mcr…/dotnet/runtime:10.0-alpine     ARM64 OK    amd64, arm/v7, arm64
```

A primeira linha é a que redesenhou a pilha: a imagem PostGIS usada em desenvolvimento e nos testes
**não existe para arm64**. Daí `postgis/Dockerfile`.

### Os cinco artefatos, construídos para `linux/arm64`

```text
torre-logistica-api:arm64            linux/arm64    146 s
torre-logistica-workers:arm64        linux/arm64     87 s
torre-logistica-simulador:arm64      linux/arm64     38 s
torre-logistica-postgis:arm64        linux/arm64    ~50 min (instalação de pacotes sob emulação)
torre-logistica-web:arm64            linux/arm64     ~2 min
```

A arquitetura de cada uma foi conferida com `docker image inspect --format '{{.Os}}/{{.Architecture}}'`,
e não pela ausência de erro na build.

> Os tamanhos dessas imagens **não** são comparáveis aos das construídas nativamente: o `buildx --load`
> as importa como conteúdo comprimido, e `docker history` devolve zero para elas. Comparar os números
> daria a impressão falsa de que a imagem arm64 é três vezes menor.

### A mudança que tornou o gate viável: compilação cruzada em vez de emulação

A primeira tentativa emulou o SDK inteiro por QEMU (`--platform linux/arm64` sem mais nada). Ela foi
interrompida depois de **1 h 30 min sem terminar a primeira imagem** — o `dotnet restore` de um projeto
só ainda estava rodando.

Os três Dockerfiles .NET passaram a usar o caminho documentado pela Microsoft:

```dockerfile
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS compilacao
ARG TARGETARCH
RUN dotnet restore -a $TARGETARCH …
RUN dotnet publish  -a $TARGETARCH --self-contained false …
```

O SDK roda nativo, e só o resultado é arm64. A mesma imagem passou de **90+ minutos sem terminar** para
**146 segundos**, com o publicado saindo em `bin/Release/net10.0/linux-musl-arm64/`.

O estágio Node da imagem web recebeu o mesmo `--platform=$BUILDPLATFORM`, por um motivo diferente: o que
ele produz é JavaScript, CSS e HTML, que não têm arquitetura.

## A pilha de pé

```text
NAME                 IMAGE                            STATUS         PORTS
torre-demo-api       torre-logistica-api:demo         Up (healthy)   8080/tcp
torre-demo-banco     torre-logistica-postgis:17-3.6   Up (healthy)
torre-demo-web       torre-logistica-web:demo         Up (healthy)   0.0.0.0:80->80, 0.0.0.0:443->443, 0.0.0.0:443->443/udp
torre-demo-workers   torre-logistica-workers:demo     Up
```

O contêiner dos workers **não tem sonda de saúde**, e isso é deliberado: ele não atende HTTP, então a
única sonda possível seria "o processo existe?" — que é exatamente o que o Docker já sabe.

### Só o Caddy publica porta

```text
torre-demo-banco       portas publicadas: NENHUMA
torre-demo-workers     portas publicadas: NENHUMA
torre-demo-api         portas publicadas: NENHUMA
torre-demo-web         portas publicadas: 80/tcp, 443/tcp, 443/udp
```

E o Caddy **não alcança o banco**: eles estão em redes diferentes, e a do banco é `internal: true`.

> Uma armadilha de leitura, registrada para ninguém repetir: um teste ingênuo de "a porta 5432 do
> hospedeiro está aberta?" responde **sim** nesta máquina. Não é o contêiner da demonstração — é uma
> instalação nativa do PostgreSQL do Windows. `docker port torre-demo-banco` é a pergunta certa, e ela
> responde vazio.

### A borda roteia os quatro nomes

```text
console.localhost      HTTP 200   <title>Console Operacional</title>
motorista.localhost    HTTP 200   <title>Torre Logística — Motorista</title>
rastreio.localhost     HTTP 200   <title>Acompanhe sua entrega</title>
localhost (API)        HTTP 200   /health/ready
```

Recarregar uma rota interna devolve a aplicação, não 404 — o que prova o retorno para o `index.html`:

```text
console.localhost/entregas/abc              HTTP 200
motorista.localhost/rotas/1/paradas         HTTP 200
rastreio.localhost/e/token-qualquer         HTTP 200
```

### Cabeçalhos

```text
Content-Security-Policy: default-src 'self'; connect-src 'self' https://… wss://…; img-src 'self' data: blob:; worker-src 'self' blob:; …
Strict-Transport-Security: max-age=31536000; includeSubDomains
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Cross-Origin-Opener-Policy: same-origin
(sem cabeçalho Server)

console.localhost      Referrer-Policy: strict-origin-when-cross-origin
motorista.localhost    Referrer-Policy: strict-origin-when-cross-origin
                       Permissions-Policy: geolocation=(self), camera=(self), microphone=(), payment=()
rastreio.localhost     Referrer-Policy: no-referrer
```

### Dois defeitos que só a verificação encontrou

Ambos no `Caddyfile`, ambos corrigidos e reverificados:

| Defeito | Sintoma real | Correção |
|---|---|---|
| `header Referrer-Policy "no-referrer"` no bloco do site **não sobrescrevia** o do trecho importado | o rastreamento público saía com `strict-origin-when-cross-origin`, e o token do link poderia vazar no `Referer` | o trecho passou a receber o valor como argumento (`import cabecalhos-comuns "no-referrer"`) |
| `header /index.html Cache-Control "no-cache"` não pegava nada | quem abre o site pede `/`, não `/index.html`; o `index.html` saía **sem** `Cache-Control`, e uma implantação não chegaria a quem já visitou | matchers disjuntos: `@versionado path /assets/*` e `@volatil not path /assets/*` |

Depois da correção:

```text
raiz (/)           Cache-Control: no-cache
/assets/index-*.js Cache-Control: public, max-age=31536000, immutable
```

## Equivalência geoespacial

A imagem construída, consultada com a pilha de pé:

```text
postgis 3.6 USE_GEOS=1 USE_PROJ=1 USE_STATS=1 | pg 17.11 (Debian 17.11-1.pgdg12+2)
geofence a 299 m: true | a 301 m: false
```

A fronteira de 300 m responde igual à da imagem `postgis/postgis:17-3.5` usada nos testes — que é o
caso de fronteira que o ROADMAP exige (`299 m → dentro`, `301 m → fora`).

Migrations aplicadas pela própria API na subida: **40 tabelas**, com a extensão criada pela migration
`Fundacao`, e o elenco fictício semeado (**2 organizações, 5 usuários**).

## As seis histórias

Encenadas pelo simulador, contra a API real, **saída 0**:

```text
OperacaoNormal       Entregue            Criada → Planejada → Atribuida → SaiuParaRota → ChegadaRegistrada → Entregue
RiscoDeAtraso        EmRota              Criada → Planejada → Atribuida → SaiuParaRota
MotoristaOffline     EmRota              Criada → Planejada → Atribuida → SaiuParaRota
TentativaFrustrada   TentativaFrustrada  Criada → Planejada → Atribuida → SaiuParaRota → ChegadaRegistrada → TentativaFrustrada
EntradaNoGeofence    ProximaDoDestino    Criada → Planejada → Atribuida → SaiuParaRota → ProximidadeDetectada
ProvaDeEntrega       Entregue            Criada → Planejada → Atribuida → SaiuParaRota → ChegadaRegistrada → Entregue
```

O que ficou no banco: 6 entregas, 32 eventos de entrega, 21 posições no histórico, 1 posição atual,
6 alertas, 1 ocorrência, 1 comprovante.

> **Uma tentativa falhou primeiro, e a causa vale registro.** O simulador foi recusado com
> `403 origem_nao_autorizada`: o login da API exige `Origin` conhecido, e o simulador não declarava
> nenhum. A correção não foi afrouxar a API — foi declarar a origem em `docker-compose.demo.yml`,
> que é exatamente o mecanismo que `OpcoesDoSimulador.OrigemDeclarada` existe para oferecer.

## Persistência

### O que o simulador NÃO prova

O roteiro registra o comprovante com `arquivos = []` — ele encena a história, não o envio do arquivo.
Uma comparação de "0 arquivos antes, 0 depois" passaria como aprovada **sem provar nada**, e foi
exatamente o que a primeira execução produziu.

A persistência do arquivo foi então provada exercitando o caminho que um motorista de verdade percorre,
através do Caddy: sessão de motorista → autorização de envio → `PUT` dos bytes na URL assinada.

```text
sessão de motorista aberta
envio autorizado — chave organizacoes/…/entregas/…/01a0cc6d8d8375cb82cacf9f6937aa65.jpg
bytes enviados: HTTP 201 (160 bytes)
```

### E sobrevive

```text
=== 1. Logo após o envio ===
  arquivos=2 impressao=765dce945d2b
  -rw-r--r-- 1654 1654  160  …/01a0cc6d8d8375cb82cacf9f6937aa65.jpg
  -rw-r--r-- 1654 1654  110  …/01a0cc6d8d8375cb82cacf9f6937aa65.jpg.meta

=== 2. Depois de reiniciar API e workers ===
  arquivos=2 impressao=765dce945d2b

=== 3. Depois de destruir e recriar a pilha inteira (down + up) ===
  contêineres da demonstração agora: 0
  arquivos=2 impressao=765dce945d2b

=== 4. E o banco? ===
  entregas: 6   eventos: 32
```

Dois arquivos porque o adaptador local guarda os metadados ao lado do conteúdo. O dono é **1654**, o
usuário sem privilégio das imagens .NET — o mesmo que o `cloud-init.yaml` prepara na máquina real.

## SignalR através do Caddy

Usando o **mesmo cliente do console** (`@microsoft/signalr`), com `transport: WebSockets` declarado —
sem isso o cliente cairia para long polling e o teste passaria sem provar o que se propõe:

```text
sessão aberta para paula.siqueira@aurora.test (perfil Operador)
CONECTADO por WebSocket através do Caddy — connectionId KVkWHDRMeuMW1poxj1atAA
reiniciando o contêiner da API...
RECONECTANDO — a conexão caiu            (WebSocket fechado, status 1006)
  … 502 do Caddy enquanto não há quem responder atrás dele — o cliente insiste
RECONECTADO com nova conexão U1WrdSy4gowlJuRFtOSM-Q
estado final: Connected | quedas: 1 | reconexões: 1
```

Quatro segundos entre a queda e a volta, sem intervenção. Os `502` no meio são o comportamento certo:
o proxy está de pé e o destino não.

## Orçamento de memória

| Contêiner | Ocioso | Pico durante as seis histórias | Teto declarado |
|---|---:|---:|---:|
| `torre-demo-banco` | 40–212 MiB | 55 MiB | 1024 MiB |
| `torre-demo-api` | 104–129 MiB | 161 MiB | 900 MiB |
| `torre-demo-workers` | 78–112 MiB | 116 MiB | 512 MiB |
| `torre-demo-web` | 13–15 MiB | 15 MiB | 192 MiB |
| **Total** | **~235–470 MiB** | **346 MiB** | **2.628 MiB** |

A faixa no ocioso é a do PostgreSQL esquentando o cache: 40 MiB recém-subido, ~212 MiB depois de
trabalhar. O que importa para a máquina de 4 GB são os dois extremos:

- **uso medido**: em torno de 350–470 MiB;
- **pior caso possível**: 2,6 GB, que é a soma dos tetos declarados — e é esse o número que precisa
  caber, porque é o máximo que o Docker deixaria os quatro contêineres tomarem.

Sobram ~1,4 GB para o sistema operacional e para a construção das imagens, que é a operação mais
faminta da máquina — e é para ela que existem os 2 GB de troca do `cloud-init.yaml`.

**Nada disso justifica pedir 12 GB.** Pedir mais memória do que se usa não acelera nada e coloca a
máquina dentro do critério de ociosa que autoriza a Oracle a recuperá-la.

## Terraform

```text
terraform fmt -recursive      sem mudanças
terraform init                oracle/oci v7.32.0 instalado
terraform validate            Success! The configuration is valid.
```

**Não houve `plan` nem `apply`.** Os dois exigem credencial da Oracle, e nada foi provisionado.

---

# O ambiente publicado

Desde **28/09/2026** a demonstração está no ar, em custo recorrente de **US$ 0,00**.

```text
operacao.torre.lucasafvr.com.br   ┐
motorista.torre.lucasafvr.com.br  ├─ A ─► 137.131.167.193  (VM.Standard.E2.1.Micro, sa-saopaulo-1)
rastrear.torre.lucasafvr.com.br   │
api.torre.lucasafvr.com.br        ┘
```

## Como o deploy foi feito

Três decisões que não são óbvias e que valem mais que o passo a passo:

**As imagens são construídas fora e transferidas.** `docker compose build` das imagens .NET numa
máquina de 1 GB estoura. O caminho é `docker save` de todas as cinco num único fluxo — o que
deduplica as camadas compartilhadas entre API, Workers e simulador — comprimido e entregue por SSH
a um `docker load` do outro lado. Levou **114 s** para ~379 MB de imagens.

**Os segredos nascem na VM.** `openssl rand` roda lá dentro, escreve direto no `.env.demo` com
`umask 077`. Nenhum segredo de produção transita pela rede nem toca a máquina de desenvolvimento,
e por isso nenhum deles pode vazar por um `git add` distraído.

**Os limites de CPU da emulação NÃO vão para a VM.** O arquivo `compose.e2-pior-caso.yml` existia
para simular 1/8 de OCPU numa máquina de desenvolvimento grande. Na VM a restrição já é real:
repeti-la estrangularia de propósito o burst que o shape entrega. Só os tetos de memória viajam,
em `compose.1gb.yml`.

## Memória real, com a pilha rodando

| Momento | Usado | Disponível | Swap |
|---|---|---|---|
| Sistema recém-instalado, sem a pilha | 440 MiB | 513 MiB | 1 MiB |
| Pilha em repouso | 593 MiB | 360 MiB | 137 MiB |
| **Pico durante as seis histórias** | **701 MiB** | 252 MiB | **295 MiB** |

De 954 MiB totais. **Zero OOM, zero reinício.**

O maior inquilino isolado não é nosso: o agente da Oracle custa **149 MiB** entre os dois serviços
dele. Desligá-lo liberaria mais que qualquer ajuste nosso — e é exatamente por isso que ele fica.
É ele que reporta uso de CPU, rede e memória à Oracle, e esse é o dado pelo qual ela decide se uma
máquina Always Free está ociosa o bastante para ser recuperada.

Foram desativados `fwupd`, `ModemManager`, `udisks2` e `rpcbind` — nenhum tem função numa VM. O
ganho foi **5 MiB**, não os ~59 que o `MemoryCurrent` do systemd sugeria: aquele número inclui cache
reclamável, não só memória anônima. `snapd` **não** foi desligado, porque o agente da Oracle é um
snap e depende dele.

## Swap: 2 GB, e não os 512 MiB planejados

O `cloud-init.yaml` já cria 2 GB com `vm.swappiness=10`, e isso antecedeu a discussão sobre
dimensionar swap. O pico medido foi de **295 MiB** — os 512 MiB planejados teriam bastado. Manter
os 2 GB não custa nada (o disco tem 43 GB livres) e amplia a proteção contra transiente; reduzir
exigiria recriar o arquivo sem ganho prático.

## Superfície exposta

```text
escutando em 0.0.0.0:   22, 80, 443       (e nada mais)
filtradas de fora:      111, 5432, 8080, 2019
rede do banco:          internal=true — banco, API e workers sem rota para a internet
publicando portas:      só o contêiner do Caddy
```

Cabeçalhos na borda: `Content-Security-Policy` com `frame-ancestors 'none'`,
`Strict-Transport-Security` de um ano com `includeSubDomains`, `X-Frame-Options: DENY`,
`X-Content-Type-Options: nosniff`, `Cross-Origin-Opener-Policy: same-origin`. Nenhum cabeçalho de
versão de servidor ou framework é devolvido.

## Latência pela internet pública

| Rota | p50 | p95 | p99 |
|---|---|---|---|
| `/health/ready` | 20 ms | 95 ms | 117 ms |
| `GET /api/entregas` | 47 ms | 222 ms | 329 ms |

Melhor que o pior caso estimado localmente (p99 de ~450 ms). A emulação não concedia burst, e a
máquina real concede: durante as histórias os workers chegaram a **272% de um núcleo**.

## Uma armadilha que custou um ciclo

Os quatro registros A foram criados e, mesmo assim, os nomes continuaram respondendo pela borda da
Vercel. O diagnóstico errado seria "propagação". O certo veio de perguntar ao **autoritativo**: se
`ns1.vercel-dns.com` ainda devolve o valor antigo, não há nada propagando.

O que decidiu foi um controle: um nome inventado na hora respondia com **o mesmo TTL e os mesmos
IPs** dos quatro nomes. Isso é assinatura de curinga, não de registro específico — e apontou para
uma atribuição de projeto na Vercel que tinha precedência sobre o registro A.

---

# Qual máquina hospeda a demonstração, e por quê

| | |
|---|---|
| **Arquitetura de produção** | **Azure** — é o alvo real do projeto, descrito em `infra/`. Nada aqui a substitui |
| **Demo preferida** | `VM.Standard.A1.Flex` — 1 OCPU inteira, 4 GB. Volta a ser usada assim que houver capacidade |
| **Demo atual** | `VM.Standard.E2.1.Micro` — contorno por indisponibilidade prolongada do pool A1 |

O A1 não foi abandonado: a cota, as variáveis e o caminho de imagem ARM64 continuam no Terraform.
Trocar de volta é mudar uma variável — `shape_da_demo = "VM.Standard.A1.Flex"`.

A troca aconteceu porque a capacidade A1 em `sa-saopaulo-1` não apareceu em **143 consultas ao
longo de 12 horas seguidas**, cobrindo noite e madrugada. A região tem um único domínio de
disponibilidade, e o nível Always Free só existe na região de origem da conta — não havia para
onde mudar dentro da OCI, exceto de shape.

## O que a máquina atual NÃO é

O `E2.1.Micro` mantém a demonstração pública no ar por US$ 0,00. Ele **não** é uma recomendação de
arquitetura para cliente real, e apresentá-lo como tal seria desonesto. As limitações, medidas e
não estimadas:

| Limitação | Número |
|---|---|
| Memória total do host | **1 GB** — sistema, Docker e a pilha inteira dividem isso |
| CPU de linha de base | **1/8 de OCPU**, em AMD EPYC 7551 de 2,0 GHz (2017) |
| Burst | existe, **não é garantido** e não foi possível reproduzi-lo localmente |
| p99 local no pior caso | **~450 ms** em `GET /api/entregas`, com p50 de 6 ms |
| Topologia | **nó único** — banco, API, workers e borda na mesma máquina |
| Disponibilidade | **sem SLA**; Always Free pode ser recuperado pela Oracle se ficar ocioso |
| Rede | 0,48 Gbps, 1 VNIC |

Para um cliente real, a resposta continua sendo `infra/` no Azure, com banco gerenciado, instâncias
separadas e SLA contratado.

# Estudo do shape E2.1.Micro — alternativa Always Free ao Ampere A1

Depois de **143 consultas em 12 horas seguidas** sem uma única janela de capacidade A1 em
`sa-saopaulo-1`, o outro shape gratuito da OCI foi medido. Este capítulo registra o estudo. **Nada
foi provisionado**: o objetivo era saber se vale a pena.

## Os dois shapes, lado a lado

| | `VM.Standard.A1.Flex` | `VM.Standard.E2.1.Micro` |
|---|---|---|
| Arquitetura | ARM64 (Ampere) | x86-64 (AMD EPYC 7551 Naples, 2,0 GHz) |
| Configuração da demo | 1 OCPU · 4 GB | fixo: 1 GB |
| CPU do nível gratuito | 1 OCPU inteira | **1/8 de OCPU com burst** |
| Rede | — | 0,48 Gbps, 1 VNIC |
| `billing-type` da API | `ALWAYS_FREE` | `ALWAYS_FREE` |
| Cota | `standard-a1-core-count` | `standard-e2-micro-core-count` |
| Capacidade em 28/09/2026 | `OUT_OF_HOST_CAPACITY` | **`AVAILABLE`** |

A última linha veio da mesma chamada, no mesmo instante, três vezes seguidas — o relatório sabe
discriminar, e isso valida retroativamente as 143 leituras negativas da vigília.

> **Uma correção que muda a leitura.** A API do shape devolve `ocpus: 1.0`, e por isso a primeira
> emulação deu à pilha um núcleo moderno inteiro. A documentação da Oracle diz outra coisa:
> *"1/8th of an OCPU with the ability to use additional CPU resources"*. O `1.0` é o tamanho
> **nominal** do shape, não o que o nível gratuito sustenta. O teste foi refeito com teto de
> 0,25 vCPU — e é esse o número que vale.

## Compatibilidade `linux/amd64`

A demo nasceu multiarch e continuou. Nenhum arquivo precisou mudar:

| Artefato | `linux/amd64` | Base |
|---|---|---|
| `torre-logistica-postgis` | ✅ 212 MB | digest fixado do `postgres:17-bookworm` é **lista de manifesto** |
| `torre-logistica-api` | ✅ 58 MB | `mcr.microsoft.com/dotnet/aspnet:10.0-noble` |
| `torre-logistica-workers` | ✅ 45 MB | idem |
| `torre-logistica-web` | ✅ 24 MB | `caddy:2.10-alpine` |
| `torre-logistica-simulador` | ✅ 40 MB | idem API |

O ARM64 segue intacto: as cinco imagens `:arm64` da prova anterior continuam válidas e nenhum
`Dockerfile` foi tocado. A compilação cruzada (`--platform=$BUILDPLATFORM` + `dotnet -a $TARGETARCH`)
atende as duas arquiteturas pelo mesmo caminho.

## Memória: cabe em 1 GB

Medido em três perfis, sempre com as seis histórias completas. Pico **simultâneo**, não soma de
picos em instantes diferentes:

| Perfil | banco | API | workers | web | simulador | **total simultâneo** |
|---|---|---|---|---|---|---|
| Tetos generosos (2.884 MiB) | 208,2 | 173,6 | 114,9 | 11,8 | 27,5 | **536,0 MiB** |
| Tetos de 1 GB (704 MiB) | 208,3 | 151,0 | 89,4 | 12,6 | 27,6 | **488,9 MiB** |
| Pior caso (1 GB + 1/8 OCPU) | 220,0 | 155,4 | 79,2 | 10,5 | 28,9 | **494,0 MiB** |

Duas leituras importam:

1. **O .NET encolhe sozinho.** Com teto de 900 MiB a API pediu 173,6 MiB; com 224 MiB pediu 151,0.
   O GC lê o limite do cgroup e se dimensiona. Nenhuma variável de ambiente foi necessária.
2. **O PostgreSQL é o inquilino apertado.** 220 MiB contra um teto de 256 MiB são 86%. É onde um
   ajuste de `shared_buffers` entraria primeiro, se entrar.

Orçamento do host de 1 GB (≈960 MiB utilizáveis):

```text
Ubuntu 24.04 Minimal + kernel + sshd   ~150 MiB
dockerd + containerd                   ~150 MiB
contêineres da Torre (pico medido)      494 MiB
                                       ---------
                                        ~794 MiB   sobra ~165 MiB
```

Nenhuma funcionalidade foi removida para chegar a esse número. Workers continuam em processo
separado, SignalR continua, PostGIS continua, e o domínio não foi tocado.

## CPU: funciona, com cauda pesada

Sob 1/8 de OCPU (0,25 vCPU somada, **sem** contar o burst que o shape real oferece):

| Medida | 1 núcleo compartilhado | 1/8 de OCPU |
|---|---|---|
| API saudável em | 24 s | **93 s** |
| Seis histórias | 100 s | **135 s** |
| `/health/ready` p50 / p95 / p99 | 6,0 / 10,3 / 11,1 ms | **4,5 / 74,3 / 164,5 ms** |
| `GET /api/entregas` p50 / p95 / p99 | — | **6,2 / 67,1 / 449,6 ms** |
| Reinícios, OOM, erros | 0 | **0** |

A mediana continua boa; a cauda é onde a estrangulação aparece. Para uma demonstração de portfólio,
p99 de 450 ms no pior caso é aceitável — e o shape real tem burst, que esta medição **não** concede.

> **Limitação declarada.** O Docker Desktop não reproduz a política de burst da OCI. O que foi
> medido é o piso (linha de base sem burst) e o teto (um núcleo moderno inteiro). O comportamento
> real fica entre os dois, mais perto do piso quanto mais sustentada for a carga. Além disso, um
> núcleo desta máquina é mais rápido que um EPYC 7551 de 2,0 GHz — o piso medido ainda é otimista
> nessa direção.

## Prova funcional sob o pior caso

Tudo abaixo rodou com 1 GB e 1/8 de OCPU:

| Prova | Resultado |
|---|---|
| Seis histórias | ✅ `OperacaoNormal`, `RiscoDeAtraso`, `MotoristaOffline`, `TentativaFrustrada`, `EntradaNoGeofence`, `ProvaDeEntrega` |
| PostGIS na fronteira | ✅ 299 m dentro, 301 m fora |
| Estado no banco | 6 entregas, 32 eventos, 21 posições, 5 alertas, 1 ocorrência, 1 comprovante |
| SignalR pelo Caddy | ✅ WebSocket, queda e **reconexão** após reinício da API |
| Comprovante | ✅ autorizar → enviar → HTTP 201, 160 bytes |
| Rastreamento público | ✅ token válido 200, token inválido **404** |
| Persistência | ✅ banco e arquivos idênticos após `down`/`up` — 2 arquivos, 590 bytes |
| Estabilidade | ✅ 0 reinícios, 0 OOM, 0 erros em API e workers |

## Swap: cinto de segurança, não muleta

Com pico de 494 MiB e ~165 MiB de sobra, **a operação normal cabe em RAM**. Swap não é necessário
para funcionar; é seguro para não morrer num transiente.

Se for adotado: 512 MiB em arquivo (`/swapfile`), no volume de inicialização, com
`vm.swappiness=10`. O custo precisa ser dito: o volume de inicialização da OCI é armazenamento de
rede, então cada página que for para o swap vira I/O de rede — lenta, e contada contra o IOPS do
volume. Swap que entra em uso constante é sintoma, não solução; nesse caso o certo é revisar
`shared_buffers` do PostgreSQL, não ampliar o arquivo.

## O que NÃO foi provado

- **O limite de serviço da tenancy para `standard-e2-micro-core-count`.** O usuário técnico
  `torre-capacity-watcher` recebe `NotAuthorizedOrNotFound` em `limits` — por desenho, a política
  de privilégio mínimo nunca concedeu isso — e a sessão humana estava expirada. A Oracle documenta
  2 instâncias por tenancy e o LinkGuardião usa 1, mas **documentação não é medição desta conta**.
- **Capacidade não é cota.** `AVAILABLE` no Compute Capacity Report diz que existe hospedeiro com
  estoque; não diz que esta tenancy tem direito a mais uma instância. São perguntas diferentes.

## Como reproduzir

```bash
# Preencha infra-demo/.env.demo (TORRE_EMAIL_ACME é obrigatório, mesmo em http)
bash infra-demo/scripts/validar-pilha-sob-e2.sh                                    # CPU compartilhada
bash infra-demo/scripts/validar-pilha-sob-e2.sh infra-demo/compose.e2-pior-caso.yml pior-caso
```

---

# Vigília de capacidade — `scripts/vigiar-capacidade-oci.ps1`

## Por que existe

A máquina Ampere A1 é o único recurso deste plano que pode ser recusado por motivo que não é cota
nem custo: **falta de estoque**. Foram **38 tentativas de `terraform apply` em cinco janelas**, todas
com `500-InternalError, Out of host capacity`, com 2 OCPU e com 1 OCPU.

Tentar criar para descobrir se dá é caro e cego: cada ciclo gasta uma chamada de criação e só
responde sim ou não. A Oracle oferece a pergunta direta — o **Compute Capacity Report** — que diz se
existe capacidade para um shape específico num domínio de disponibilidade **sem criar nada**.

A vigília pergunta a cada 5 minutos e só chama o Terraform quando a resposta é `AVAILABLE`.

## Identidade própria, com privilégio mínimo

A vigília **não** usa a sessão humana. Ela tem usuário técnico, grupo e chave próprios:

| | |
|---|---|
| Usuário | `torre-capacity-watcher` — sem senha de console |
| Grupo | `torre-capacity-watchers` |
| Política | `torre-capacity-watch`, na raiz da tenancy |
| Chave | par RSA 2048 exclusivo, fora do repositório |
| Perfil | `TORRE_WATCH`, em arquivo de configuração **próprio** |

As cinco declarações da política:

```text
Allow group torre-capacity-watchers to manage instance-family        in compartment torre-demo
Allow group torre-capacity-watchers to use    volume-family          in compartment torre-demo
Allow group torre-capacity-watchers to use    virtual-network-family in compartment torre-demo
Allow group torre-capacity-watchers to manage compute-capacity-reports in tenancy
Allow group torre-capacity-watchers to read   instance-images        in tenancy
```

A quinta foi acrescentada porque o teste provou que faltava — o Terraform resolve a imagem do Ubuntu
por consulta na raiz da tenancy, e sem ela o `plan` morria com `404 NotAuthorizedOrNotFound`. Foi a
única adição; nada foi ampliado por conveniência.

### Onde o perfil mora, e por quê

O perfil `TORRE_WATCH` fica em **`~/.oci/config`**, ao lado do `DEFAULT`, e a chave privada em
`~/.oci/torre-watch/`.

Ele já esteve num arquivo separado, por um diagnóstico meu que estava **errado**: eu atribuí uma
falha de `NotAuthenticated` à herança da seção `[DEFAULT]` do `ConfigParser`. Medido depois, com o
perfil de volta no arquivo padrão: **0 falhas em 10 chamadas**. A causa original era propagação da
chave recém-criada, não herança.

E o arquivo separado tinha um custo que só apareceu quando a sessão humana expirou: **o provedor do
Terraform não honra `OCI_CLI_CONFIG_FILE`**. Ele lê `~/.oci/config` e mais nada. Com o perfil fora
dali, o `terraform plan` simplesmente usava o `DEFAULT` — e passava, porque a sessão humana ainda
valia. O teste tinha passado **pelo motivo errado**.

## O que a vigília pode e o que não pode

Medido, não presumido — 13 verificações contra a conta real:

| Deve permitir | Resultado | | Deve negar | Resultado |
|---|---|---|---|---|
| Compute Capacity Report | ✅ | | instâncias na raiz (LinkGuardião) | ✅ negado |
| ler sub-rede da Torre | ✅ | | VCN do LinkGuardião | ✅ negado |
| ler VCN da Torre | ✅ | | volumes de boot na raiz | ✅ negado |
| ler instâncias da Torre | ✅ | | usuários da tenancy | ✅ negado |
| ler imagens da plataforma | ✅ | | políticas | ✅ negado |
| domínios de disponibilidade | ✅ | | criar usuário IAM | ✅ negado |
| | | | **apagar a VCN da própria Torre** | ✅ negado |

A última linha merece atenção: `use virtual-network-family` deixa usar a rede e **não** deixa
destruí-la. A vigília cria a máquina e não consegue desfazer o resto.

> **Uma medição enganou antes de acertar.** A primeira matriz acusou três negações falsas. A causa
> era propagação: política e chave de API recém-criadas levam dezenas de segundos para valer em toda
> a região, e nesse intervalo a mesma chamada alterna entre 200 e 401. Medido depois: **0 falhas em
> 40 chamadas**. Conclusão tirada cedo demais teria culpado a política.

## Como funciona cada ciclo

```text
Compute Capacity Report
      │
      ├── OUT_OF_HOST_CAPACITY ──► registra e dorme 15 min   (Terraform NÃO é chamado)
      │
      └── AVAILABLE
              │
              ├─► terraform plan -target=oci_core_instance.torre -out=torre-capacidade.tfplan
              │
              ├─► confere o plano: precisa ser EXATAMENTE 1 mudança, e ela precisa ser
              │   `create` de `oci_core_instance`. Qualquer outra coisa PARA a vigília.
              │
              └─► terraform apply <aquele mesmo plano>
                      │
                      ├── sucesso ─────────────► PARA. A VM existe; nada é configurado.
                      ├── "Out of host capacity" ─► a capacidade sumiu entre o relatório e a
                      │                             criação. Esperado. Volta a vigiar.
                      └── outro erro ──────────► PARA por segurança.
```

Não há geração de plano entre a conferência e a aplicação — é onde uma diferença entraria sem
ninguém ver.

## Uso

```powershell
# Um ciclo, sem criar nada — confere o mecanismo
.\vigiar-capacidade-oci.ps1 -CompartimentoOcid <ocid do torre-demo> -CiclosMaximos 1

# Exercita o ramo AVAILABLE sem capacidade real e sem criar VM
.\vigiar-capacidade-oci.ps1 -CompartimentoOcid <ocid> -CiclosMaximos 1 -Simular -EstadoSimulado AVAILABLE

# Vigília de 12 horas, a cada 5 minutos (padrão)
.\vigiar-capacidade-oci.ps1 -CompartimentoOcid <ocid>
```

`-EstadoSimulado` só é aceito junto com `-Simular`. Sem essa trava, um valor errado faria a vigília
tentar criar acreditando numa capacidade que não existe.

## Duas decisões do Terraform que o privilégio mínimo obrigou

### `-var` e não `TF_VAR_`

Em Terraform, **`terraform.tfvars` tem precedência sobre variáveis de ambiente `TF_VAR_*`**. O
`tfvars` deste projeto fixa `perfil_da_cli = "DEFAULT"` e `metodo_de_autenticacao = "SecurityToken"`,
então definir `TF_VAR_perfil_da_cli` não mudava nada — o plano rodava com a sessão humana. A vigília
passa os dois por `-var`, que vence tudo.

### `-refresh=false`

Não é atalho: é consequência direta do privilégio mínimo.

O usuário técnico **não lê** o compartimento, o orçamento nem a cota — e não deve mesmo. Num plano
com refresh, o Terraform interpreta "não consigo ler" como "não existe" e propõe **recriar**. Medido:

```text
Plan: 2 to add, 5 to change, 0 to destroy
  identity_compartment.torre    create   ← compartimento DUPLICADO
  core_vcn.torre                update
  core_subnet.torre             update
  core_internet_gateway.torre   update
  core_route_table.torre        update
  core_security_list.torre      update
```

O gate barrou e a vigília parou — funcionou como devia. Mas a correção certa não é ampliar a
permissão para o plano ficar bonito: é **não pedir ao vigia que reavalie o mundo**. Sem refresh, ele
planeja contra o estado que o administrador já validou, e o único recurso ausente é a instância:

```text
Plan: 1 to add, 0 to change, 0 to destroy
  core_instance.torre  create | VM.Standard.A1.Flex 1 OCPU 4 GB boot 50 GB
```

## Duas armadilhas de codificação que custaram tempo

Ficam registradas porque a segunda desmente a primeira, e o par é fácil de repetir:

| Arquivo | BOM | Por quê |
|---|---|---|
| `vigiar-capacidade-oci.ps1` | **obrigatório** | sem BOM, o PowerShell 5.1 lê o arquivo como Windows-1252. O travessão `—` (`E2 80 94`) vira três caracteres, e o último é `"` — que o PowerShell aceita como aspa. Doze travessões nos comentários abriam doze strings fantasma, e o erro aparecia cem linhas adiante |
| `shape-consultado.json` | **proibido** | `Set-Content -Encoding utf8` do PowerShell 5.1 grava com BOM, e a CLI da Oracle recusa com `Parameter 'shape_availabilities' must be in JSON format` — o conteúdo está certo, o prefixo é que atrapalha |

O mesmo byte, exigências opostas, dois arquivos a um metro de distância.

## Depois que a VM existir — feito em 28/09/2026

Credencial de automação que sobrevive à tarefa é credencial esquecida. A identidade da vigília foi
removida no mesmo dia em que a VM entrou no ar:

| Artefato | Situação |
|---|---|
| Chave de API `ef:cb:76:…` | revogada |
| Vínculo usuário → grupo | removido |
| Usuário `torre-capacity-watcher` | removido |
| Grupo `torre-capacity-watchers` | removido |
| Política `torre-capacity-watch` | removida |
| `~/.oci/torre-watch/` | chaves sobrescritas com bytes aleatórios e o diretório apagado |
| Seção `[TORRE_WATCH]` do `~/.oci/config` | removida; `[DEFAULT]` preservado |

Restou na tenancy apenas a conta humana, os dois grupos nativos e a política de administração.

### Três cuidados que a remoção exigiu

**Resolver por OCID, não por nome.** Antes de apagar, o grupo foi consultado para confirmar que
tinha **um único** membro, o usuário para confirmar que pertencia a **um único** grupo, e todas as
políticas da tenancy foram varridas para provar que nenhuma outra citava
`torre-capacity-watchers`. Apagar por nome sem essa checagem é como cortar um fio pela cor.

**A revogação propaga, e demora.** Logo após o `api-key delete`, seis tentativas de leitura com a
credencial deram **2 sucessos e 4 negações**; noventa segundos depois, mais 8 tentativas deram 2 e
6, com as **seis últimas seguidas negando**. É o mesmo atraso regional que enganou a matriz de
privilégio mínimo quando a chave foi *criada*, agora na direção oposta. Concluir pela primeira
resposta teria produzido a conclusão errada nas duas pontas. O que encerra a dúvida não é a
revogação isolada: é o usuário deixar de existir.

**O `[DEFAULT]` do `ConfigParser` é herdado.** Listar a seção `[TORRE_WATCH]` mostrava
`security_token_file`, que na verdade vinha do `[DEFAULT]`. Reescrever o arquivo com um parser
teria materializado essa herança dentro de outra seção. A remoção foi textual, linha a linha, e o
`[DEFAULT]` saiu com as mesmas cinco chaves que tinha.

O script `scripts/vigiar-capacidade-oci.ps1` **permanece versionado**: ele não contém credencial
nenhuma e documenta como o problema de capacidade foi resolvido. O log fica de fora pelo `*.log`
do `.gitignore`.
