# ROADMAP.md — Torre Logística

> Roadmap executável do projeto **Torre Logística**.
>
> Este arquivo deve ser lido junto com `CLAUDE.md` antes de qualquer alteração.
> A regra operacional é simples:
>
> **uma autorização de fase = autorização para concluir a fase inteira.**
>
> Claude pode planejar, implementar, pesquisar, criar migrations, executar testes, corrigir falhas, atualizar documentação e criar commits locais dentro da fase autorizada.
>
> Claude deve parar somente nos gates definidos em `CLAUDE.md`, especialmente quando houver:
>
> - mudança de arquitetura ou stack já congelada;
> - operação irreversível;
> - necessidade de secret não disponível;
> - risco de custo pago;
> - push/deploy não autorizado;
> - requisito de negócio impossível de inferir com segurança;
> - conflito real entre fontes ou requisitos.
>
> `git push`, deploy e criação de release exigem autorização explícita.

---

# 1. OBJETIVO DO PRODUTO

A **Torre Logística** é uma plataforma B2B para acompanhamento e operação de entregas em tempo real, reunindo:

- entregas;
- rotas;
- motoristas;
- veículos;
- localização;
- ETA;
- SLA;
- alertas;
- ocorrências;
- tentativas de entrega;
- comprovantes;
- rastreamento público;
- integração;
- auditoria;
- simulação operacional.

O produto deve parecer software real de operação logística.

Não deve parecer:

- CRUD acadêmico;
- dashboard genérico;
- clone de Uber;
- ERP;
- TMS completo;
- WMS;
- sistema de cobrança;
- SaaS comercial fictício.

A aplicação ficará publicada para demonstração e portfólio, mas a arquitetura deve permitir evolução futura para uso comercial sem reescrever o núcleo.

---

# 2. PRINCÍPIOS DO ROADMAP

Cada fase deve entregar uma fatia vertical utilizável.

Nenhuma fase é considerada concluída apenas porque:

- compila;
- tem controller;
- possui migration;
- a tela abre.

Uma fase só é concluída quando seus critérios funcionais, técnicos, de segurança e testes estiverem satisfeitos.

## Definition of Done global

Salvo quando a fase disser o contrário, para marcar uma fase como concluída:

1. implementação completa;
2. testes relevantes verdes;
3. migrations aplicadas em ambiente de teste;
4. nenhum TODO crítico silencioso;
5. nenhum secret hardcoded;
6. erros possuem contrato consistente;
7. autorização e tenant isolation revisados quando aplicável;
8. logs não expõem dados sensíveis;
9. documentação da fase atualizada;
10. Security Gate executado;
11. build backend verde;
12. build frontend verde quando afetado;
13. commit local criado;
14. `ROADMAP.md` atualizado com status e evidências.

## Status permitidos

- ⬜ não iniciada
- 🟨 em andamento
- 🟥 bloqueada
- ✅ concluída

---

# 3. REGRAS DE DEPENDÊNCIA

Fases devem ser executadas em ordem, salvo dependência explicitamente independente.

Não antecipar:

- infraestrutura de produção antes do produto funcionar localmente;
- dashboard antes do modelo operacional existir;
- simulador antes das APIs reais existirem;
- otimização antes de medição;
- abstrações de escala sem problema comprovado;
- serviços pagos sem aprovação.

---

# 4. MAPA DE FASES

| Fase | Nome | Status |
|---:|---|:---:|
| 0 | Fundação Técnica | ✅ |
| 1 | Identidade e Multi-tenancy | ⬜ |
| 2 | Frota e Estrutura Operacional | ⬜ |
| 3 | Núcleo de Entregas | ⬜ |
| 4 | Rotas e Paradas | ⬜ |
| 5 | Máquina de Estados e Concorrência | ⬜ |
| 6 | Ingestão de Localização | ⬜ |
| 7 | PostGIS e Geofencing | ⬜ |
| 8 | Tempo Real com SignalR | ⬜ |
| 9 | ETA e SLA | ⬜ |
| 10 | Motor de Alertas Operacionais | ⬜ |
| 11 | PWA do Motorista | ⬜ |
| 12 | Offline, Sincronização e Idempotência | ⬜ |
| 13 | Ocorrências e Tentativas de Entrega | ⬜ |
| 14 | Proof of Delivery | ⬜ |
| 15 | Rastreamento Público | ⬜ |
| 16 | API de Integração e Importação | ⬜ |
| 17 | Webhooks e Backbone Assíncrono | ⬜ |
| 18 | Console Operacional e Mapa | ⬜ |
| 19 | Indicadores e Analytics | ⬜ |
| 20 | Segurança e Privacidade | ⬜ |
| 21 | Observabilidade | ⬜ |
| 22 | Performance e Resiliência | ⬜ |
| 23 | Simulador e Seed Narrativo | ⬜ |
| 24 | UX Final e Modo Demonstração | ⬜ |
| 25 | Infraestrutura e Deploy | ⬜ |
| 26 | Validação em Produção e Pentest | ⬜ |
| 27 | Release v1.0.0 | ⬜ |

---

# FASE 0 — FUNDAÇÃO TÉCNICA

## Objetivo

Criar a base técnica do projeto sem iniciar funcionalidades de negócio antes das fronteiras estarem claras.

## Entregáveis

### Backend

Criar:

- `TorreLogistica.Domain`
- `TorreLogistica.Application`
- `TorreLogistica.Infrastructure`
- `TorreLogistica.Api`
- `TorreLogistica.Workers`
- `TorreLogistica.Simulator`

Configurar:

- .NET 10;
- nullable habilitado;
- warnings relevantes tratados;
- analyzers;
- formatação;
- DI;
- configuração tipada;
- Problem Details;
- correlation ID;
- relógio injetável;
- UUIDv7;
- health endpoints;
- logging estruturado.

### Frontend

Workspace contendo:

- `apps/operacao`
- `apps/motorista`
- `apps/rastreamento`

Stack:

