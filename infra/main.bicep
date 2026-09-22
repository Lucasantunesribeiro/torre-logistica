// Infraestrutura da Torre Logística no Azure.
//
// NADA AQUI FOI APLICADO. Este arquivo descreve o que seria criado; criar exige autorização explícita,
// porque a partir do provisionamento há cobrança real.
//
// Para conferir antes de qualquer coisa:
//   az bicep build --file infra/main.bicep
//   az deployment group what-if --resource-group <grupo> --template-file infra/main.bicep --parameters ...
//
// A escolha de Container Apps em vez de um serviço que escala a zero está em docs/cost-model.md, e a
// razão é operacional: o processo que atende HTTP é o mesmo que avalia SLA, despacha webhook e apaga
// rastro vencido. Um serviço que dorme deixa de fazer tudo isso.

targetScope = 'resourceGroup'

@description('Prefixo dos nomes. Curto: alguns recursos do Azure têm limite baixo de caracteres.')
@minLength(3)
@maxLength(12)
param prefixo string = 'torrelog'

@description('Região. Todos os recursos ficam juntos — tráfego entre regiões é cobrado e lento.')
param regiao string = resourceGroup().location

@description('Usuário administrador do PostgreSQL.')
param usuarioDoBanco string = 'torre'

@description('Senha do administrador do PostgreSQL. Vem de fora, nunca do repositório.')
@secure()
@minLength(16)
param senhaDoBanco string

@description('Imagem da API, já publicada num registro acessível.')
param imagemDaApi string

@description('Imagem do processo de trabalho, já publicada num registro acessível.')
param imagemDosWorkers string

@description('Origens que o console e a PWA usam, separadas por vírgula.')
param origensPermitidas string

// ---------------------------------------------------------------------------
// Observabilidade
// ---------------------------------------------------------------------------
// O ambiente do Container Apps exige um workspace. Trinta dias de retenção: log de aplicação além
// disso responde pergunta que ninguém faz mais, e cada GB retido é cobrado.
resource analise 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefixo}-logs'
  location: regiao
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// ---------------------------------------------------------------------------
// Banco
// ---------------------------------------------------------------------------
resource banco 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: '${prefixo}-pg'
  location: regiao
  sku: {
    // A carga medida cabe folgada: 33 requisições por segundo com p50 de 10 ms (docs/performance.md).
    // O que dita o tamanho aqui é memória para cache, não CPU.
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '17'
    administratorLogin: usuarioDoBanco
    administratorLoginPassword: senhaDoBanco
    storage: {
      // 30 GB é o regime medido: ~1 GB por dia com a retenção de 30 dias da Fase 20. 64 GB dá margem
      // para o crescimento antes do primeiro expurgo e para índice em reconstrução.
      storageSizeGB: 64
      autoGrow: 'Enabled'
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: { mode: 'Disabled' }
  }
}

// PostGIS não vem ligado: é extensão que precisa ser permitida no servidor antes de o CREATE EXTENSION
// da migration funcionar. Sem isto, a aplicação sobe e falha na primeira consulta geográfica.
resource extensoes 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: banco
  name: 'azure.extensions'
  properties: {
    value: 'POSTGIS'
    source: 'user-override'
  }
}

resource bancoDaAplicacao 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: banco
  name: 'torre_logistica'
  properties: {
    charset: 'UTF8'
    collation: 'pt_BR.utf8'
  }
}

// O Container App sai por endereço público. Esta regra libera serviços do Azure — é o mínimo que
// funciona sem VNet. Com rede privada, some daqui e vira integração de VNet, que custa mais.
resource acessoDeServicosDoAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: banco
  name: 'permitir-servicos-do-azure'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// ---------------------------------------------------------------------------
// Armazenamento dos comprovantes
// ---------------------------------------------------------------------------
resource armazenamento 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: '${prefixo}arquivos'
  location: regiao
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    // Comprovante é foto de quem recebeu, no endereço de quem recebeu. Nunca público (ADR 0023).
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource servicoDeBlob 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: armazenamento
  name: 'default'
}

resource contedorDeComprovantes 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: servicoDeBlob
  name: 'comprovantes'
  properties: {
    publicAccess: 'None'
  }
}

// ---------------------------------------------------------------------------
// Registro das imagens
// ---------------------------------------------------------------------------
// O workflow de deploy já publicava em `<registro>.azurecr.io` e este template não criava registro
// nenhum: a infraestrutura descrita não era suficiente para o deploy descrito. Corrigido aqui.
//
// Basic, e não Standard: o que muda entre os dois é cota de armazenamento incluso (10 GB contra 100) e
// banda. Duas imagens de ~200 MB com histórico de tags cabem em 10 GB com folga.
//
// Sem usuário administrador: a senha do registro seria mais um segredo de longa duração para guardar,
// justamente o que a identidade gerenciada existe para evitar.
resource registro 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: '${prefixo}registro'
  location: regiao
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

