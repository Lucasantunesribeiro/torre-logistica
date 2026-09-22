# Infraestrutura

> **Nada aqui foi aplicado.** Estes arquivos descrevem o que seria criado. Provisionar cobra, e cobrar
> exige autorização explícita — a desta fase foi para *escrever e validar*, não para *aplicar*.

## O que existe

| Arquivo | O que descreve |
|---|---|
| `main.bicep` | ambiente de contêiner, **duas** aplicações (API e workers), PostgreSQL com PostGIS, registro das imagens, armazenamento privado, cofre e workspace de log |
| `parametros.exemplo.json` | os parâmetros; a senha **não aparece** — ela vem de fora |

**Região pretendida: East US.** A comparação com Brazil South, com preços do dia, está em
[`docs/cost-model.md`](../docs/cost-model.md#4-região-east-us): o compute do Container Apps custa igual nas
duas, e a diferença se concentra no banco, que é justamente a parcela que roda 730 horas por mês.

## Dois processos, e por quê

A API e os workers são aplicações separadas porque fazem coisas diferentes:

| | API | Workers |
|---|---|---|
| Ingress | HTTPS público | **nenhum** — não há porta por onde alcançá-lo |
| Por que fica viva | conexão persistente do console (SignalR, ADR 0017) | os laços só existem enquanto o processo vive |
| `minReplicas` | 1 | 1 |
| `maxReplicas` | 1 (ver abaixo) | 1 (dois processos disputariam o mesmo outbox sem ganho) |
| CPU / memória | 0,25 vCPU / 0,5 GiB | 0,25 vCPU / 0,5 GiB |
| O que executa | borda HTTP, autenticação, consultas, tempo real | outbox, reavaliação de previsão, alertas, retenção e medidas |

Juntos num processo só — como estavam até esta fase — os dois se amarravam: a API não podia escalar sem
duplicar o trabalho de fundo, e o trabalho de fundo não podia parar sem derrubar a borda.

### O caminho para escalar a API além de uma réplica

Hoje `maxReplicas` é 1 por causa do SignalR: com duas instâncias, consoles conectados a instâncias
diferentes receberiam avisos diferentes, e um operador que não vê o alerta é pior que um sem tempo real.

O caminho, quando o tráfego justificar — e **só então**:

1. Ligar um backplane para o SignalR (Azure SignalR Service em modo *Default*, ou Redis). É o que faz o
   aviso publicado numa instância chegar às conexões das outras.
2. Subir `maxReplicas` da API. Nada mais precisa mudar: os workers já estão de fora, então escalar a
   borda não multiplica o trabalho de fundo.

A ordem é essa. Subir a réplica antes do backplane produz um sistema que parece funcionar e mente para
metade dos operadores.

### Retenção: avaliada para Container Apps Job, mantida no processo de trabalho

A limpeza roda a cada 6 horas, o que é cara de tarefa agendada. Um `Microsoft.App/jobs` com `cron` seria a
forma canônica — e foi recusado por um motivo simples: **os workers já estão sempre vivos** por causa do
outbox, que roda a cada 5 segundos. Um Job traria uma terceira imagem, um terceiro recurso e um terceiro
caminho de configuração para executar trabalho que o processo existente faz em uma rodada de 12 ms
(medido em `docs/performance.md`).

O gatilho para rever: se o outbox um dia virar fila nativa com escala por evento, o processo de trabalho
deixa de precisar estar sempre vivo — e aí a retenção passa a valer como Job.

## Validação executada

A Azure CLI não está instalada nesta máquina. Em vez de instalar um MSI de sistema, foi baixado o **Bicep
CLI standalone** (executável único, oficial, gratuito) para o diretório temporário da sessão:

```text
Bicep CLI version 0.47.16 (3f73e1a234)
```

| Verificação | Comando | Resultado |
|---|---|---|
| Compilação | `bicep build infra/main.bicep` | **0 erros, 0 avisos** |
| Lint | `bicep lint infra/main.bicep` | **sem achados** |
| Recursos gerados | inspeção do ARM compilado | **13 recursos**, listados abaixo |
| Segredo fixo | varredura por padrões de senha/chave | **nenhum**; `senhaDoBanco` é `securestring` e não consta do arquivo de exemplo |

### Os 13 recursos que seriam criados

| # | Tipo | SKU / configuração | Versão de API |
|---|---|---|---|
| 1 | `Microsoft.OperationalInsights/workspaces` | PerGB2018, retenção 30 dias | 2023-09-01 |
| 2 | `Microsoft.DBforPostgreSQL/flexibleServers` | **Standard_B1ms** Burstable, PG 17, 64 GiB, backup 7 dias, sem HA | 2024-08-01 |
| 3 | `Microsoft.DBforPostgreSQL/flexibleServers/configurations` | `azure.extensions = POSTGIS` | 2024-08-01 |
| 4 | `Microsoft.DBforPostgreSQL/flexibleServers/databases` | `torre_logistica`, UTF8 | 2024-08-01 |
| 5 | `Microsoft.DBforPostgreSQL/flexibleServers/firewallRules` | serviços do Azure | 2024-08-01 |
| 6 | `Microsoft.Storage/storageAccounts` | **Standard_LRS**, sem acesso público, TLS 1.2 | 2023-05-01 |
| 7 | `Microsoft.Storage/storageAccounts/blobServices` | padrão | 2023-05-01 |
| 8 | `Microsoft.Storage/storageAccounts/blobServices/containers` | `comprovantes`, acesso `None` | 2023-05-01 |
| 9 | `Microsoft.ContainerRegistry/registries` | **Basic**, sem usuário administrador | 2023-11-01-preview |
| 10 | `Microsoft.KeyVault/vaults` | **standard**, RBAC, soft delete 7 dias | 2023-07-01 |
| 11 | `Microsoft.App/managedEnvironments` | consumo (sem workload profile dedicado) | 2024-03-01 |
| 12 | `Microsoft.App/containerApps` — **API** | 0,25 vCPU / 0,5 GiB, min 1, max 1, ingress HTTPS | 2024-03-01 |
| 13 | `Microsoft.App/containerApps` — **workers** | 0,25 vCPU / 0,5 GiB, min 1, max 1, **sem ingress** | 2024-03-01 |

### O armazenamento dos comprovantes, e o que ele ainda não é

O template passava `Torre__Armazenamento__Conta` e `Torre__Armazenamento__Contedor` para a API. **Nenhuma
das duas existia no código**: não há opção com esse nome nem adaptador de Blob. Elas pareciam configuração
e não configuravam nada — a aplicação usava disco local o tempo todo, em silêncio, e o contêiner
`comprovantes` ficaria vazio para sempre.

Agora o provedor é declarado, e a declaração diz a verdade:

```text
Torre__Armazenamento__Provedor                        = local
Torre__Armazenamento__PermitirLocalForaDeDesenvolvimento = true
```

A segunda variável existe para que ninguém faça isso sem perceber: sem ela o processo **recusa subir** em
produção, porque comprovante gravado em disco de contêiner **some em qualquer reinício ou nova revisão**.
Para a demonstração — em que o simulador reencena tudo — é aceitável. Para operação real, não é.

O recurso 6 (Storage Account) e o contêiner `comprovantes` continuam no template de propósito: são o
destino do adaptador de Blob quando ele existir, e custam praticamente nada vazios. Enquanto isso, pedir
`Provedor=blob` falha na subida com a mensagem certa, em vez de cair no disco local por baixo do pano.

### O registro que faltava

O workflow de deploy já publicava em `<registro>.azurecr.io`, mas o template não criava registro nenhum: a
infraestrutura descrita não era suficiente para o deploy descrito. O preflight desta fase encontrou a
inconsistência e o recurso 9 a corrige.

Basic em vez de Standard porque o que separa os dois é cota inclusa (10 GB contra 100) e banda — duas
imagens de ~200 MB com histórico de tags cabem em 10 GB. Sem usuário administrador: a senha do registro
seria mais um segredo de longa duração, que é o que a identidade gerenciada evita.

### O que **não** foi executado

`az deployment group what-if` **não rodou**, e o motivo não é comodidade: ele exige uma assinatura
autenticada e um grupo de recursos existente. Não há credencial do Azure nesta máquina, e criar o grupo já
seria mexer na conta — fora do que foi autorizado.

O que substitui parcialmente: a compilação valida esquema, tipos, versões de API e referências entre
recursos; a lista acima é extraída do ARM que seria enviado. O que só o `what-if` diria é o *diferencial*
contra um ambiente existente — e não existe ambiente.

## Antes de aplicar

```bash
az bicep build --file infra/main.bicep

az deployment group what-if \
  --resource-group <grupo> \
  --template-file infra/main.bicep \
  --parameters @infra/parametros.exemplo.json \
  --parameters senhaDoBanco="$SENHA" \
               imagemDaApi="<registro>/torre-logistica-api:<versão>" \
               imagemDosWorkers="<registro>/torre-logistica-workers:<versão>"
```

## A ordem do primeiro provisionamento

O template cria o registro **e** as duas aplicações que puxam imagem dele, mas não atribui papel — e sem o
papel `AcrPull` a primeira revisão não consegue puxar. Não é descuido: atribuir papel é operação de
diretório, e o motivo de mantê-la fora está logo abaixo. A consequência é que o primeiro provisionamento
tem três passos, nesta ordem:

```bash
# 1. Cria tudo. As duas aplicações sobem, e a primeira revisão falha ao puxar a imagem.
az deployment group create -g <grupo> --template-file infra/main.bicep --parameters ...

# 2. Dá a cada identidade o direito de puxar.
REGISTRO=$(az acr show -g <grupo> -n <prefixo>registro --query id -o tsv)
for app in api workers; do
  az role assignment create --role AcrPull --scope "$REGISTRO" \
    --assignee-object-id "$(az containerapp show -g <grupo> -n <prefixo>-$app --query identity.principalId -o tsv)" \
    --assignee-principal-type ServicePrincipal
done

# 3. Reaplica. Agora a revisão puxa e sobe.
az deployment group create -g <grupo> --template-file infra/main.bicep --parameters ...
```

Da segunda vez em diante só o passo 3 é necessário. O workflow de deploy assume que os papéis já existem.

## Depois de aplicar

Três coisas que o template **não** faz, de propósito:

1. **Papéis das identidades gerenciadas.** API e workers têm identidade própria e precisam de leitura no
   cofre e de puxar do registro; a API também precisa de escrita no contêiner de comprovantes. Atribuir
   papel é operação de diretório e costuma exigir permissão que uma pipeline de aplicação não deveria ter.
2. **Migrations.** O banco sobe vazio. Aplicar schema é passo de deploy, não de infraestrutura — misturar
   os dois faz um `what-if` de infraestrutura parecer inofensivo quando não é.
3. **Frontends.** As três aplicações web são arquivos estáticos, não guardam segredo e não precisam de
   contêiner.

## O que as imagens já provam

Ambas foram construídas e postas para rodar nesta máquina, contra o banco de desenvolvimento real.

| Prova | API | Workers |
|---|---|---|
| Tamanho | 196 MB | **155 MB** (imagem `runtime`, não `aspnet`: não há servidor HTTP) |
| Usuário | UID 1654 | UID 1654 |
| Portas publicadas | 8080 | **nenhuma** |
| Com banco real | `/health/ready` **200**, migrations aplicadas no arranque | fica vivo e assume os laços |
| Sem banco | `/health/live` **200**, `/health/ready` **503** | encerra de propósito (falha rápida da Fase 0) |
| Laços de fundo | **0 ocorrências** de "Falha na rodada" no log | é quem as registra |

A última linha é a prova da separação: a imagem anterior, com os laços dentro da API, enchia o log de
`Falha na rodada de despacho do outbox` quando o banco estava fora. A imagem atual não registra nenhuma.