- React 19;
- TypeScript strict;
- Vite;
- React Router;
- TanStack Query;
- Zod quando fizer sentido.

### Banco

Subir PostgreSQL + PostGIS localmente.

Criar primeira migration técnica.

### Testes

Criar projetos:

- UnitTests;
- IntegrationTests;
- ArchitectureTests;
- frontend tests.

Configurar Testcontainers com imagem PostgreSQL/PostGIS real.

### CI

Pipeline mínimo:

- restore;
- backend build;
- backend tests;
- frontend install;
- frontend build;
- frontend tests;
- architecture tests;
- dependency/security checks iniciais.

## ADRs mínimos

Criar decisões:

1. monólito modular;
2. PostgreSQL + PostGIS;
3. React separado em três experiências;
4. UUIDv7;
5. SignalR como direção de realtime;
6. simulador externo ao núcleo;
7. storage de comprovantes fora do banco.

## Critérios de aceite

- solução compila do zero;
- frontends compilam;
- Testcontainers inicializa PostGIS;
- `/health/live` funciona;
- `/health/ready` verifica banco;
- teste de arquitetura impede dependência Domain → Infrastructure;
- CI verde;
- nenhum secret versionado.

## Security Gate 0

Verificar:

- secret scanning;
- dependências;
- headers mínimos;
- ProblemDetails sem stack trace em produção;
- configuração de CORS fechada por ambiente;
- ausência de credenciais em exemplos.

---

## Fase 0 concluída — 2026-09-09

### Critérios de aceite, um por um

| Critério | Resultado | Evidência |
|---|:---:|---|
| Solução compila do zero | ✅ | `dotnet build TorreLogistica.slnx -c Release -warnaserror` → 0 avisos, 0 erros |
| Frontends compilam | ✅ | `npm run build` nas 3 aplicações → 221 / 221 / 173 módulos |
| Testcontainers inicializa PostGIS | ✅ | `postgis/postgis:17-3.5`; `postgis_version()` responde depois da migration |
| `/health/live` funciona | ✅ | `200` com `{"status":"Healthy","verificacoes":[]}` — sem tocar no banco |
| `/health/ready` verifica banco | ✅ | `200` com verificação `banco-de-dados` `Healthy` em 32,3 ms |
| Teste impede Domain → Infrastructure | ✅ | `DependenciasEntreCamadasTestes` reprova referência de projeto e pacote de infraestrutura no `Domain` |
| CI verde | ⚠️ | Pipeline escrito e YAML validado por parser; **não executado** — exige `git push`, não autorizado |
| Nenhum secret versionado | ✅ | gitleaks sobre a árvore: 0 achados; `.env` ignorado e fora do índice |

### Security Gate 0

| Item | Resultado | Evidência |
|---|:---:|---|
| Secret scanning | ✅ | gitleaks v8.30.1: 0 achados em 310 KB. **Scanner comprovado**: com 3 credenciais falsas realistas plantadas, acusou os 3 |
| Dependências | ✅ | `dotnet list package --vulnerable --include-transitive` → nenhum pacote vulnerável nos 9 projetos; `npm audit` → 0 vulnerabilidades; `--deprecated` → nada |
| Headers mínimos | ✅ | `nosniff`, `X-Frame-Options: DENY`, CSP `default-src 'none'`, `Referrer-Policy: no-referrer`, `Permissions-Policy`, CORP e `X-Permitted-Cross-Domain-Policies` — verificados por teste e na API em execução |
| ProblemDetails sem stack trace | ✅ | Teste procura no corpo a mensagem da exceção, `InvalidOperationException`, `at TorreLogistica`, `stackTrace` e `.cs:line` — nenhum presente |
| CORS fechado por ambiente | ✅ | Origem autorizada recebe liberação; origem desconhecida não recebe, nem em preflight |
| Ausência de credenciais em exemplos | ✅ | `.env.example` só com nomes e marcadores; `docker-compose.yml` por interpolação; `appsettings.json` com cadeia vazia que faz a aplicação falhar na subida |

### Testes executados

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 43 | ✅ |
| `TorreLogistica.ArchitectureTests` | 16 | ✅ |
| `TorreLogistica.IntegrationTests` (PostGIS real) | 32 | ✅ |
| Frontend — `operacao` | 15 | ✅ |
| Frontend — `motorista` | 12 | ✅ |
| Frontend — `rastreamento` | 13 | ✅ |
| **Total** | **131** | **✅** |

Além disso, `npm run lint` e `npm run typecheck` limpos nas três aplicações.

### Defeitos encontrados e corrigidos durante a fase

Os quatro primeiros foram encontrados **por teste**, não por inspeção.

1. **Relógio devolvia fuso diferente de UTC.** `RelogioDoSistema` repassava o valor do
   `TimeProvider` sem normalizar; com provedor em `-03:00`, o sistema gravaria instantes
   fora de UTC. Corrigido com `ToUniversalTime()`.
2. **CORS lido cedo demais.** A política era montada a partir de uma leitura direta da
   configuração no momento de construir o host, antes de as demais fontes do ambiente
   entrarem em vigor — a origem autorizada não era liberada. Passou a ser montada a
   partir das opções resolvidas pela injeção de dependência.
3. **Identificador de correlação desaparecia na resposta de erro.** O middleware de
   tratamento de erro limpa a resposta antes de escrever o ProblemDetails, apagando o
   cabeçalho gravado de imediato — justamente na resposta que mais precisa dele. Passou
   a ser gravado em `Response.OnStarting`.
4. **Validação de URL aceitava `localhost:5080`.** `new URL('localhost:5080')` é válido:
   o analisador lê `localhost:` como esquema. A aplicação aceitaria a configuração e
   tentaria falar com um endereço inexistente. Passou a exigir protocolo `http`/`https`.
