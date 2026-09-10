# CLAUDE.md — Torre Logística

> Documento de governança técnica e operacional do projeto.
> Este arquivo é fonte de verdade para decisões arquiteturais, invariantes, padrões de implementação, critérios de qualidade e autonomia do agente.
>
> O agente deve ler este arquivo e o `ROADMAP.md` integralmente antes de qualquer alteração relevante.

---

# 1. Identidade do projeto

**Nome:** Torre Logística  
**Tipo:** plataforma B2B de operação logística em tempo real  
**Objetivo principal:** acompanhar e operar entregas entre a saída para rota e a conclusão, com localização, ETA, SLA, geofencing, alertas, ocorrências, prova de entrega e rastreamento público controlado.  
**Objetivo de portfólio:** ser o projeto tecnicamente mais completo do portfólio, com engenharia de produção realista, UX operacional forte e demo pública convincente.  
**Objetivo de produto:** não é um SaaS comercial ativo, mas deve ser arquitetado e implementado de forma suficientemente séria para poder evoluir para um produto comercial sem reescrita estrutural.

A Torre Logística não é uma landing page, não é um projeto acadêmico e não é um clone de Uber. Ela deve se comportar como software operacional real.

---

# 2. Posicionamento do produto

A Torre Logística é uma plataforma para empresas que precisam acompanhar e controlar entregas locais ou regionais, principalmente com frota própria ou operação de última milha.

Fluxo principal esperado:

```text
Entrega criada
→ planejamento
→ atribuição ao motorista
→ saída para rota
→ localização em tempo real
→ cálculo de ETA
→ acompanhamento de SLA
→ alertas e ocorrências
→ chegada ao destino
→ prova de entrega
→ conclusão
→ histórico operacional
```

O produto deve permitir que um recrutador ou avaliador entre na aplicação, explore uma operação já acontecendo e entenda rapidamente o valor do sistema.

---

# 3. O que o projeto deve provar

A Torre Logística deve demonstrar competência prática em:

- C# / .NET 10 / ASP.NET Core;
- modelagem de domínio e máquinas de estado;
- PostgreSQL + PostGIS;
- geolocalização e geofencing;
- ingestão de telemetria GPS;
- eventos fora de ordem;
- idempotência;
- concorrência e conflitos de estado;
- PWA e operação offline;
- sincronização posterior;
- SignalR / tempo real;
- filas e processamento assíncrono;
- Transactional Outbox e Inbox quando aplicável;
- armazenamento de arquivos com URLs assinadas;
- segurança multi-tenant;
- privacidade de localização;
- rastreamento público por token;
- observabilidade e métricas operacionais;
- testes geoespaciais, concorrentes, offline e de performance;
- simulação determinística de uma operação logística realista;
- deploy e troubleshooting em ambiente real.

O projeto não deve adicionar tecnologia apenas para aumentar a lista de ferramentas do README.

---

# 4. Escopo funcional principal

A solução possui três experiências distintas.

## 4.1 Console Operacional

Aplicação para Administrador, Supervisor e Operador.

Deve cobrir, conforme evolução do roadmap:

- painel operacional;
- mapa da operação;
- entregas;
- detalhe da entrega;
- motoristas;
- veículos;
- rotas;
- paradas;
- hubs;
- alertas operacionais;
- ocorrências;
- SLA e ETA;
- timeline;
- prova de entrega;
- indicadores operacionais;
- auditoria;
- usuários e permissões;
- integrações;
- configurações.

## 4.2 PWA do Motorista

Interface separada da aplicação administrativa.

Deve ser simples, mobile-first e orientada a execução:

```text
rota do dia
→ próxima parada
→ navegação
→ chegada
→ entrega concluída
ou
→ tentativa frustrada / ocorrência
→ comprovante
```

Deve suportar operação offline nas partes previstas no roadmap.

## 4.3 Rastreamento Público

Página pública, acessada por token forte, destinada ao destinatário da entrega.

Deve mostrar somente informações adequadas ao contexto público, sem expor dados administrativos ou localização sensível desnecessária.

---

# 5. Fora de escopo

Não implementar, salvo alteração explícita posterior do produto:

- landing page comercial;
- planos, billing, checkout ou assinatura;
- marketplace de fretes;
- cálculo de frete;
- emissão de NF-e;
- ERP completo;
- WMS;
- estoque;
- contas a pagar ou receber;
- folha de pagamento de motoristas;
- gestão de combustível;
- telemetria CAN/OBD;
- roteirização ótima VRP avançada;
- app nativo Android/iOS;
- chat;
- pagamentos;
- pricing comercial;
- CRM;
- funcionalidades de marketing.

A PWA do motorista não deve crescer até se transformar em um aplicativo de transporte completo.

---

# 6. Arquitetura macro

A direção arquitetural é:

```text
monólito modular no núcleo
+
workers separados para cargas assíncronas
+
console operacional web
+
PWA do motorista
+
rastreamento público
+
simulador separado
```

Não migrar para microserviços sem necessidade comprovada e autorização explícita.

Estrutura inicial esperada:

```text
src/
  TorreLogistica.Domain/
  TorreLogistica.Application/
  TorreLogistica.Infrastructure/
  TorreLogistica.Api/
  TorreLogistica.Workers/
  TorreLogistica.Simulator/

apps/
  operacao/
  motorista/
  rastreamento/

tests/
  TorreLogistica.UnitTests/
  TorreLogistica.IntegrationTests/
  TorreLogistica.ArchitectureTests/
  ...

docs/
  adr/
  arquitetura/
  seguranca/
  operacao/
```

A estrutura pode evoluir se houver motivo técnico claro, mas evitar explosão de projetos `.csproj` sem ganho real.

---

# 7. Camadas e dependências

Direção padrão:

```text
Domain
  ↑
Application
  ↑
Infrastructure
  ↑
Api / Workers / Simulator composition roots
```

O domínio não deve depender de:

- ASP.NET Core;
- EF Core;
- SignalR;
- AWS SDK;
- PostGIS/Npgsql diretamente, salvo tipos de valor deliberadamente escolhidos e documentados;
- storage;
- provedores externos;
- infraestrutura de mensageria.

Regras de arquitetura devem ser protegidas por testes automatizados.

---

# 8. Módulos de domínio

A solução pode usar módulos lógicos como:

```text
Identidade
Organizacoes
Frota
Entregas
Rotas
Rastreamento
Geoespacial
Operacao
Ocorrencias
Comprovantes
Integracoes
Notificacoes
Auditoria
Demo
```

A fronteira entre módulos deve ser explícita. Não criar uma pasta `Services` global contendo lógica de todos os domínios.

---

# 9. Entrega como núcleo operacional

`Entrega` é um dos agregados centrais do sistema.

Mudanças de estado não podem ocorrer por atribuição arbitrária:

```csharp
entrega.Status = StatusEntrega.Entregue;
```

O domínio deve expor operações com intenção explícita, por exemplo:

```text
Criar
Planejar
Atribuir
IniciarRota
RegistrarProximidade
RegistrarTentativaFrustrada
Reagendar
Concluir
Cancelar
```

Os nomes definitivos devem nascer do domínio implementado, mantendo clareza semântica.

---

# 10. Máquina de estados

Direção inicial:

```text
Criada
  ↓
Planejada
  ↓
Atribuida
  ↓
EmRota
  ↓
ProximaDoDestino
  ↓
Entregue
```

Fluxos alternativos podem incluir:

```text
EmRota
  ↓
TentativaFrustrada
  ↓
Reagendada
```

ou cancelamento a partir de estados autorizados.

Toda transição precisa:

1. validar o estado atual;
2. validar regras e invariantes;
3. registrar evento operacional relevante;
4. manter histórico auditável;
5. evitar regressão silenciosa de estado.

Uma entrega concluída nunca deve voltar para `EmRota` por causa de telemetria atrasada.

---

# 11. Timeline da entrega

Eventos relevantes da entrega são append-only.

Exemplo:

```text
09:00 Entrega criada
09:20 Atribuída a Rafael
10:14 Saiu para rota
11:02 Risco de atraso detectado
11:37 Entrou no geofence
11:40 Chegada registrada
11:43 Entrega concluída
```

Eventos históricos não devem ser sobrescritos para refletir o “estado atual”. Estado atual e histórico são conceitos diferentes.

---

# 12. Identificadores

Usar UUIDv7 como identificador interno padrão, salvo exceção justificada.

Entidades com exposição operacional podem possuir código humano adicional, por exemplo:

```text
ENT-2026-004821
ROT-2026-0092
OCO-2026-0031
```

Nunca usar código humano como chave primária.

---

# 13. Multi-tenancy

Multi-tenancy é requisito estrutural, inclusive no ambiente de demonstração.

Cada recurso de negócio relevante deve pertencer a uma organização.

O tenant deve derivar de uma autoridade autenticada:

- sessão do usuário;
- identidade do motorista;
- credencial de integração.

Nunca confiar em `OrganizacaoId` recebido no payload como fonte de autorização.

Exemplo proibido:

```json
{
  "organizacaoId": "tenant-alvo"
}
```

O backend sempre valida o tenant efetivo.

Acesso cross-tenant deve ser impossível por desenho e coberto por testes.

---

# 14. Perfis e autoridades

Perfis administrativos iniciais:

```text
Administrador
Supervisor
Operador
```

Motorista é uma autoridade separada, não apenas um papel administrativo adicional.

A sessão do motorista não deve conceder acesso ao console operacional.

O backend é a autoridade de autorização. Ocultar botão no frontend nunca conta como controle de segurança.

---

# 15. Banco de dados

Banco principal:

> PostgreSQL com extensão PostGIS.

PostgreSQL é a fonte de verdade para o domínio operacional.

Principais grupos de persistência esperados:

```text
organizacoes
usuarios
sessoes
motoristas
veiculos
hubs
entregas
eventos_da_entrega
atribuicoes
rotas
paradas
rotas_entregas
posicoes
posicoes_atuais
ocorrencias
tentativas_de_entrega
comprovantes
alertas_operacionais
integracoes
webhooks
entregas_de_webhook
outbox
inbox
auditoria
```

Os nomes físicos podem ser ajustados conforme convenções finais.

---

# 16. PostGIS

PostGIS não deve ser apenas uma dependência nominal.

Usar capacidades geoespaciais reais para operações como:

- distância;
- proximidade;
- geofence;
- busca espacial;
- pontos, linhas e áreas quando aplicável.

Exemplos de funções esperadas:

```text
ST_DWithin
ST_Distance
ST_MakePoint
```

Não reimplementar cálculos geodésicos complexos em C# quando o banco oferece solução confiável e testável.

Testes de integração geoespaciais devem rodar contra PostgreSQL + PostGIS real.

---

# 17. Ingestão de localização

Uma posição deve carregar, no mínimo, conceitos equivalentes a:

```text
motorista
latitude
longitude
accuracy
capturedAt
receivedAt
sequence
locationEventId
```

`capturedAt` e `receivedAt` não são equivalentes e não podem ser fundidos.

A API deve suportar ingestão em lote quando previsto no roadmap.

---

# 18. Histórico de posição e posição atual

Separar:

```text
posicoes
```

como histórico append-only, de:

```text
posicoes_atuais
```

como projeção/estado atual eficiente.

Não consultar a última posição de milhões de registros com `ORDER BY ... DESC LIMIT 1` em toda abertura do mapa se existe uma projeção adequada.

---

# 19. Invariante de evento fora de ordem

Se posições forem capturadas em:

```text
10:01
10:03
10:02
```

mas chegarem nessa ordem:

```text
10:01
10:03
10:02
```

as três podem existir no histórico.

Porém a posição atual continua sendo a de `10:03`.

**Regra absoluta:** evento antigo nunca regressa a posição atual.

Essa regra deve possuir testes explícitos.

---

# 20. Idempotência de localização

O cliente gera `LocationEventId` antes do envio.

Uma repetição da mesma posição deve resultar em um único efeito lógico.

Restrição conceitual:

```text
Organizacao + Motorista + LocationEventId
```

única.

A API deve tratar retry como comportamento esperado, não como exceção rara.

---

# 21. Qualidade da localização

Localizações podem ser:

- imprecisas;
- antigas;
- duplicadas;
- impossíveis;
- fora de sequência;
- recebidas em rajadas.

O domínio/serviço de rastreamento deve definir política clara para aceitação, rejeição e uso de posições.

Não esconder posições problemáticas sem telemetria adequada.

Métricas devem permitir saber quantas posições foram rejeitadas ou ignoradas para estado atual.

---

# 22. Retenção e volume de GPS

Localização bruta é dado volumoso e sensível.

A arquitetura deve nascer preparada para:

- retenção configurável;
- limpeza automática;
- particionamento temporal quando necessário;
- preservação de eventos operacionais relevantes por período maior.

Não implementar retenção infinita por conveniência.

Particionamento não precisa estar ativo na primeira fase, mas decisões que impossibilitem sua adoção futura devem ser evitadas.

---

# 23. Geofencing

Geofence deve detectar transições, não apenas “ponto dentro do raio”.

Exemplo:

```text
fora → dentro
```

pode gerar evento:

```text
MotoristaEntrouNoGeofence
```

Receber 30 posições dentro do raio não deve gerar 30 eventos de entrada.

Estado/projeção de geofence deve impedir spam lógico.

---

# 24. ETA

ETA deve ser determinístico, explicável e mensurável na v1.

Não usar IA como requisito para cálculo de ETA.

A arquitetura deve separar:

```text
IRoutingProvider
```

responsável por informações externas de rota, de um serviço interno de ETA que combina contexto operacional.

O domínio não deve depender diretamente de Google Maps, Mapbox ou outro fornecedor específico.

---

# 25. Histórico de ETA

Não armazenar apenas o ETA atual.

Quando relevante, preservar evolução da previsão, por exemplo:

```text
13:50 → 14:20
14:00 → 14:27
14:10 → 14:39
14:20 → 14:51
```

Isso permite explicar alertas e medir qualidade da previsão.

Histórico de ETA não deve ser reescrito retroativamente.

---

# 26. SLA

Entrega deve suportar janela prometida, não apenas timestamp único.

Exemplo:

```text
PromisedFrom
PromisedUntil
```

O sistema deve distinguir estados operacionais como:

```text
Normal
Atencao
Risco
Atrasada
```

As regras definitivas de limiar devem ser configuráveis e testáveis.

---

# 27. Alertas operacionais

Alertas não devem ser `if` dispersos pelo código.

Deve existir um módulo ou serviço de monitoramento operacional com regras tipadas.

Exemplos de alertas possíveis:

```text
RiscoDeAtraso
EntregaAtrasada
MotoristaOffline
ParadoTempoExcessivo
TentativasExcedidas
OcorrenciaCritica
DesvioRelevante
```

Cada alerta deve possuir evidência suficiente para explicar por que foi criado.

---

# 28. Concorrência

Conflitos reais devem ser tratados explicitamente.

Exemplo:

```text
Operador reatribui entrega
X
Motorista conclui entrega
```

Não aceitar last-write-wins silencioso.

Entidades críticas devem usar estratégia clara de concorrência otimista ou serialização quando necessário.

Conflitos legítimos devem resultar em erro de domínio/HTTP adequado, normalmente `409 Conflict`.

Testes concorrentes devem validar cenários de corrida reais.

---

# 29. PWA do motorista

A PWA deve ser mobile-first, simples e orientada a tarefa.

