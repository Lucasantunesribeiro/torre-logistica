# Infraestrutura

> **Nada aqui foi aplicado.** Estes arquivos descrevem o que seria criado. Provisionar cobra, e cobrar
> exige autorização explícita — a desta fase foi para *escrever e validar*, não para *aplicar*.

## O que existe

| Arquivo | O que descreve |
|---|---|
| `main.bicep` | ambiente de contêiner, **duas** aplicações (API e workers), PostgreSQL com PostGIS, armazenamento privado, cofre e workspace de log |
| `parametros.exemplo.json` | os parâmetros; a senha **não aparece** — ela vem de fora |

## Dois processos, e por quê

A API e os workers são aplicações separadas porque fazem coisas diferentes:

| | API | Workers |
|---|---|---|
| Ingress | HTTPS público | **nenhum** — não há porta por onde alcançá-lo |
| Por que fica viva | conexão persistente do console (SignalR, ADR 0017) | os laços só existem enquanto o processo vive |
| `minReplicas` | 1 | 1 |
| `maxReplicas` | 1 (ver abaixo) | 1 (dois processos disputariam o mesmo outbox sem ganho) |
| CPU / memória | 0,5 vCPU / 1 GiB | 0,25 vCPU / 0,5 GiB |
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
| Recursos gerados | inspeção do ARM compilado | **12 recursos**, listados abaixo |
| Segredo fixo | varredura por padrões de senha/chave | **nenhum**; `senhaDoBanco` é `securestring` e não consta do arquivo de exemplo |

### Os 12 recursos que seriam criados

| # | Tipo | Versão de API |
|---|---|---|
| 1 | `Microsoft.OperationalInsights/workspaces` | 2023-09-01 |
| 2 | `Microsoft.DBforPostgreSQL/flexibleServers` | 2024-08-01 |
| 3 | `Microsoft.DBforPostgreSQL/flexibleServers/configurations` (PostGIS) | 2024-08-01 |
| 4 | `Microsoft.DBforPostgreSQL/flexibleServers/databases` | 2024-08-01 |
| 5 | `Microsoft.DBforPostgreSQL/flexibleServers/firewallRules` | 2024-08-01 |
| 6 | `Microsoft.Storage/storageAccounts` | 2023-05-01 |
| 7 | `Microsoft.Storage/storageAccounts/blobServices` | 2023-05-01 |
| 8 | `Microsoft.Storage/storageAccounts/blobServices/containers` | 2023-05-01 |
| 9 | `Microsoft.KeyVault/vaults` | 2023-07-01 |
| 10 | `Microsoft.App/managedEnvironments` | 2024-03-01 |
| 11 | `Microsoft.App/containerApps` — **API** | 2024-03-01 |
| 12 | `Microsoft.App/containerApps` — **workers** | 2024-03-01 |

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

## Depois de aplicar

Três coisas que o template **não** faz, de propósito:

1. **Papéis das identidades gerenciadas.** API e workers têm identidade própria e precisam de leitura no
   cofre; a API também precisa de escrita no contêiner de comprovantes. Atribuir papel é operação de
   diretório e costuma exigir permissão que uma pipeline de aplicação não deveria ter.
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