5. **Workers e Simulator não carregavam `appsettings.json`.**
   `Host.CreateApplicationBuilder` usa o diretório de trabalho como content root, e o
   sintoma era **silêncio absoluto**, sem exceção, porque o Serilog ficava sem sink.
   Encontrado ao conferir a saída esperada do simulador. Corrigido com
   `ContentRootPath = AppContext.BaseDirectory` e protegido por `ComposicaoDeHostTestes`
   — ausência de log não é observável por teste de comportamento.

### Armadilhas de ambiente documentadas

Em [`docs/operacao/ambiente-local.md`](./docs/operacao/ambiente-local.md):

1. **PostgreSQL nativo na porta 5432.** No Windows, a instalação nativa e o
   redirecionamento do Docker escutam ao mesmo tempo e a conexão vai para a errada sem
   erro visível — o sintoma é `28P01` com a senha correta. O contêiner do projeto passou
   a usar a porta **55432**.
2. **`DOCKER_HOST` com quatro barras.** A CLI do Docker usa `npipe:////…`; a biblioteca
   usada pelo Testcontainers só aceita duas barras. `scripts/testar.ps1` normaliza.
3. **Duas instalações do .NET.** O `dotnet` do PATH não tem o runtime 10.
   `scripts/testar.ps1` escolhe a instalação com SDK 10 e ajusta `DOTNET_ROOT`.
4. **`dotnet test` não funciona** no SDK 10.0.400 com xunit.v3 4.0.0: encerra com "Zero
   testes executados" (código 5) enquanto o executável de teste roda todas as provas.
   `scripts/testar.ps1` roda os executáveis; o código de saída continua reprovando
   a execução em caso de falha.

### Decisões registradas

ADR [0001](./docs/adr/0001-monolito-modular.md) a
[0007](./docs/adr/0007-storage-de-comprovantes-fora-do-banco.md), cada um com a seção
"Como isto é verificado".

Decisões de stack tomadas no caminho:

- **TypeScript 5.9.3, não 7.0.2.** A 7 é a versão `latest`, mas `typescript-eslint@8.70.0`
  exige `<6.1.0` e não existe 6.x estável. Revisar quando o lint acompanhar.
- **`.slnx` em vez de `.sln`** — formato padrão do SDK 10.
- **`EFCore.NamingConventions`** para `snake_case`, mantida pelo autor do provedor
  Npgsql; sem ela, cada mapeamento precisaria ser escrito à mão.

### Pendências conhecidas

| Item | Situação |
|---|---|
| CI nunca executada | Exige `git push`, não autorizado. O YAML foi validado por parser; a primeira execução real pode exigir ajuste |
| Node 22.17.0 na máquina | Vite 8 e `undici` pedem ≥ 22.22.0. Build, lint, testes e tipos passam, mas o aviso `EBADENGINE` é real |
| Aviso `NoEntityTypeConfigurationsWarning` na subida | `ApplyConfigurationsFromAssembly` não encontra configuração porque ainda não há entidade. A chamada foi mantida de propósito: removê-la faria a primeira configuração da Fase 1 ser silenciosamente ignorada. O aviso desaparece na Fase 1 |
| `package-lock.json` fora da varredura de segredos | Exclusão da configuração padrão do gitleaks. Sem impacto hoje: não há registry privado. Documentado em `docs/seguranca/gestao-de-segredos.md` |
| Hook local bloqueou o primeiro commit, com razão | Dois achados, ambos corrigidos na origem e sem lista de exceção: a documentação de segurança reproduzia o formato de uma chave de exemplo, e o workflow usava `$PWD`, que casa com o padrão de senha. O workflow passou a usar `${{ github.workspace }}`, que é mais explícito |

---

# FASE 1 — IDENTIDADE E MULTI-TENANCY

## Objetivo

Implementar identidade humana e isolamento por organização antes de qualquer dado operacional relevante.

## Entregáveis

Entidades:

- Organização;
- Usuário;
- Sessão;
- Papel;
- Convite, se necessário para demo/administração.

Perfis:

- Administrador;
- Supervisor;
- Operador;
- Motorista.

Implementar:

- login;
- logout;
- access token curto;
- refresh token rotativo;
- reuse detection;
- revogação de família;
- autorização backend;
- resolução de tenant via identidade;
- política de recurso inexistente cross-tenant → 404 quando aplicável.

Motorista deve possuir autoridade separada das APIs administrativas.

## Invariantes

- `OrganizacaoId` de payload nunca é autoridade;
- usuário não pode trocar de tenant por parâmetro;
- motorista não herda permissões de Operador;
- refresh token reutilizado revoga a família;
- recurso de outro tenant não pode vazar existência.

## Testes

- login válido/inválido;
- refresh;
- rotação;
- reuse;
- logout;
- RBAC;
- isolamento entre tenants;
- motorista tentando rota administrativa;
- operador tentando administração;
- acesso cross-tenant.

## Critérios de aceite

Uma suíte de integração deve provar explicitamente:

```text
tenant A não consegue ler, alterar ou inferir recurso do tenant B
```

## Security Gate 1

- cookies/tokens;
- CSRF se cookie cross-site for adotado;
- brute-force/rate limiting;
- enumeração de usuários;
- sessão;
- headers;
- logs de autenticação sem token.

---

# FASE 2 — FROTA E ESTRUTURA OPERACIONAL

## Objetivo

Construir os conceitos estruturais usados pela operação.

## Entidades

- Motorista;
- Veículo;
- Hub;
- Cliente;
- Destinatário.

## Funcionalidades

Motoristas:

- cadastrar;
- ativar/inativar;
- associar identidade de motorista;
- dados operacionais mínimos.

Veículos:

- placa;
- identificação;
- tipo;
- capacidade simples quando útil;
- ativo/inativo.

Hubs:

- nome;
- endereço;
- coordenada geográfica.

Clientes:

- contratante/origem operacional;
- sem transformar em CRM.

Destinatários:

- dados necessários para entrega;
- endereço;
- contato mínimo.

## Regras

