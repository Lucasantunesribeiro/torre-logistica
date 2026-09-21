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
            // Medido: a carga alvo cabe nisto com folga. Subir daqui é decisão com número, não palpite.
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'Torre__BancoDeDados__CadeiaDeConexao', secretRef: 'cadeia-de-conexao' }
            { name: 'Torre__Cors__OrigensPermitidas__0', value: split(origensPermitidas, ',')[0] }
            { name: 'Torre__Armazenamento__Conta', value: armazenamento.name }
            { name: 'Torre__Armazenamento__Contedor', value: contedorDeComprovantes.name }
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

@description('Endereço público da API.')
output enderecoDaApi string = 'https://${api.properties.configuration.ingress.fqdn}'

@description('Identidade da aplicação, para receber papéis no cofre e no armazenamento.')
output identidadeDaApi string = api.identity.principalId

@description('Identidade do processo de trabalho, para receber papéis no cofre e no armazenamento.')
output identidadeDosWorkers string = workers.identity.principalId

@description('Servidor do banco.')
output servidorDoBanco string = banco.properties.fullyQualifiedDomainName