// ---------------------------------------------------------------------------
// Segredos
// ---------------------------------------------------------------------------
// Cofre com RBAC em vez de política de acesso: identidade gerenciada recebe papel, e ninguém guarda
// credencial para ler credencial.
resource cofre 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: '${prefixo}-cofre'
  location: regiao
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

// ---------------------------------------------------------------------------
// Aplicação
// ---------------------------------------------------------------------------
resource ambiente 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefixo}-ambiente'
  location: regiao
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: analise.properties.customerId
        sharedKey: analise.listKeys().primarySharedKey
      }
    }
  }
}

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${prefixo}-api'
  location: regiao
  identity: {
    // Identidade gerenciada: a aplicação lê o cofre sem carregar segredo para ler segredo.
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: ambiente.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        // 'auto' negocia HTTP/1.1 e HTTP/2 e mantém a conexão aberta: é o que o SignalR precisa
        // (ADR 0017). Com transporte que encerra a conexão, o console cairia a cada aviso.
        transport: 'auto'
        allowInsecure: false
      }
      // Puxa a imagem com a identidade gerenciada, não com senha de registro. Exige o papel AcrPull,
      // atribuído depois do provisionamento — a ordem está em infra/README.md.
      registries: [
        {
          server: registro.properties.loginServer
          identity: 'system'
        }
      ]
      secrets: [
        {
          name: 'cadeia-de-conexao'
          value: 'Host=${banco.properties.fullyQualifiedDomainName};Database=${bancoDaAplicacao.name};Username=${usuarioDoBanco};Password=${senhaDoBanco};SSL Mode=Require;Trust Server Certificate=true'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: imagemDaApi
          resources: {
            // Medido, não estimado. Com o simulador encenando as seis histórias e seis consoles lendo ao
            // mesmo tempo, esta imagem limitada a 0,25 vCPU / 512 MiB atendeu 2.215 requisições sem erro,
            // com p50 de 8 ms e p95 de 51 ms — os mesmos números de 0,5 vCPU / 1 GiB — usando 181 MiB.
            // O que dobra ao reduzir é só o arranque (9 s → 18 s), e com minReplicas 1 isso acontece em
            // deploy, não em visita. Subir de volta é esta linha; o gatilho está em docs/cost-model.md.
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'Torre__BancoDeDados__CadeiaDeConexao', secretRef: 'cadeia-de-conexao' }
            { name: 'Torre__Cors__OrigensPermitidas__0', value: split(origensPermitidas, ',')[0] }
            // Comprovantes no Blob privado, com identidade gerenciada. Nenhuma chave de conta: a SAS de
            // leitura é assinada com chave de delegação pedida ao Azure em nome desta identidade, então
            // não há segredo permanente para guardar nem para vazar.
            { name: 'Torre__Armazenamento__Provedor', value: 'blob' }
            { name: 'Torre__Armazenamento__Blob__Conta', value: armazenamento.name }
            { name: 'Torre__Armazenamento__Blob__Contedor', value: contedorDeComprovantes.name }
            { name: 'Torre__Armazenamento__Blob__Autenticacao', value: 'identidade-gerenciada' }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: 8080 }
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: 8080 }
              periodSeconds: 15
            }
          ]
        }
      ]
      scale: {
        // Mínimo de 1 pela conexão persistente: o console mantém SignalR aberto (ADR 0017), e uma
        // réplica que dorme derruba todos os consoles conectados. O trabalho de fundo, que antes também
        // exigia isto, agora mora na aplicação de workers.
        minReplicas: 1
        // Teto baixo de propósito: o backplane do SignalR ainda não existe (ADR 0017), então duas
        // réplicas atenderiam consoles diferentes sem compartilhar aviso.
        maxReplicas: 1
      }
    }
  }
}