- motorista inativo não recebe nova atribuição;
- veículo inativo não inicia nova rota;
- dados sensíveis minimizados;
- exclusão lógica onde histórico impedir deleção física.

## Testes

- tenant isolation;
- regras de ativo/inativo;
- unicidades por organização;
- endereços inválidos;
- autorização.

## Critérios de aceite

Supervisor consegue preparar uma organização operacional sem criar entrega ainda.

---

# FASE 3 — NÚCLEO DE ENTREGAS

## Objetivo

Implementar a Entrega como aggregate central.

## Modelo

Entrega deve possuir, no mínimo:

- UUIDv7;
- código humano;
- organização;
- cliente;
- destinatário;
- endereço;
- coordenada de destino quando disponível;
- janela prometida;
- status;
- timestamps operacionais;
- versão de concorrência.

Criar timeline append-only:

- EventoDaEntrega.

## Ações iniciais

- criar entrega;
- editar dados permitidos antes do início;
- cancelar dentro das regras;
- consultar;
- listar;
- filtrar.

## Regras

Histórico não deve ser reescrito silenciosamente.

Campos que deixam de ser mutáveis após início da operação devem ser explicitamente protegidos.

## Testes

- criação;
- código humano único por tenant;
- atualização permitida;
- atualização proibida;
- cancelamento;
- timeline;
- isolamento.

## Critérios de aceite

A API deve permitir criar e consultar uma entrega com histórico consistente sem depender ainda de rota ou GPS.

---

# FASE 4 — ROTAS E PARADAS

## Objetivo

Criar agrupamento operacional de entregas.

## Entidades

- Rota;
- Parada;
- associação RotaEntrega.

## Funcionalidades

- criar rota;
- adicionar/remover entregas ainda elegíveis;
- ordenar paradas;
- atribuir motorista;
- atribuir veículo;
- planejar saída;
- consultar sequência.

## Regras

- entrega não pode estar simultaneamente em duas rotas ativas;
- rota concluída não aceita alteração estrutural;
- motorista/veículo inativos não podem ser atribuídos;
- ordem de paradas é versionada ou registrada quando alterada.

## Testes

- duplicidade;
- concorrência de atribuição;
- remoção;
- reorder;
- tenant isolation.

## Critérios de aceite

Supervisor consegue montar rota do dia e atribuí-la.

---

# FASE 5 — MÁQUINA DE ESTADOS E CONCORRÊNCIA

## Objetivo

Formalizar transições operacionais e impedir last-write-wins silencioso.

## Estados mínimos

- Criada
- Planejada
- Atribuida
- EmRota
- ProximaDoDestino
- TentativaFrustrada
- Reagendada
- Entregue
- Cancelada

A implementação pode refinar nomes sem mudar semântica.

## Comandos de domínio

Exemplos:

- Planejar;
- Atribuir;
- IniciarRota;
- RegistrarChegada;
- RegistrarTentativaFrustrada;
- Reagendar;
- Concluir;
- Cancelar.

## Concorrência

Implementar controle otimista.

Cenário obrigatório:

```text
Operador tenta reatribuir enquanto motorista conclui.
```

Apenas uma sequência válida pode prevalecer.

Conflito deve retornar 409 ou contrato equivalente explícito.

## Testes

- todas transições permitidas;
- todas transições proibidas importantes;
- concorrência;
- retry frontend;
- timeline coerente.

## Critérios de aceite

Não deve existir endpoint genérico `PATCH status`.

Toda mudança de status deve passar por caso de uso explícito.

---

# FASE 6 — INGESTÃO DE LOCALIZAÇÃO

## Objetivo

Criar pipeline correto para telemetria de motorista.

## Contrato

Cada posição contém:

- `LocationEventId`;
- motorista;
- latitude;
- longitude;
- precisão;
- `CapturedAt`;
- `Sequence`;
- opcionalmente velocidade/direção se usado.

Servidor grava também:

- `ReceivedAt`.

## Persistência

Separar:

- `posicoes` — histórico append-only;
- `posicoes_atuais` — estado atual otimizado.

## Invariantes

- duplicata não duplica histórico;
- evento antigo entra no histórico, mas não regressa posição atual;
- coordenadas inválidas são rejeitadas;
- precisão fora de limite não atualiza estado confiável quando política disser;
- motorista só envia para seu próprio contexto.

## Batch

Suportar envio em lote para recuperação offline.

## Testes obrigatórios

```text
101
103
102
```

Resultado:

```text
histórico = 101,102,103
posição atual = 103
```

Também testar:

- duplicata;
- batch parcial;
- timestamps futuros;
- posição antiga;
- tenant/motorista indevido.

## Critérios de aceite

O sistema deve receber telemetria realista sem ainda depender de mapa.

---

# FASE 7 — POSTGIS E GEOFENCING

## Objetivo

Usar PostGIS como parte real do domínio operacional.

## Implementar

- pontos geográficos;
- distância;
- raio;
- geofence de destino;
- detecção `fora → dentro`;
- registro de entrada;
- proteção contra evento repetido enquanto permanece dentro.

Usar operações PostGIS, por exemplo:

- `ST_DWithin`;
- `ST_Distance`;
- tipos geography/geometry adequados.

## Comportamento

Entrada no geofence pode disparar:

```text
EmRota → ProximaDoDestino
```

quando a regra permitir.

GPS antigo não pode produzir transição retroativa incorreta.

## Testes

Com PostgreSQL/PostGIS real:

- dentro;
- fora;
- borda;
- duplicado;
- out-of-order;
- geofence de outro tenant;
- reentrada quando aplicável.

## Critérios de aceite

Geofencing deve ser comprovado por testes reais, não mockado.

---

# FASE 8 — TEMPO REAL COM SIGNALR

## Objetivo

Atualizar console operacional sem refresh.

## Eventos realtime mínimos

- DriverPositionUpdated;
- DeliveryStatusChanged;
- DeliveryRiskChanged;
- AlertCreated;
- IncidentCreated.

## Arquitetura

Domínio não conhece SignalR.