Evitar replicar o console operacional em tela pequena.

Deve possuir, quando habilitado pelo roadmap:

- service worker;
- IndexedDB;
- detecção online/offline;
- fila local de comandos;
- sincronização ao reconectar;
- GPS;
- câmera/upload;
- feedback claro de sincronização.

Não depender de Background Sync como única estratégia, pois suporte de navegador varia.

---

# 30. Offline-first nas ações previstas

Comandos realizados offline devem nascer com identificador local, por exemplo:

```text
ClientOperationId = UUIDv7
```

Estados locais podem seguir algo como:

```text
Pending
Uploading
Synced
Failed
Conflict
```

O formato pode evoluir, mas o comportamento precisa ser explícito.

---

# 31. Idempotência de ações offline

Cenário obrigatório:

```text
motorista conclui entrega
→ backend processa
→ resposta HTTP se perde
→ PWA tenta novamente
```

Resultado esperado:

```text
1 conclusão
1 evento lógico
1 comprovante lógico
```

Não criar efeitos duplicados.

---

# 32. Conflitos durante offline

Exemplo:

```text
PWA offline: motorista conclui
Enquanto isso: operador cancela
PWA reconecta
```

O comando atrasado não pode sobrescrever o estado atual sem validação.

O backend deve retornar conflito de domínio e a PWA deve apresentar estado recuperável ao usuário.

---

# 33. Comprovantes de entrega

Arquivos binários não devem ser armazenados diretamente em PostgreSQL.

Usar abstração de storage, por exemplo:

```text
IObjectStorage
```

Fluxo preferencial:

```text
cliente solicita upload
→ backend autoriza
→ cliente envia direto ao storage
→ backend registra metadados
```

Quando aplicável, usar URL assinada de curta duração.

---

# 34. Privacidade dos comprovantes

Comprovantes são privados por padrão.

Nunca publicar bucket inteiro.

Acesso deve ser autenticado/autorizado ou ocorrer via URL assinada temporária.

Metadados sensíveis não devem aparecer em logs.

---

# 35. Rastreamento público

Cada entrega pública deve ser acessada por token forte e não previsível.

Não armazenar token público cru se uma representação hash segura puder ser usada para lookup.

Abordagem esperada:

```text
SHA-256(token)
```

ou equivalente apropriado.

O endpoint público nunca aceita tenant como autoridade.

---

# 36. Privacidade de localização pública

A página pública não deve revelar localização exata do motorista sem necessidade.

Possíveis estratégias:

- posição aproximada;
- atualização com atraso;
- posição visível apenas próximo do destino;
- área/região em vez de coordenada exata.

A decisão final deve ser documentada e coberta por testes de exposição de dados.

---

# 37. Autenticação humana

Usuários administrativos devem usar sessão segura com estratégia equivalente a:

```text
access token curto
+
refresh token rotativo
+
reuse detection
+
revogação de família
```

A implementação exata pode variar, mas não reduzir segurança sem justificativa.

Cookies cross-site, CORS e CSRF devem ser tratados com base na topologia real de deploy, não em suposições locais.

---

# 38. Credenciais de integração

Integrações sistema-a-sistema devem usar autoridade separada da autenticação humana.

Exemplo:

```text
POST /api/integracoes/v1/entregas
```

com credencial própria e `Idempotency-Key` quando aplicável.

Credenciais devem ser armazenadas de forma segura e nunca logadas.

---

# 39. Webhooks

Webhooks devem ser:

- assinados;
- idempotentes no emissor;
- reenviáveis;
- observáveis;
- persistidos com histórico de tentativa;
- processados de forma assíncrona quando apropriado.

Assinatura preferencial: HMAC com segredo por integração.

Não considerar HTTP 200 recebido uma única vez como garantia de entrega eterna.

---

# 40. Transactional Outbox

Eventos que precisam sair do banco de forma confiável devem usar Transactional Outbox quando houver risco de dual-write.

Exemplo:

```text
Entrega concluída
+
Outbox event
```

no mesmo commit.

Evitar:

```text
salvar no banco
→ publicar fora da transação
→ processo cai
```

sem mecanismo de recuperação.

---

# 41. Inbox / consumidores idempotentes

Filas são at-least-once por padrão.

Consumidores devem assumir duplicação.

Usar Inbox, chave natural ou outro mecanismo idempotente quando o efeito não puder ocorrer duas vezes.

Nunca escrever código assumindo “SQS não repete”.

---

# 42. Mensageria

SQS é a direção inicial provável, mas decisões finais de infraestrutura ocorrerão na fase apropriada.

Separar cargas com características distintas quando houver justificativa, por exemplo:

```text
operacional
webhooks
comprovantes
notificacoes
```

Não criar fila para cada evento de domínio.

Posição GPS comum não precisa virar cascata de mensagens se persistência síncrona e projeção local forem suficientes.

---

# 43. SignalR e tempo real

O domínio não conhece SignalR.

Definir fronteira como:

```text
IOperationRealtimePublisher
```

ou equivalente.

Eventos de UI em tempo real podem incluir:

```text
DriverPositionUpdated
DeliveryStatusChanged
DeliveryRiskChanged
AlertCreated
IncidentCreated
```