// ---------------------------------------------------------------------------
// Processo de trabalho
// ---------------------------------------------------------------------------
// Aplicação separada da API, e a separação é o ponto: o que este processo faz não depende de requisição
// nenhuma. Ele despacha o outbox, reavalia previsão, abre alerta, apaga rastro vencido e mede o estado
// da operação — de madrugada, com o console fechado.
//
// Juntos num processo só, os dois se amarrariam: a API não poderia escalar sem duplicar o trabalho de
// fundo, e o trabalho de fundo não poderia parar sem derrubar a borda.
resource workers 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${prefixo}-workers'
  location: regiao
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: ambiente.id
    configuration: {
      // Sem bloco de ingress: nenhuma porta é publicada, e não há endereço por onde alcançar este
      // processo de fora. Um erro de regra de rede não consegue expor o que não tem entrada.
      registries: [
        {
          server: registro.properties.loginServer
          identity: 'system'
        }
      ]
      secrets: [
        {
          name: 'cadeia-de-conexao'
          value: 'Host=${banco.properties.fullyQualifiedDomainName};Database=${bancoDaAplicacao.name};Username=${usuarioDoBanco};Password=${senhaDoBanco};SSL Mode=Require;Trust Server Certificate=true'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'workers'
          image: imagemDosWorkers
          resources: {
            // Metade da CPU da API: aqui não há pico de requisição, e sim trabalho periódico em lote.
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
            { name: 'Torre__BancoDeDados__CadeiaDeConexao', secretRef: 'cadeia-de-conexao' }
            // Os workers registram a mesma camada de infraestrutura, então a validação de subida cobra
            // deles a mesma declaração de armazenamento — mesmo sem gravarem comprovante.
            // Nenhuma variável de armazenamento aqui, e isso é a consequência visível da divisão do
            // composition root: o processo de trabalho não registra storage, então não precisa saber
            // dele — nem de conta, nem de contêiner, nem de chave de assinatura de URL. Por isso
            // também não recebe papel no Storage (menor privilégio).
          ]
        }
      ]
      scale: {
        // Um, e exatamente um. Mínimo 1 porque os laços só existem enquanto o processo vive; máximo 1
        // porque dois processos despachando o mesmo outbox dobrariam o trabalho — o arrendamento com
        // FOR UPDATE SKIP LOCKED (ADR 0026) evitaria entrega duplicada, mas duplicaria a disputa por
        // linha sem ganho nenhum no volume deste projeto.
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

// ---------------------------------------------------------------------------
// Permissões
// ---------------------------------------------------------------------------
// Até esta fase o template não atribuía papel nenhum, e o motivo registrado era que atribuir papel é
// operação de diretório. A decisão mudou por uma razão concreta: sem papel, a API não consegue nem puxar
// a própria imagem nem assinar leitura de comprovante, e o primeiro provisionamento virava um
// procedimento de três passos que alguém ia esquecer. Quem aplica o template precisa, portanto, de
// permissão para atribuir papel no grupo de recursos — `User Access Administrator` ou `Owner`.
//
// Escopo mínimo em cada caso: papel de dado fica no CONTÊINER, não na conta.

@description('Storage Blob Data Contributor — ler, gravar e alterar metadado dos blobs.')
var papelDeDadosDeBlob = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'ba92f5b4-2d11-453d-a403-e96b0029c9fe')

@description('Storage Blob Delegator — obter a chave de delegação que assina as SAS.')
var papelDeDelegacao = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'db58b8e5-c6ad-4a2a-8342-4190687cbf4a')

@description('AcrPull — puxar imagem do registro.')
var papelDePullDoRegistro = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '7f951dda-4ed3-4680-a7ca-43fe172d538d')

// A API escreve metadado no blob (o resumo SHA-256 calculado uma vez), então leitura pura não basta.
// O escopo é o contêiner `comprovantes`: nada de outro contêiner que a conta venha a ter.
resource apiEscreveComprovantes 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: contedorDeComprovantes
  name: guid(contedorDeComprovantes.id, api.id, papelDeDadosDeBlob)
  properties: {
    roleDefinitionId: papelDeDadosDeBlob
    principalId: api.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// A chave de delegação é operação de CONTA — não existe versão por contêiner. É o único papel aqui com
// escopo de conta, e ele não dá acesso a dado nenhum por si só: só permite pedir a chave que assina SAS,
// e a SAS resultante nunca ultrapassa o que a identidade já podia fazer.
resource apiAssinaLeitura 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: armazenamento
  name: guid(armazenamento.id, api.id, papelDeDelegacao)
  properties: {
    roleDefinitionId: papelDeDelegacao
    principalId: api.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Os workers NÃO recebem papel no Storage: eles nunca resolvem IObjectStorage. Se um dia passarem a
// gravar ou ler comprovante, a falta aparece como 403 do Azure — e é aqui que se corrige.
resource apiPuxaImagem 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registro
  name: guid(registro.id, api.id, papelDePullDoRegistro)
  properties: {
    roleDefinitionId: papelDePullDoRegistro
    principalId: api.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource workersPuxamImagem 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registro
  name: guid(registro.id, workers.id, papelDePullDoRegistro)
  properties: {
    roleDefinitionId: papelDePullDoRegistro
    principalId: workers.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

@description('Endereço público da API.')
output enderecoDaApi string = 'https://${api.properties.configuration.ingress.fqdn}'

@description('Identidade da aplicação, para receber papéis no cofre e no armazenamento.')
output identidadeDaApi string = api.identity.principalId

@description('Identidade do processo de trabalho, para receber papéis no cofre e no armazenamento.')
output identidadeDosWorkers string = workers.identity.principalId

@description('Servidor do banco.')
output servidorDoBanco string = banco.properties.fullyQualifiedDomainName

@description('Registro das imagens, para onde o workflow publica e de onde as aplicações puxam.')
output servidorDoRegistro string = registro.properties.loginServer