Criar abstração de publicação em Application/Infrastructure.

## Segurança

Grupos/canais precisam respeitar tenant.

Motorista não assina canal administrativo.

Rastreamento público não usa canal interno.

## Testes

- conexão autenticada;
- tenant isolation;
- reconexão;
- evento correto;
- ausência de vazamento entre organizações.

## Critérios de aceite

Uma posição válida deve aparecer em cliente operacional conectado sem F5.

---

# FASE 9 — ETA E SLA

## Objetivo

Criar previsão explicável e controle de janela prometida.

## SLA

Entrega possui:

- `PromisedFrom`;
- `PromisedUntil`.

## Routing Provider

Criar fronteira:

- `IRoutingProvider`.

Pode começar com implementação controlada/simulada em desenvolvimento se provider real exigir custo, mantendo contrato pronto.

## ETA

Combinar de forma determinística:

- duração restante;
- paradas anteriores;
- tempo médio configurado por parada;
- estado operacional.

## Histórico

Persistir mudanças relevantes de ETA.

Não sobrescrever histórico.

## Classificação

Exemplo:

- Normal;
- Atenção;
- Risco;
- Atrasada.

Os thresholds devem ser configuráveis e documentados.

## Testes

- ETA;
- janela;
- mudança de risco;
- histórico;
- ausência de provider;
- timeout/falha do provider.

## Critérios de aceite

A aplicação deve explicar por que uma entrega passou de Normal para Risco.

---

# FASE 10 — MOTOR DE ALERTAS OPERACIONAIS

## Objetivo

Centralizar detecção de problemas logísticos.

## Tipos iniciais

- RiscoDeAtraso;
- EntregaAtrasada;
- MotoristaOffline;
- ParadoTempoExcessivo;
- TentativasExcedidas;
- OcorrenciaCritica.

`DesvioRelevante` só entra se houver regra confiável e demonstrável.

## Alerta

Guardar:

- tipo;
- severidade;
- entrega;
- motorista;
- evidência;
- aberto em;
- resolvido em;
- estado.

## Regras

Não gerar centenas de alertas idênticos a cada tick.

Deve existir deduplicação/ciclo de vida.

## Testes

- cria;
- não duplica;
- resolve;
- reabre quando regra definir;
- tenant isolation.

## Critérios de aceite

Operador consegue identificar uma entrega problemática sem analisar manualmente todos os GPS.

---

# FASE 11 — PWA DO MOTORISTA

## Objetivo

Criar experiência operacional móvel separada do console.

## Telas

- login;
- rota do dia;
- lista de paradas;
- próxima entrega;
- detalhe;
- iniciar rota;
- chegada;
- tentativa frustrada;
- concluir;
- ocorrências;
- estado de conexão.

## UX

Ações críticas devem ser simples e grandes.

Nada de painel administrativo comprimido em mobile.

## GPS

Integrar coleta via APIs web possíveis para o contexto da PWA.

Documentar limitações reais de background location em browser/PWA.

Não fingir capacidade que navegador não garante.

## Testes

- navegação;
- autorização;
- estados;
- permission denied de geolocation;
- erros;
- responsividade.

## Critérios de aceite

Motorista consegue completar fluxo básico usando somente a PWA.

---

# FASE 12 — OFFLINE, SINCRONIZAÇÃO E IDEMPOTÊNCIA

## Objetivo

Tornar ações críticas resilientes a conexão instável.

## IndexedDB

Criar fila local de operações.

Cada operação possui:

- `ClientOperationId` UUIDv7;
- tipo;
- payload;
- createdAt;
- status;
- tentativas.

Estados sugeridos:

- Pending;
- Uploading;
- Synced;
- Conflict;
- Failed.

## Backend

Processar `ClientOperationId` exatamente uma vez.

## Casos obrigatórios

### Resposta perdida

```text
backend conclui
↓
resposta cai
↓
PWA repete
↓
mesmo resultado, sem efeito duplicado
```

### Conflito real

```text
motorista offline tenta concluir
operador cancela
motorista reconecta
```

Não sobrescrever cancelamento.

## Testes

- offline;
- reconnect;
- duplicate retry;
- batch;
- conflict;
- crash/restart de browser;
- ação já sincronizada.

## Critérios de aceite

Nenhuma ação crítica pode depender de "tomara que o POST não repita".

---

# FASE 13 — OCORRÊNCIAS E TENTATIVAS DE ENTREGA

## Objetivo

Modelar falhas reais de última milha.

## Motivos tipados

- DestinatarioAusente;
- EnderecoNaoEncontrado;
- Recusado;
- LocalFechado;
- ProblemaComVeiculo;
- ProblemaComMercadoria;
- Outro.

## Ocorrências

Possuir:

- tipo;
- severidade;
- observação;
- horário;
- localização quando apropriado;
- autor.

## Regras

Tentativa frustrada deve produzir timeline.

Não permitir texto livre como única estrutura quando motivo tipado existir.

## Testes

- cada motivo;
- reagendamento;
- limite de tentativas;
- ocorrência crítica;
- autorização.

## Critérios de aceite

Falha de entrega deixa rastreabilidade operacional clara.

---

# FASE 14 — PROOF OF DELIVERY

## Objetivo

Implementar conclusão com evidência.

## Dados

Possíveis campos:

- nome de quem recebeu;
- timestamp;
- posição;
- foto;
- assinatura;
- observação.

## Storage

Usar abstração `IObjectStorage`.

Arquivos não entram no PostgreSQL.

Em produção, direção provável: S3 ou compatível.

## Upload

Fluxo preferido:

1. solicitar autorização de upload;
2. gerar signed URL;
3. upload direto;
4. registrar metadados;
5. associar à conclusão.

## Segurança

Bucket privado.

Leitura somente por URL assinada curta e autorizada.

## Testes

- upload;
- tipo/tamanho;
- expiração;
- tenant isolation;
- arquivo inexistente;
- retry idempotente;
- conclusão sem evidência obrigatória quando política exigir.