O uso de SignalR é uma decisão de infraestrutura/aplicação.

---

# 44. Escalabilidade do realtime

Não adicionar Redis/backplane no início sem necessidade.

A arquitetura deve permitir evolução para múltiplas instâncias com backplane ou serviço gerenciado quando necessário.

Evitar criar dependência operacional permanente apenas por hipótese futura.

---

# 45. Simulador

`TorreLogistica.Simulator` é componente separado.

O simulador deve se comportar como cliente real.

Proibido:

```sql
UPDATE entregas ...
```

ou qualquer manipulação direta do banco para “fingir” operação.

Fluxo obrigatório:

```text
Simulator
→ APIs reais / contratos reais
→ aplicação real
```

Assim o modo demo valida os mesmos caminhos de produção.

---

# 46. Cenários determinísticos de demonstração

A demo deve ser reproduzível.

Usar cenário e seed controlados, com relógio e randomização determinística quando necessário.

Cenários esperados incluem, ao menos:

- operação normal;
- entrega entrando em risco de atraso;
- entrega atrasada;
- motorista offline;
- tentativa frustrada;
- ocorrência crítica;
- entrada em geofence;
- entrega concluída com comprovante.

A demo deve parecer uma operação real, não uma coleção de registros aleatórios.

---

# 47. Modo demonstração

O ambiente público deve possuir forma clara de entrar em modo demo, preferencialmente:

```text
Explorar demonstração
```

Não exigir criação de conta comercial.

Recursos de demo como:

```text
pausar
retomar
reiniciar cenário
```

podem existir, mas devem ser isolados do comportamento produtivo normal.

---

# 48. Relógio

Nunca espalhar `DateTime.UtcNow` arbitrariamente em regras importantes.

Usar abstração de relógio onde determinismo, testes ou simulador exigirem.

O relógio é especialmente importante em:

- SLA;
- ETA;
- timeout offline;
- motorista offline;
- timeline;
- simulador;
- testes determinísticos.

---

# 49. Tempo e timezone

Persistir instantes em UTC.

Timezone local deve ser uma preocupação de apresentação/configuração da organização.

Nunca assumir que todo cliente futuro estará em `America/Sao_Paulo`, mesmo que a demo inicial esteja no Brasil.

---

# 50. Observabilidade

OpenTelemetry é a direção preferencial.

O sistema deve ser observável por logs, métricas e traces.

Fluxos importantes precisam de correlation/trace context consistente.

Exemplo de trace útil:

```text
GPS received
→ persisted
→ current position updated
→ geofence evaluated
→ ETA recalculated
→ risk changed
→ realtime published
```

---

# 51. Métricas importantes

Métricas candidatas:

```text
tracking_positions_received_total
tracking_positions_duplicate_total
tracking_positions_out_of_order_total
tracking_positions_rejected_accuracy_total
tracking_ingestion_lag
active_drivers
offline_drivers
deliveries_in_route
deliveries_at_risk
deliveries_late
eta_calculation_duration
signalr_connected_clients
outbox_pending
webhook_delivery_failures
```

Não criar métricas sem utilidade operacional.

---

# 52. Logs

Logs devem ser estruturados.

Não registrar:

- tokens;
- refresh tokens;
- chaves de integração;
- secrets;
- conteúdo completo de comprovantes;
- localização bruta em excesso;
- dados pessoais desnecessários.

GPS não deve gerar log detalhado por evento em produção se isso causar custo/ruído sem valor.

---

# 53. Health checks

Manter distinção:

```text
/live
/ready
```

`/live`: processo está funcional.  
`/ready`: dependências críticas necessárias à operação estão disponíveis.

Não transformar `/live` em teste de todas as dependências externas.

---

# 54. Segurança

Segurança é requisito transversal, não fase final.

Cada fase relevante deve incluir Security Gate.

Validar continuamente:

- autenticação;
- autorização;
- tenant isolation;
- mass assignment;
- IDOR;
- rate limiting;
- upload;
- path traversal;
- CORS;
- CSRF;
- cookies;
- secrets;
- logs;
- tokens públicos;
- URLs assinadas;
- webhooks;
- SSRF em integrações, quando aplicável.

---

# 55. LGPD e minimização

Não transformar o projeto em consultoria jurídica, mas adotar boas práticas técnicas:

- coletar apenas localização necessária;
- limitar retenção;
- evitar exposição pública exata;
- controlar acesso a comprovantes;
- permitir políticas de retenção;
- manter auditoria administrativa;
- documentar finalidade e fluxo dos dados sensíveis.

---

# 56. Auditoria

Auditoria administrativa é append-only.

Exemplos de eventos auditáveis:

- motorista criado;
- veículo alterado;
- entrega reatribuída;
- entrega cancelada;
- configuração alterada;
- integração criada/revogada;
- usuário ou permissão alterada.

Não duplicar toda telemetria GPS na tabela de auditoria.

GPS possui histórico próprio.

---

# 57. Frontend operacional

Direção tecnológica:

```text
React 19
TypeScript strict
Vite
TanStack Query
React Router
MapLibre GL
SignalR client
Zod ou validação equivalente
```

Não adicionar Redux automaticamente.

Estado de servidor deve preferencialmente permanecer em ferramenta de server-state como TanStack Query.