## Critérios de aceite

Entrega concluída pode ser provada sem tornar arquivos públicos.

---

# FASE 15 — RASTREAMENTO PÚBLICO

## Objetivo

Permitir ao destinatário acompanhar entrega sem login administrativo.

## Token

Gerar token forte.

Persistir apenas hash do token.

## Página

Mostrar:

- código/pedido;
- status;
- janela;
- ETA;
- timeline simplificada;
- comprovante após conclusão quando permitido.

## Privacidade

Não expor:

- telefone do motorista;
- dados internos;
- coordenada exata continuamente;
- informações de outras entregas.

Posição pública deve ser aproximada, atrasada ou ocultada conforme política documentada.

## Segurança

- rate limiting;
- token não enumerável;
- resposta neutra para token inválido;
- proteção de comprovante.

## Critérios de aceite

Destinatário consegue entender onde sua entrega está sem acesso ao sistema interno.

---

# FASE 16 — API DE INTEGRAÇÃO E IMPORTAÇÃO

## Objetivo

Permitir que sistemas externos criem entregas de forma séria.

## Autenticação

Credencial de integração separada de usuário humano.

## API

Versão explícita:

```text
/api/integracoes/v1/...
```

## Idempotência

Suportar `Idempotency-Key`.

Restrições equivalentes a:

```text
Tenant + Integration + IdempotencyKey
Tenant + Integration + ExternalDeliveryId
```

## CSV

Importação com:

- preview;
- validação;
- relatório de erros;
- nenhuma gravação parcial silenciosa;
- hash do arquivo quando fizer sentido.

## Testes

- replay;
- payload conflitante;
- importação;
- tenant;
- credencial revogada;
- rate limiting.

## Critérios de aceite

Um ERP fictício consegue integrar sem depender da UI.

---

# FASE 17 — WEBHOOKS E BACKBONE ASSÍNCRONO

## Objetivo

Criar entrega confiável de eventos externos e efeitos assíncronos.

## Transactional Outbox

Eventos relevantes devem ser gravados no mesmo commit do estado.

## Inbox / deduplicação

Consumidores devem tolerar at-least-once.

## SQS

Filas podem ser separadas conforme necessidade comprovada, por exemplo:

- operacional;
- webhooks;
- comprovantes/processamento.

Cada fila relevante com DLQ.

## Webhooks

Eventos mínimos:

- delivery.started;
- delivery.at_risk;
- delivery.failed_attempt;
- delivery.completed.

Assinar com HMAC.

Implementar:

- retry;
- backoff;
- status;
- histórico;
- replay manual controlado.

## Testes

- duplicate delivery;
- worker crash;
- partial failure;
- HMAC;
- endpoint 500;
- timeout;
- DLQ.

## Critérios de aceite

Salvar estado e perder webhook não pode ser um failure mode silencioso.

---

# FASE 18 — CONSOLE OPERACIONAL E MAPA

## Objetivo

Criar a principal experiência visual do produto.

## Tela símbolo

**Mapa da Operação**

Mostrar:

- motoristas ativos;
- última posição;
- rotas;
- destinos;
- entregas em risco;
- atrasadas;
- offline;
- painel lateral;
- filtros.

## Outras telas

- Painel Operacional;
- Entregas;
- Detalhe da Entrega;
- Motoristas;
- Detalhe do Motorista;
- Rotas;
- Alertas;
- Ocorrências.

## Mapa

Direção: MapLibre GL.

Provider de tiles deve ser configurável.

## UX

Data-first.

Evitar:

- glassmorphism;
- gradientes gratuitos;
- cards decorativos;
- excesso de espaço vazio.

## Critérios de aceite

Usuário deve entender o estado da operação em menos de alguns segundos.

---

# FASE 19 — INDICADORES E ANALYTICS

## Objetivo

Fornecer indicadores operacionais úteis.

## Métricas de produto

- OTD;
- first-attempt success;
- atraso médio;
- entregas por motorista;
- tempo médio por parada;
- ocorrências por motivo;
- SLA por cliente;
- tempo em rota;
- entregas por rota.

## Regras

Não criar gráfico sem pergunta operacional clara.

Sempre mostrar definição do indicador.

## Testes

- período;
- timezone;
- entrega cancelada;
- dados incompletos;
- tenant isolation.

## Critérios de aceite

Supervisor consegue responder onde a operação está falhando usando os indicadores.

---

# FASE 20 — SEGURANÇA E PRIVACIDADE

## Objetivo

Executar hardening amplo focado especialmente em localização e comprovantes.

## Escopo

- RBAC completo;
- tenant isolation;
- CSRF;
- CORS;
- CSP;
- HSTS;
- nosniff;
- framing;
- referrer policy;
- rate limiting;
- upload security;
- signed URLs;
- tracking tokens;
- secret hygiene;
- log redaction;
- session hardening;
- webhook signature;
- integration credentials;
- brute force.

## Privacidade de localização

Definir e documentar:

- quando GPS pode ser coletado;
- retenção;
- finalidade;
- acesso;
- minimização;
- comportamento fora da jornada;
- localização pública aproximada.

## Retenção

Criar política técnica para GPS bruto.

Não precisa implementar multi-tier archive complexo, mas deve existir mecanismo configurável de limpeza.

## Critérios de aceite

Produzir documento `docs/security-model.md`.

---

# FASE 21 — OBSERVABILIDADE

## Objetivo

Tornar o sistema diagnosticável ponta a ponta.

## OpenTelemetry

Instrumentar:

- HTTP;
- DB;
- workers;
- SQS;
- SignalR quando viável;
- provider de rota;
- webhooks.

## Correlation ID

Propagar:

```text
HTTP
→ domínio
→ outbox
→ SQS
→ worker
→ webhook/realtime
```

## Métricas

Mínimas:

- tracking_positions_received_total
- tracking_positions_duplicate_total
- tracking_positions_out_of_order_total
- tracking_positions_rejected_total
- drivers_online
- drivers_offline
- deliveries_in_route
- deliveries_at_risk
- deliveries_late
- eta_calculation_duration
- signalr_connections
- outbox_pending
- webhook_failures

## Logs

Estruturados.

Nunca registrar token, signed URL completa ou dados sensíveis sem necessidade.

## Critérios de aceite

Uma entrega problemática deve ser rastreável pelos logs/traces sem abrir o banco manualmente.

---

# FASE 22 — PERFORMANCE E RESILIÊNCIA

## Objetivo

Medir antes de otimizar e eliminar gargalos óbvios.

## Cenário de carga inicial

Meta de teste:

```text
500 motoristas
1 posição / 15 segundos
≈ 33 posições por segundo
```

Não é promessa comercial.

É carga de engenharia.

## Testar

- ingestão GPS;
- atualização posição atual;
- geofence;
- SignalR;
- listagem de mapa;
- ETA;
- outbox;
- banco.

## Resiliência

Testar:

- DB momentaneamente indisponível;
- SQS duplicado;
- worker restart;
- provider de rota timeout;
- SignalR reconnect;
- storage falha;
- webhook indisponível.

## Banco

Avaliar índices e, apenas se justificado por medição:

- particionamento temporal de `posicoes`.

Não implementar particionamento por estética arquitetural.

## Critérios de aceite

Criar `docs/performance.md` com números reais e gargalos conhecidos.

---

# FASE 23 — SIMULADOR E SEED NARRATIVO

## Objetivo

Criar demonstração viva e reproduzível sem alterar banco diretamente.

## Simulator

Projeto separado.

Deve consumir APIs reais.

Nunca:

```text
UPDATE direto em tabelas produtivas
```

## Cenários obrigatórios

### História A — Operação normal

Motorista entrega dentro do prazo.

### História B — Risco de atraso

ETA piora progressivamente e gera alerta.

### História C — Motorista offline

Última localização envelhece e gera alerta.

### História D — Tentativa frustrada

Destinatário ausente.

### História E — Geofence

Entrada gera `ProximaDoDestino`.

### História F — Proof of Delivery

Conclusão com evidência.

## Determinismo

Usar cenário e seed fixos.

Permitir reset seguro apenas em ambiente demo.

## Modo acelerado

Suportar multiplicador de tempo para demo.

## Critérios de aceite

Resetar e reproduzir deve levar ao mesmo storytelling.

---

# FASE 24 — UX FINAL E MODO DEMONSTRAÇÃO

## Objetivo

Transformar a aplicação funcional em uma demonstração de portfólio excelente.

## Entrada

Botão:

> Explorar demonstração

Sem onboarding comercial.

## Demo

Usuário entra em organização controlada com permissões adequadas.

Pode haver:

- pausar simulação;
- retomar;
- reiniciar cenário,

somente em ambiente de demo e devidamente protegido.

## Refinos

- empty states;
- loading;
- reconnect;
- offline indicators;
- errors;
- responsividade;
- acessibilidade;
- keyboard;
- mapa;
- timeline;
- consistência visual.

## Screenshots planejadas

No mínimo:

1. mapa operacional;
2. entrega com ETA/SLA;
3. motorista/rota;
4. alerta;
5. PWA motorista;
6. tracking público;
7. proof of delivery.

## Critérios de aceite

Produto deve parecer operação real sem depender de explicação externa.

---

# FASE 25 — INFRAESTRUTURA E DEPLOY

## Objetivo

Escolher infraestrutura a partir dos requisitos reais, especialmente WebSocket.

## Gate de arquitetura

Antes de provisionar, comparar opções para backend com:

- ASP.NET Core;
- WebSockets/SignalR;
- custo baixo;
- deploy;
- scale-to-zero ou baixo idle cost;
- observabilidade;
- banco;
- storage;
- filas.

Não copiar automaticamente a arquitetura Lambda da Central Antifraude.

## Banco

PostgreSQL + PostGIS gerenciado.

## Frontends

Deploy público.

## Storage

Objeto privado.

## Mensageria

SQS se a arquitetura final mantiver AWS e fizer sentido.

## Secrets

Secret manager/SSM equivalente.

## IaC

Infra reprodutível.

## Custo

Criar `docs/cost-model.md`.

Custo de portfólio deve ser baixo e previsível.

Qualquer recurso com risco de cobrança relevante exige aprovação antes.

## Critérios de aceite

- app pública;
- API pública protegida;
- realtime funcional;
- workers;
- storage;
- DB;
- health;
- logs;
- HTTPS;
- secrets fora do código.

Deploy exige autorização explícita.

---

# FASE 26 — VALIDAÇÃO EM PRODUÇÃO E PENTEST

## Objetivo

Validar o sistema real publicado, não apenas localhost.

## Jornada funcional obrigatória

Executar ponta a ponta:

```text
demo
→ rota
→ posição
→ realtime
→ ETA
→ risco
→ alerta
→ geofence
→ PWA
→ offline/retry
→ tentativa/frustração
→ proof of delivery
→ tracking público
→ webhook
→ auditoria
```

## Produção

Testar também:

- cold start;
- connection pooling;
- WebSocket reconnect;
- CORS;
- cookies;
- cache;
- signed URLs;
- headers;
- workers;
- DLQ;
- provider failures.

## Pentest gray-box

Cobrir no mínimo:

- auth bypass;
- IDOR;
- cross-tenant;
- RBAC;
- tracking token;
- brute force;
- CSRF;
- CORS;
- XSS;
- upload;
- signed URL;
- path/content type;
- webhook forgery;
- integration key;
- idempotency abuse;
- rate limiting;
- session reuse;
- GPS spoofing básico/validação;
- mass assignment;
- error leakage;
- secrets/logs.

Produzir:

`docs/pentest-v1.md`

## Dependências

Executar scans de backend, frontend, containers/IaC quando aplicável.

## Critérios de aceite

Zero vulnerabilidade conhecida de alta severidade não aceita.

Qualquer risco aceito deve ser documentado.

---

# FASE 27 — RELEASE v1.0.0

## Objetivo

Congelar a primeira versão completa do projeto.

## README final

Deve explicar:

- problema;
- produto;
- público;
- fluxo;
- screenshots;
- vídeo;
- arquitetura;
- stack;
- PostGIS;
- offline;
- idempotência;
- SignalR;
- Outbox;
- segurança;
- observabilidade;
- performance;
- demo;
- limitações;
- como executar;
- decisões arquiteturais.

## Vídeo

Vídeo curto, sem código/terminal.

Roteiro provável:

```text
entrada demo
→ mapa operacional
→ alerta de risco
→ detalhe da entrega / ETA
→ motorista/PWA
→ geofence/realtime
→ proof of delivery
→ tracking público
```

Alvo aproximado:

20–35 segundos.

## GitHub

Preparar:

- description;
- homepage;
- topics;
- screenshots;
- video user-attachment;
- branch protection;
- dependabot;
- security settings disponíveis.

## Release

Somente com autorização explícita:

- commit final;
- push;
- tag `v1.0.0`;
- GitHub Release.

## Congelamento

Após release:

> não reabrir escopo por melhoria cosmética infinita.

Somente:

- bug real;
- vulnerabilidade;
- dependência importante;
- melhoria claramente valiosa.

## Critérios de aceite

Torre Logística deve poder ser apresentada como:

> Plataforma B2B de operação logística em tempo real, com geolocalização, PostGIS, ETA/SLA, PWA offline, SignalR, processamento idempotente, proof of delivery e rastreamento público.

---

# 5. GATES TRANSVERSAIS OBRIGATÓRIOS

Além dos Security Gates específicos, Claude deve verificar ao longo do projeto:

## Gate de tenant

Toda nova entidade tenant-owned deve ganhar teste cross-tenant.

## Gate de idempotência

Todo comando que possa ser repetido por rede/offline deve responder:

> o que acontece se chegar duas vezes?

## Gate de concorrência

Toda operação que altera dono/status/atribuição deve avaliar corrida concorrente.

## Gate de histórico

Toda informação operacional relevante deve responder:

> o passado pode ser explicado depois?

## Gate de localização

Toda feature de GPS deve responder:

- precisamos realmente guardar isso?
- por quanto tempo?
- quem pode ver?
- posição antiga pode alterar estado atual?

## Gate de arquivo

Todo upload deve responder:

- é privado?
- tipo/tamanho validados?
- autorização correta?
- lifecycle/retention?

## Gate de realtime

Realtime nunca pode ser a única fonte da verdade.

Banco continua autoritativo.

## Gate de demo

Simulador e ferramentas de reset nunca podem estar disponíveis de forma insegura em ambiente produtivo real.

---

# 6. CENÁRIOS DE REGRESSÃO QUE DEVEM SOBREVIVER ATÉ O FIM

Estes cenários devem virar testes permanentes:

## R1 — GPS duplicado

Mesma posição enviada duas vezes gera um registro lógico.

## R2 — GPS fora de ordem

Evento antigo não regressa posição atual.

## R3 — conclusão repetida

Retry não cria duas conclusões.

## R4 — conflito offline

Mudança remota vence conforme regra explícita; cliente recebe conflito, não sobrescreve silenciosamente.

## R5 — geofence duplicado

Permanecer dentro não cria N entradas.

## R6 — tenant isolation

Tenant A não acessa tenant B.

## R7 — tracking público

Token inválido não vaza recurso.

## R8 — comprovante

Arquivo de tenant A não é acessível por tenant B.

## R9 — webhook duplicado

At-least-once não duplica efeito do consumidor interno.

## R10 — concorrência de atribuição

Duas alterações simultâneas não corrompem estado.

## R11 — alerta repetitivo

Mesmo risco não cria spam infinito.

## R12 — simulador

Usa APIs reais e cenário reproduzível.

---

# 7. DOCUMENTAÇÃO ESPERADA AO FINAL

Diretório `docs/` deverá conter, no mínimo:

```text
docs/
  adr/
  architecture.md
  domain-model.md
  state-machine.md
  gps-and-geospatial.md
  offline-sync.md
  security-model.md
  observability.md
  performance.md
  cost-model.md
  demo.md
  pentest-v1.md
  production-validation.md
```

Documentos podem nascer gradualmente nas fases correspondentes.

---

# 8. REGRA FINAL DE EXECUÇÃO

Quando Lucas disser:

> **siga para a próxima fase**

Claude deve:

1. ler `CLAUDE.md`;
2. ler `ROADMAP.md`;
3. localizar a primeira fase não concluída;
4. revisar dependências;
5. implementar a fase inteira;
6. testar;
7. corrigir falhas;
8. executar Security Gate;
9. atualizar documentação;
10. atualizar status e evidências no roadmap;
11. criar commit local;
12. parar somente quando a fase estiver realmente concluída ou em um gate legítimo.

Claude não deve pedir confirmação para:

- criar arquivos normais;
- criar migrations reversíveis;
- escrever testes;
- corrigir bugs da fase;
- refatorar dentro da arquitetura aprovada;
- atualizar documentação;
- executar comandos locais seguros;
- criar commit local.

Claude deve pedir confirmação para:

- alterar stack/arquitetura congelada;
- executar ação irreversível;
- usar secret ausente;
- provisionar custo relevante;
- fazer push;
- fazer deploy;
- criar tag/release;
- tomar decisão de negócio não inferível.

---

# 9. ESTADO ATUAL

| | |
|---|---|
| Última fase concluída | **Fase 0 — Fundação Técnica** (2026-09-09) |
| Próxima fase | **Fase 1 — Identidade e Multi-tenancy** |
| Testes verdes | 131 — 43 unidade, 16 arquitetura, 32 integração, 40 frontend |

Comando para continuar:

> **siga para a próxima fase**