---

# 58. UI do console

O console deve parecer software de operação logística, não template SaaS genérico.

Características desejadas:

- mapa como elemento central;
- informação densa, mas legível;
- listas sincronizadas com mapa;
- drawers e painéis laterais;
- status claros;
- timelines;
- prioridade operacional;
- filtros úteis;
- foco em velocidade de decisão.

Evitar glassmorphism, cards decorativos excessivos e dashboards cheios de gráficos sem ação operacional.

---

# 59. Tela símbolo do projeto

O `Mapa da Operação` deve ser tratado como uma das principais experiências do produto.

Ele deve permitir entender rapidamente:

- entregas em rota;
- entregas em risco;
- atrasos;
- motoristas ativos/offline;
- posição atual;
- rota;
- próxima parada;
- ETA;
- alertas.

A UX dessa tela merece atenção especial no roadmap.

---

# 60. Indicadores

Priorizar métricas operacionais reais:

- OTD / On Time Delivery;
- sucesso na primeira tentativa;
- atraso médio;
- entregas por motorista;
- tempo médio por parada;
- ocorrências por motivo;
- SLA por cliente;
- tempo parado;
- duração de rota.

Evitar gráficos de vaidade.

---

# 61. Provedores externos

Abstrair somente fronteiras externas que podem variar de verdade.

Interfaces candidatas:

```text
IObjectStorage
IRoutingProvider
IGeocodingProvider
IRealtimePublisher
IMessageBus
INotificationProvider
```

Não criar abstrações sobre tudo.

Evitar repositories genéricos universais, Unit of Work artificial ou wrappers sem valor sobre EF Core.

---

# 62. Infraestrutura

Não assumir AWS Lambda como compute principal apenas porque foi usado anteriormente.

SignalR/WebSocket e conexões persistentes alteram a decisão.

Na fase de infraestrutura, comparar opções com base em:

- suporte a ASP.NET Core;
- WebSocket/SignalR;
- custo;
- cold start;
- escala;
- logs e observabilidade;
- simplicidade de deploy;
- experiência operacional.

AWS pode continuar sendo usada para serviços específicos, mas a escolha deve ser técnica, não habitual.

---

# 63. Custos

Mesmo sendo portfólio, evitar arquitetura com custo desnecessário.

O sistema deve ser econômico o suficiente para ficar disponível para avaliação pública.

Medir e documentar custos relevantes de:

- compute;
- banco;
- mapas/roteamento;
- storage;
- tráfego;
- observabilidade.

Não sacrificar arquitetura correta apenas para perseguir custo zero absoluto.

---

# 64. Testes

A estratégia de qualidade deve incluir, conforme fase:

```text
Unit Tests
Integration Tests
Architecture Tests
Contract Tests
Frontend Tests
PWA/Offline Tests
Realtime Tests
Concurrency Tests
Geospatial Tests
Performance Tests
Security Tests
```

Testes devem validar comportamento e invariantes, não apenas aumentar contagem.

---

# 65. Integração real

Testes de integração devem usar PostgreSQL/PostGIS real via Testcontainers quando aplicável.

Evitar InMemory provider para provar comportamento de:

- concorrência;
- constraints;
- transações;
- SQL;
- PostGIS;
- índices;
- serialização;
- isolamento.

---

# 66. Testes geoespaciais obrigatórios

Cenários de fronteira devem ser testados com geometria real.

Exemplo conceitual:

```text
299 m → dentro
301 m → fora
```

Não mockar PostGIS para testes cuja finalidade é provar comportamento geográfico.

---

# 67. Teste de evento fora de ordem obrigatório

Cenário mínimo:

```text
sequence 101
sequence 103
sequence 102
```

Resultado:

```text
histórico contém todos os eventos válidos
posição atual continua em 103
```

Esse teste deve permanecer como regressão permanente.

---

# 68. Testes offline obrigatórios

Validar cenários como:

```text
offline
→ concluir entrega
→ registrar comprovante
→ registrar posição
→ reconectar
→ sincronizar exatamente uma vez
```

Também testar conflito de estado durante o período offline.

---

# 69. Testes de concorrência obrigatórios

Cobrir corridas relevantes, por exemplo:

- reatribuição x conclusão;
- cancelamento x conclusão;
- múltiplos updates do mesmo agregado;
- duplicate idempotency key;
- processamento concorrente de Outbox;
- múltiplos consumidores.

Não simular concorrência apenas chamando métodos sequencialmente.

---

# 70. Performance

Meta inicial de referência para teste, não promessa comercial:

```text
500 motoristas
1 posição a cada 15 segundos
≈ 33 posições/segundo
```

O benchmark final deve medir:

- latência de ingestão;
- escrita de histórico;
- atualização de posição atual;
- geofence;
- ETA;
- realtime;
- uso de CPU/memória;
- banco e índices.

Não otimizar prematuramente sem dados.

---

# 71. Dependências

Toda nova dependência precisa justificar:

1. problema que resolve;
2. por que solução nativa é insuficiente;
3. impacto de segurança;
4. impacto operacional;
5. manutenção.

Evitar bibliotecas abandonadas ou pouco confiáveis.

Manter scans de dependências e atualização automatizada quando aplicável.

---

# 72. Migrations

Schema de banco evolui exclusivamente por migrations versionadas.

Não editar banco de produção manualmente como procedimento normal.

Correções emergenciais precisam ser registradas em migration/script/documentação imediatamente após a intervenção.

Seed de demo deve ser determinístico e separado de dados produtivos reais.

---

# 73. Dados sensíveis e secrets

Nunca commitar:

- tokens;
- senhas;
- secrets;
- connection strings produtivas;
- chaves de API;
- credenciais AWS;
- chaves de integração;
- links assinados duradouros.

`.env.example` pode documentar nomes de variáveis sem valores sensíveis.

---

# 74. Dados de demonstração

Dados de demo devem ser fictícios e coerentes.

Não usar CPF, telefone, endereço ou identidade real de pessoas sem necessidade.

A demo deve ter narrativa e consistência temporal.

Não gerar milhares de linhas aleatórias sem propósito visual ou funcional.

---

# 75. Documentação

Decisões importantes devem ser documentadas.

Usar ADR quando houver escolha arquitetural relevante, por exemplo:

- provider de mapa/roteamento;
- estratégia realtime;
- estratégia de storage;
- retenção de GPS;
- concorrência;
- hosting;
- modo offline;
- particionamento;
- rastreamento público.

README não deve virar depósito de decisões internas.

---

# 76. Commits

Commits locais são permitidos durante execução de fase.

Preferir commits pequenos e semanticamente coerentes.

Exemplo:

```text
feat: adiciona ingestao idempotente de localizacao
fix: impede regressao da posicao atual por evento atrasado
test: cobre conflito entre cancelamento e conclusao
```

Evitar commit genérico como `updates`.

---

# 77. Git push e deploy

O agente pode:

- editar arquivos;
- executar comandos locais;
- instalar dependências necessárias e gratuitas;
- criar migrations;
- executar testes;
- criar commits locais;
- atualizar documentação;
- preparar infraestrutura.

O agente **não pode**, sem autorização explícita:

- fazer `git push`;
- criar release pública;
- criar tag remota;
- fazer deploy produtivo;
- excluir recursos cloud;
- executar operação irreversível em ambiente externo;
- criar recurso pago com risco real de cobrança;
- alterar DNS;
- compartilhar secrets.

---

# 78. Autonomia por fase

Quando o usuário disser:

> `siga para a próxima fase`

isso significa autorização para executar **toda a próxima fase incompleta do `ROADMAP.md`**.

Não pedir confirmação para cada arquivo, migration, teste ou decisão técnica reversível dentro da fase.

O agente deve:

1. ler `CLAUDE.md`;
2. ler `ROADMAP.md`;
3. identificar a próxima fase incompleta;
4. compreender critérios de aceite e gates;
5. executar a fase integralmente;
6. testar;
7. corrigir falhas causadas pela fase;
8. documentar;
9. atualizar status do roadmap quando apropriado;
10. criar commit(s) local(is) coerente(s);
11. parar ao concluir a fase ou atingir gate real.

---

# 79. Quando o agente deve parar

Parar e pedir decisão somente se houver:

- mudança de arquitetura estrutural não prevista;
- requisito de produto genuinamente ambíguo que muda comportamento externo;
- necessidade de segredo que o agente não possui;
- custo pago relevante;
- operação irreversível;
- push/deploy/release não autorizado;
- exclusão de dados externos;
- conflito entre `CLAUDE.md`, `ROADMAP.md` e estado real do projeto que não possa ser resolvido com segurança;
- vulnerabilidade relevante que exija mudança de escopo;
- escolha de fornecedor externo com implicação financeira ou lock-in relevante não coberta pelo roadmap.

Não parar por detalhes técnicos reversíveis.

---

# 80. Decisões autônomas permitidas

O agente deve decidir sozinho, quando seguro e reversível:

- nomes internos consistentes;
- organização de classes;
- pequenas refatorações;
- índices necessários;
- testes adicionais;
- factories/builders de teste;
- DTOs;
- validações;
- pequenas melhorias de UX;
- tratamento de erros;
- logging;
- otimizações comprovadas por profiling;
- biblioteca madura para problema local já aprovado pela arquitetura.

Sempre priorizar simplicidade e clareza.

---

# 81. Proibido mascarar falhas

Nunca:

- desabilitar teste para deixar CI verde;
- reduzir assertion para “passar”;
- engolir exception sem telemetria;
- remover Security Gate por dificuldade;
- mockar integração cujo objetivo do teste é justamente validar integração;
- alterar regra de domínio apenas para satisfazer teste incorreto;
- marcar fase concluída com pendências silenciosas.

Quando teste estiver errado, corrigir o teste e documentar por que estava errado.

---

# 82. Definition of Done de uma fase

Uma fase só pode ser marcada concluída quando, conforme aplicável:

- implementação funcional está completa;
- migrations estão consistentes;
- testes da fase estão verdes;
- regressão relevante está verde;
- arquitetura continua válida;
- Security Gate da fase passou;
- documentação necessária foi atualizada;
- não existem TODOs críticos escondidos;
- observabilidade mínima da funcionalidade existe;
- código está formatado/lintado;
- commit local foi criado quando apropriado;
- evidências ou resultados importantes foram registrados.

---

# 83. Qualidade acima de contagem de features

Não correr para “terminar roadmap” sacrificando qualidade.

É preferível concluir menos escopo com comportamento correto do que adicionar funcionalidades frágeis.

A Torre Logística deve ser mais trabalhada que Prisma RH e Central Antifraude, não apenas maior.

---

# 84. UX de produção

Antes da release, validar manualmente jornadas completas em navegador real.

Não considerar screenshot isolada como validação de UX.

Testar diferentes perfis e cenários:

```text
Operador
Supervisor
Administrador
Motorista
Rastreamento público
```

A aplicação pública deve ser compreensível sem explicação verbal do desenvolvedor.

---

# 85. Compatibilidade da demo

A demo pública deve continuar funcional mesmo quando serviços de apoio estiverem ociosos ou sofrerem cold start.

Mensagens de carregamento e recuperação precisam ser claras.

Não deixar tela quebrada durante inicialização de infraestrutura.

---

# 86. Robustez do simulador

O simulador não deve gerar estado impossível.

Cada cenário precisa respeitar as mesmas invariantes do sistema.

Se o simulador encontrar `409`, `429`, indisponibilidade ou outro erro esperado, deve tratar de forma realista e observável.

Não adicionar “atalho de demo” que bypassa regra de domínio.

---

# 87. Relacionamento entre estado síncrono e assíncrono

Definir claramente quais efeitos precisam ocorrer no commit da operação e quais podem ocorrer depois.

Exemplo:

```text
concluir entrega
```

pode exigir no mesmo commit:

- estado `Entregue`;
- evento de timeline;
- metadados do comprovante;
- Outbox.

Já efeitos como webhook, notificação e analytics podem ocorrer depois.

Evitar inconsistência por dual-write.

---

# 88. APIs

APIs devem:

- usar contratos explícitos;
- validar entrada;
- evitar overposting;
- retornar erros padronizados;
- usar status HTTP adequados;
- versionar integrações externas quando necessário;
- possuir limites de payload;
- possuir paginação em coleções grandes;
- possuir filtros tipados.

Não expor entidades EF diretamente.

---

# 89. Erros

Erros devem distinguir:

- validação;
- autenticação;
- autorização;
- recurso inexistente;
- conflito;
- rate limit;
- dependência externa indisponível;
- erro inesperado.

Não retornar stack trace em produção.

---

# 90. Rate limiting

Aplicar limites diferentes conforme superfície:

- login;
- rastreamento público;
- telemetria GPS;
- API de integração;
- uploads;
- webhooks administrativos.

Telemetria legítima não deve ser bloqueada por uma política genérica inadequada.

---

# 91. Uploads

Validar:

- tamanho;
- tipo permitido;
- metadata;
- extensão não confiável;
- nome do arquivo;
- quantidade;
- autorização;
- associação ao tenant.

Não confiar em MIME informado pelo cliente como única defesa.

---

# 92. Índices

Toda query crítica deve ter estratégia de índice consciente.

Especial atenção para:

- tenant + status;
- entregas por período;
- posição atual por motorista;
- histórico temporal;
- geospatial GIST/SP-GiST quando apropriado;
- Outbox pendente;
- Inbox;
- alertas abertos;
- tracking token hash;
- idempotency keys.

Adicionar índice apenas com justificativa e validar plano de consulta quando performance importar.

---

# 93. Demo e produção

Código de demo deve ser isolado por ambiente/configuração.

Não deixar endpoints de reset de demo disponíveis em produção comercial genérica sem proteção.

O ambiente público de portfólio pode ativar funcionalidades próprias de demo de forma controlada.

---

# 94. Release final

A release `v1.0.0` só ocorre após:

- roadmap concluído;
- produção validada;
- testes finais verdes;
- pentest concluído;
- revisão de dependências;
- demo funcional;
- seed/simulador estabilizados;
- README final;
- screenshots;
- vídeo curto de demonstração;
- documentação técnica revisada;
- secrets verificados;
- ausência de dados sensíveis;
- validação das jornadas principais.

Git tag e GitHub Release exigem autorização explícita do usuário.

---

# 95. Filosofia de engenharia do projeto

A Torre Logística deve seguir estas prioridades, nesta ordem:

```text
correção
→ segurança
→ consistência
→ observabilidade
→ simplicidade
→ performance comprovada
→ experiência do usuário
→ extensibilidade razoável
```

Não trocar correção por “arquitetura bonita”.

Não trocar simplicidade por padrões desnecessários.

Não trocar segurança por velocidade de demo.

---

# 96. Regra final

Antes de qualquer implementação significativa, perguntar:

> Isso ajuda a Torre Logística a se comportar como um produto operacional real ou estamos adicionando complexidade apenas para parecer mais sofisticado?

Se a resposta for a segunda opção, simplificar.

---

# 97. Próximo documento

O `ROADMAP.md` deve transformar este contrato em fases executáveis, com:

- objetivo de cada fase;
- entregáveis;
- critérios de aceite;
- testes obrigatórios;
- Security Gate;
- observabilidade;
- restrições;
- dependências;
- gates de autorização;
- definição clara de conclusão.

O `ROADMAP.md` não pode contrariar este arquivo sem registrar mudança deliberada de arquitetura.
