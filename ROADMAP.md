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
| 1 | Identidade e Multi-tenancy | ✅ |
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

## Fase 1 concluída — 2026-09-14

### Entregáveis

| Entregável | Situação | Onde |
|---|:---:|---|
| Organização, Usuário, Sessão, Perfil | ✅ | `src/TorreLogistica.Domain/Identidade/` |
| Convite | não implementado, por decisão | ver "Decisões" abaixo |
| Perfis Administrador, Supervisor, Operador, Motorista | ✅ | `Perfil`, `CanalDeAcesso` |
| Login e logout | ✅ | `/api/autenticacao/{login,sair}` e `/api/motorista/autenticacao/{login,sair}` |
| Token de acesso curto | ✅ | JWT HS256, 15 min |
| Refresh token rotativo | ✅ | opaco, hash SHA-256, cookie `HttpOnly; Secure; SameSite=Strict; Path` |
| Reuse detection e revogação de família | ✅ | `PoliticaDeRenovacao` + trava de linha na sessão |
| Autorização no backend | ✅ | políticas por perfil, fallback exigindo sessão |
| Tenant resolvido pela identidade | ✅ | `ContextoDoUsuarioHttp` + filtro global que falha fechado |
| Cross-tenant → 404 | ✅ | idêntico ao de identificador inexistente |
| Motorista com autoridade separada | ✅ | esquema e audiência próprios; token não autentica no console |
| Tela de login do console | ✅ | `apps/operacao` — sessão em memória, renovação serializada, rota protegida |

### Invariantes, um por um

| Invariante | Resultado | Evidência |
|---|:---:|---|
| `OrganizacaoId` de payload nunca é autoridade | ✅ | `OrganizacaoInformadaNoCorpoEhRecusada`: 400 e nada criado |
| Usuário não troca de tenant por parâmetro | ✅ | nenhuma rota recebe organização; `ContaDeBNaoAutenticaUsandoOIdentificadorDeA` |
| Motorista não herda permissões de Operador | ✅ | matriz: 401 em toda rota do console; `ContaDeMotoristaNaoViraContaDoConsole` (422) |
| Refresh token reutilizado revoga a família | ✅ | `ReusoDeTokenRevogaAFamiliaInteiraERegistraAuditoria`; `RepeticaoDentroDaJanelaEhAceitaMasSucessorDescartadoDenunciaReuso` |
| Recurso de outro tenant não vaza existência | ✅ | 404 comparado campo a campo com identificador inexistente; e-mail de B cadastra em A sem conflito |

### Critério de aceite

> tenant A não consegue ler, alterar ou inferir recurso do tenant B

✅ `IsolamentoEntreTenantsTestes` (9 provas) contra PostgreSQL real: leitura, troca de perfil,
desativação, listagem, mass assignment, inferência por conflito de e-mail, login cruzado e o
filtro global direto no contexto — com tenant A enxerga só A, sem tenant enxerga zero.

### Testes pedidos pelo roadmap

| Pedido | Onde |
|---|---|
| login válido/inválido | `AutenticacaoTestes` — inclusive 7 causas de falha indistinguíveis |
| refresh e rotação | `RenovacaoTestes` |
| reuse | `ReusoDeTokenTestes`, `RenovacaoTestes` |
| logout | `RenovacaoTestes.LogoutEncerraASessaoNaHora` |
| RBAC | `AutorizacaoTestes` — 40 casos perfil × rota + matriz contra o roteamento real |
| isolamento entre tenants | `IsolamentoEntreTenantsTestes` |
| motorista tentando rota administrativa | `AutorizacaoTestes` (401 em todas) |
| operador tentando administração | `AutorizacaoTestes` (403) |
| acesso cross-tenant | `IsolamentoEntreTenantsTestes` |

Além do pedido: prazos com relógio controlado, bloqueio temporário, 10 renovações simultâneas,
criações simultâneas com mesmo e-mail, rebaixamentos cruzados simultâneos de administradores,
auditoria somente-inserção recusando `UPDATE`/`DELETE`/`TRUNCATE`, JWT adulterado, assinado com
outra chave e com `alg: none`.

### Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 121 | ✅ |
| `TorreLogistica.ArchitectureTests` | 19 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 128 | ✅ |
| Frontend — `operacao` | 24 | ✅ |
| Frontend — `motorista` | 12 | ✅ |
| Frontend — `rastreamento` | 13 | ✅ |
| **Total** | **317** | **✅** |

Lint e verificação de tipos limpos nas três aplicações; os três builds verdes.

**Validação contra a API real** (processo em Development, banco local, semeadura habilitada):
19/19 verificações — atributos do cookie, RBAC, 404 cross-tenant idêntico ao inexistente,
motorista recusado no console e aceito na PWA, renovação, renovação sem `Origin` (403), logout
imediato, preflight CORS com credenciais e login de origem desconhecida (403). A migration
`Identidade` aplicou no banco local na subida.

### Security Gate 1

| Item | Resultado | Evidência |
|---|:---:|---|
| Cookies e tokens | ✅ | cookie `HttpOnly; Secure; SameSite=Strict; Path` por canal; token de renovação fora do corpo; só hash no banco; JWT com emissor, audiência, algoritmo e validade exigidos |
| CSRF | ✅ | `SameSite=Strict` **e** `Origin` obrigatório e conhecido em login, renovação e logout |
| Brute force e rate limiting | ✅ | 10 logins/min por endereço antes de qualquer hash (429 com `Retry-After`); bloqueio de 15 min após 5 falhas, contador atômico |
| Enumeração de usuários | ✅ | resposta idêntica para 7 causas de falha; hash calculado em todos os caminhos; e-mail único por organização |
| Sessão | ✅ | conferida a cada requisição; logout, desativação, troca de perfil e reuso valem na hora; prazo absoluto |
| Headers | ✅ | suíte da Fase 0 verde; `Cache-Control: no-store` nas respostas com token |
| Logs de autenticação sem token | ✅ | `LogsSemSegredoTestes`: nenhum token, cookie, senha ou e-mail no log — com prova de que o log foi capturado |
| Segredos | ✅ | gitleaks: 0 achados em 661 KB; chave de assinatura obrigatória fora de Development/Testing |
| Dependências | ✅ | `dotnet list package --vulnerable --include-transitive`: nada; `--deprecated`: nada; `npm audit`: 0 |

### Defeitos encontrados e corrigidos durante a fase

1. **Hash de senha corrompido derrubava o login com 500.** O `PasswordHasher` do Identity lança
   `FormatException` com hash que não é Base64. Além da indisponibilidade, só contas **existentes**
   dariam 500 — um oráculo de enumeração. Encontrado por teste de unidade; corrigido para recusar,
   gastar o tempo de uma conferência real e registrar o problema em log sem o valor.

### Comportamentos que mudaram e testes da Fase 0 ajustados

A política de *fallback* passou a exigir sessão em todo endpoint sem anotação. Consequência
deliberada: rota inexistente sem sessão responde 401, igual a rota existente — o mapa de rotas não
é levantável sem login. Três testes da Fase 0 dependiam do comportamento antigo e foram ajustados,
não afrouxados: as rotas de erro de teste passaram a ser chamadas autenticadas, a rota inexistente
é verificada com e sem sessão, e a ausência do OpenAPI fora de desenvolvimento é verificada com
sessão (404) para não confundir "não publicado" com "sem sessão".

### Decisões

ADRs [0008](./docs/adr/0008-application-usa-ef-core-sem-repositorio.md),
[0009](./docs/adr/0009-autenticacao-e-sessao.md) e
[0010](./docs/adr/0010-multi-tenancy-e-isolamento.md); matriz em
[`docs/seguranca/matriz-de-autorizacao.md`](./docs/seguranca/matriz-de-autorizacao.md).

- **E-mail único por organização**, com login por organização + e-mail + senha. Unicidade global
  faria o conflito de cadastro revelar contas de outro tenant.
- **Convite não implementado.** O roadmap o condiciona a "se necessário para demo/administração":
  administrador cria conta com senha inicial, e a semeadura local cobre o desenvolvimento. A demo
  pública terá seed próprio (Fases 23 e 24).
- **Tela de login da PWA do motorista fica para a Fase 11**, onde o roadmap a lista. O endpoint
  existe e é testado.
- **Application passa a usar o núcleo do EF Core** — altera a tabela do ADR 0001, registrado no ADR 0008.

### Pendências conhecidas

| Item | Situação |
|---|---|
| CI nunca executada | exige `git push`, não autorizado |
| Endereço de origem atrás de proxy | o limite por endereço verá o do proxy até os cabeçalhos encaminhados serem tratados — Fase 25 |
| `SameSite=Strict` pressupõe console e API no mesmo domínio registrável | verdadeiro em localhost; a topologia de produção é decidida na Fase 25 e, se divergir, o ADR 0009 precisa ser revisto |
| Bloqueio temporário usável contra a vítima | quem sabe organização e e-mail consegue atrasar o login dela por 15 min; aceito na v1 e documentado no ADR 0009 |
| Log de erro do EF Core em violação de unicidade tratada | o `409` de e-mail duplicado em corrida gera também um log `Error` do próprio EF Core; ruído de observabilidade a tratar na Fase 21 |
| Uma consulta de sessão por requisição autenticada | desprezível na escala atual; medir na Fase 22 |
| Node 22.17.0 na máquina | Vite 8 pede ≥ 22.22.0; tudo passa, o aviso continua real |

### Commit

`feat: identidade, sessao e isolamento entre organizacoes (Fase 1)`

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

## Fase 2 concluída — 2026-09-14

### Entregáveis

| Entregável | Situação | Onde |
|---|:---:|---|
| Motorista: cadastrar, ativar/inativar, dados mínimos | ✅ | `Domain/Frota/Motorista.cs`, `/api/motoristas` |
| Motorista: associar identidade | ✅ | `PUT` e `DELETE /api/motoristas/{id}/conta` — só conta ativa, perfil Motorista, mesma organização |
| Veículo: placa, identificação, tipo, capacidade, ativo/inativo | ✅ | `Domain/Frota/Veiculo.cs`, `/api/veiculos` |
| Hub: nome, endereço, coordenada geográfica | ✅ | `Domain/Operacao/Hub.cs`, coluna `geography(Point,4326)` com índice GiST |
| Cliente: contratante, sem CRM | ✅ | `Domain/Clientes/Cliente.cs` — nome e CNPJ opcional, nada além |
| Destinatário: endereço, contato mínimo | ✅ | `Domain/Clientes/Destinatario.cs` — telefone opcional, instruções, coordenada opcional |
| Migration | ✅ | `EstruturaOperacional` — 5 tabelas, 4 índices únicos, 2 GiST, 1 check |

### Regras, uma por uma

| Regra | Resultado | Evidência |
|---|:---:|---|
| Motorista inativo não recebe nova atribuição | ✅ no domínio | `MotoristaInativoNaoRecebeAtribuicao`; chamada pela atribuição da Fase 4 |
| Veículo inativo não inicia nova rota | ✅ no domínio | `VeiculoInativoNaoIniciaRota`; chamada pelo início de rota da Fase 4 |
| Dados sensíveis minimizados | ✅ | destinatário sem CPF nem e-mail; auditoria só com nomes de campos (`AuditoriaDeDestinatarioNaoGuardaDadoPessoal`, que também confere o log) |
| Exclusão lógica onde histórico impede deleção física | ✅ | nenhum `DELETE` de cadastro; ativar/inativar idempotentes e auditados só quando mudam |

### Critério de aceite

> Supervisor consegue preparar uma organização operacional sem criar entrega ainda.

✅ `SupervisorPreparaAOrganizacaoOperacionalSemCriarEntrega`, contra PostgreSQL real, com sessão de
**Supervisor**: cria hub, cliente, destinatário, veículo e motorista e associa a conta do motorista. O Operador
da mesma organização lista os cinco cadastros preparados e recebe 403 ao tentar criar. A trilha
de auditoria de cada cadastro é conferida em `CicloCompletoDeCadastroComVersaoEAuditoria`.

### Testes pedidos pelo roadmap

| Pedido | Onde |
|---|---|
| tenant isolation | `CadastroDeOutraOrganizacaoRespondeComoInexistente` — nos 5 cadastros: leitura, alteração, ativação e inativação respondem 404 idêntico ao inexistente, e o registro de B segue intacto (mesma versão) |
| regras de ativo/inativo | `CicloCompletoDeCadastroComVersaoEAuditoria` (5 cadastros), `AtivarEInativarSaoIdempotentes`, regras de inativo no domínio |
| unicidades por organização | placa sem diferença de formato, hub sem diferença de acento ou caixa, CNPJ quando informado, conta do motorista — cada uma com a mesma chave aceita em outra organização |
| endereços inválidos | `DadoForaDaRegraDevolve422ComCodigo` pela API (CEP, UF, coordenada fora da faixa, `(0,0)`, meia coordenada, telefone, placa, CNPJ, capacidade) e testes de unidade de `Endereco` (inclusive endereço incompleto) |
| autorização | `AutorizacaoTestes` — linhas novas da matriz e mapa de rotas conferido contra o roteamento real |

Além do pedido: seis `PUT` simultâneos com a mesma versão (um vencedor, cinco `409`), associações
simultâneas da mesma conta (um vencedor), busca sem acento com `%` tratado como texto, campo
desconhecido e `organizacaoId` no corpo recusados com 400, e a coordenada lida do PostGIS por
`GeometryType`, `ST_SRID`, `ST_X`, `ST_Y` e `ST_Distance` — prova de que longitude e latitude não
foram invertidas.

### Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 184 | ✅ |
| `TorreLogistica.ArchitectureTests` | 19 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 213 | ✅ |
| Frontend — `operacao` | 24 | ✅ |
| Frontend — `motorista` | 12 | ✅ |
| Frontend — `rastreamento` | 13 | ✅ |
| **Total** | **465** | **✅** |

Solução compila com 0 aviso e 0 erro (analisadores tratados como erro). Lint, tipos e builds do
frontend verdes — sem alteração de frontend nesta fase.

### Security Gate 2

| Item | Resultado | Evidência |
|---|:---:|---|
| Isolamento entre tenants e IDOR | ✅ | 404 cross-tenant nos 5 cadastros e em todas as operações; conta de outra organização na associação responde 404 |
| Mass assignment | ✅ | `organizacaoId`, campo desconhecido e tipo errado recusados com 400; `ativo` e `usuarioId` só mudam por comando próprio |
| Autorização | ✅ | leitura A/S/O, gestão A/S; matriz verificada contra o roteamento |
| Conflito não revela outro tenant | ✅ | unicidades por organização; mesma placa, nome e CNPJ aceitos em outra organização |
| Dado pessoal | ✅ | auditoria sem valores; log de cadastro só com identificadores; `AuditoriaDeDestinatarioNaoGuardaDadoPessoal` procura o dado pessoal do destinatário na trilha e no log capturado |
| Injeção por texto | ✅ | busca parametrizada com curinga escapado; caractere de controle recusado em nomes (quebra de linha forjada em log) |
| Segredos | ✅ | gitleaks v8.30.1: 0 achados em 927 KB |
| Dependências | ✅ | `dotnet list package --vulnerable --include-transitive`: nada; `--deprecated`: nada; `npm audit`: 0. Pacote novo: `Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite` 10.0.3, restrito à Infrastructure |

### Defeitos encontrados e corrigidos durante a fase

1. **Busca e unicidade sem acento não funcionavam.** A solução roda com `InvariantGlobalization`,
   e nesse modo `Normalize(FormD)` não separa o acento da letra: "Hub São José" e "hub sao jose"
   seriam hubs diferentes. Encontrado por teste de unidade e por dois testes de integração;
   corrigido com tabela explícita de letras, que também torna a forma gravada idêntica em qualquer
   sistema operacional — importante, porque ela sustenta índice único.
2. **Quebra de linha escapava da recusa de caractere de controle.** A checagem rodava depois de
   aparar o texto, que já tinha convertido `\n` em espaço. Passou a olhar o texto recebido.
   Tabulação continua aceita e vira espaço (chega de planilha colada); o teste antigo misturava os
   dois casos numa teoria com desvio por `if` e foi separado em dois.

### Decisões

[ADR 0011](./docs/adr/0011-cadastros-operacionais.md); matriz atualizada em
[`docs/seguranca/matriz-de-autorizacao.md`](./docs/seguranca/matriz-de-autorizacao.md).

- **Coordenada como tipo próprio do domínio**, convertida para `Point` só na Infrastructure; teste
  de arquitetura proíbe NetTopologySuite no domínio.
- **Concorrência otimista por `xmin`** com `versao` obrigatória no `PUT`; comandos de estado sem versão.
- **Endereço e telefone brasileiros na v1.** Operação fora do Brasil pede ADR novo.
- **CNPJ alfanumérico aceito desde já**, com o exemplo oficial da Receita em teste.
- **Motorista e conta são coisas separadas**; trocar a conta exige desassociar antes.

### Pendências conhecidas

| Item | Situação |
|---|---|
| CI nunca executada | exige `git push`, não autorizado |
| Log de erro do EF Core em violação de unicidade tratada | continua da Fase 1, agora também em placa, hub, CNPJ e conta — Fase 21 |
| Regras de inativo ainda sem chamador | por desenho: atribuição e início de rota nascem na Fase 4 |
| Destinatário sem política de retenção | anonimização é da Fase 20 (ADR 0011) |
| Busca por `ILIKE` sem índice de trigrama | suficiente na escala de cadastros; medir na Fase 22 |
| Node 22.17.0 na máquina | Vite 8 pede ≥ 22.22.0; tudo passa, o aviso continua real |

### Commit

`feat: frota e estrutura operacional com PostGIS e concorrencia otimista (Fase 2)`

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

## Fase 3 concluída — 2026-09-14

### Modelo, item por item

| Pedido | Situação | Onde |
|---|:---:|---|
| UUIDv7 | ✅ | `Entrega.Id`, gerado pelo `IGeradorDeIdentificador` |
| Código humano | ✅ | `CodigoDaEntrega` — `ENT-AAAA-NNNNNN`, contador `sequencias_de_codigo` |
| Organização | ✅ | filtro global de tenant, como nos cadastros |
| Cliente e destinatário | ✅ | chaves estrangeiras; referência nova precisa existir no tenant e estar ativa |
| Endereço | ✅ | cópia na criação (do destinatário, se não informado) |
| Coordenada de destino quando disponível | ✅ | `geography(Point,4326)` opcional, índice GiST |
| Janela prometida | ✅ | `JanelaDeEntrega` — `prometida_de`, `prometida_ate` |
| Status | ✅ | `StatusDaEntrega` com os 9 status do CLAUDE.md; nesta fase só Criada e Cancelada são alcançáveis |
| Timestamps operacionais | ✅ | `criada_em`, `atualizada_em`, `cancelada_em`; os de rota e conclusão entram com as operações deles |
| Versão de concorrência | ✅ | `xmin`, `versao` obrigatória no `PUT` |
| Timeline `EventoDaEntrega` append-only | ✅ | sequência por entrega, status resultante, trigger recusando `UPDATE`/`DELETE`/`TRUNCATE` |

### Ações e regras

| Ação ou regra | Resultado | Evidência |
|---|:---:|---|
| Criar | ✅ | `POST /api/entregas` — entrega, evento e auditoria no mesmo commit |
| Editar dados permitidos antes do início | ✅ | `PUT /api/entregas/{id}`; tabela campo × status |
| Cancelar dentro das regras | ✅ | `POST .../cancelamento` com motivo; permitido antes da rota e após tentativa frustrada; idempotente |
| Consultar, listar, filtrar | ✅ | `GET /api/entregas/{id}`, `GET /api/entregas` (status, cliente, destinatário, código, período), `GET .../eventos` |
| Histórico não reescrito silenciosamente | ✅ | timeline somente-inserção no banco; endereço copiado — alterar o destinatário não muda a entrega |
| Campos imutáveis após o início explicitamente protegidos | ✅ | `RegrasDaEntrega.CampoEditavel`, testada para os 6 campos × 9 status; alteração é tudo ou nada (`409 campo_nao_editavel`) |

### Critério de aceite

> A API deve permitir criar e consultar uma entrega com histórico consistente sem depender ainda
> de rota ou GPS.

✅ `ApiCriaEConsultaEntregaComHistoricoConsistente`, contra PostgreSQL real, com sessão de
**Operador**: cria a entrega, confere código, status, endereço copiado do destinatário, janela e
`Location`; a leitura devolve exatamente o que a criação devolveu; a lista traz a entrega; a timeline
tem um evento `Criada`, sequência 1, status resultante `Criada` e autor correto; a auditoria registra
`entrega_criada`. Consistência ao longo de mudanças concorrentes em
`AlteracaoECancelamentoSimultaneosMantemAHistoriaCoerente`: sequência sem lacuna, último status
resultante igual ao status atual, e cancelamento nunca seguido de alteração.

### Testes pedidos pelo roadmap

| Pedido | Onde |
|---|---|
| criação | `ApiCriaEConsultaEntregaComHistoricoConsistente`; `EntregaTestes.CriarGeraEntregaCriadaEOPrimeiroEventoDaTimeline` |
| código humano único por tenant | `CodigoHumanoEhSequencialSemRepeticaoEPorOrganizacao` — 12 criações simultâneas numeradas de 1 a 12, outra organização começando em 1; `CadastroDeOutraOrganizacaoNaoServeParaEntrega` prova que tentativa recusada não consome número |
| atualização permitida | `AlteracaoPermitidaRegistraOsCamposNaTimelineEExigeVersao` |
| atualização proibida | `CancelamentoEncerraAEntregaEBloqueiaAlteracao` (409 `entrega_nao_editavel`), `RegrasDaEntregaTestes.EdicaoPorCampoEStatus` (54 casos), versão velha (409 `conflito_de_versao`) |
| cancelamento | `CancelamentoEncerraAEntregaEBloqueiaAlteracao`, `MotivoOutroSemDescricaoEhRecusadoSemCancelar`, `CancelamentosSimultaneosGeramUmUnicoEvento`, `RegrasDaEntregaTestes.CancelamentoPorStatus` |
| timeline | `TimelineRecusaAlteracaoEExclusaoNoBanco` (3 comandos), `EnderecoDaEntregaNaoMudaQuandoODestinatarioMuda`, eventos conferidos em todos os fluxos |
| isolamento | `EntregaDeOutraOrganizacaoRespondeComoInexistente` (leitura, timeline, alteração, cancelamento — 404 idêntico ao inexistente, entrega de B intacta) e `CadastroDeOutraOrganizacaoNaoServeParaEntrega` |

Além do pedido: cliente ou destinatário inativo recusado em entrega nova sem travar a existente,
seis regras de negócio com código próprio (422), seis corpos malformados ou com `status`, `codigo`
e `organizacaoId` (400, nada criado), filtros com status inválido e período invertido (400), e
ausência de dado pessoal na timeline, na auditoria e no log.

### Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 280 | ✅ |
| `TorreLogistica.ArchitectureTests` | 19 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 255 | ✅ |
| Frontend — `operacao` | 24 | ✅ |
| Frontend — `motorista` | 12 | ✅ |
| Frontend — `rastreamento` | 13 | ✅ |
| **Total** | **603** | **✅** |

Solução compila com 0 aviso e 0 erro; formatação verificada. Frontend sem alteração nesta fase.

### Security Gate 3

| Item | Resultado | Evidência |
|---|:---:|---|
| Isolamento entre tenants e IDOR | ✅ | 404 idêntico ao inexistente em leitura, timeline, alteração e cancelamento; cliente e destinatário de outra organização recusados como inexistentes |
| Mass assignment | ✅ | `status`, `codigo` e `organizacaoId` no corpo respondem 400; status muda só por operação de domínio |
| Autorização | ✅ | leitura A/S/O, operação de entregas A/S/O; matriz verificada contra o roteamento |
| Integridade do histórico | ✅ | trigger recusa `UPDATE`, `DELETE` e `TRUNCATE` na timeline; sequência única por entrega |
| Concorrência | ✅ | código humano sem repetição sob 12 criações simultâneas; cancelamentos simultâneos com um único evento; alteração × cancelamento sem história incoerente |
| Dado pessoal | ✅ | timeline e auditoria com nomes de campos e códigos de motivo; log com identificador e código; teste procura endereço, nome e observação nos três |
| Injeção por texto | ✅ | caractere de controle recusado em observações; filtro de status só aceita nome exato; código de busca limitado a 30 caracteres |
| Segredos | ✅ | gitleaks v8.30.1: 0 achados em 1,14 MB |
| Dependências | ✅ | nenhum pacote novo; `dotnet list package --vulnerable --include-transitive` e `--deprecated`: nada nos 9 projetos; `npm audit`: 0 |

### Defeitos encontrados e corrigidos durante a fase

1. **Código com quebra de linha no fim passava como válido.** A expressão regular terminava em `$`,
   que no .NET casa também antes de um `\n` final. Encontrado por teste de unidade; trocado por `\z`.
2. **Tipos de dados do domínio expunham `init` público.** `DadosDaEntrega` e `AlteracaoDaEntrega`
   nasceram como `record` posicional. O teste de arquitetura da Fase 1 recusou; viraram classes com
   propriedades somente-leitura.

### Decisões

[ADR 0012](./docs/adr/0012-entrega-como-agregado-central.md); matriz atualizada em
[`docs/seguranca/matriz-de-autorizacao.md`](./docs/seguranca/matriz-de-autorizacao.md).

- **Contador por organização e ano na transação do insert**, e não `SEQUENCE`: sequence não volta
  atrás no rollback e deixaria lacuna.
- **Endereço copiado**, não referenciado.
- **Timeline com sequência e status resultante**, para a consistência ser verificável.
- **Regras em tabela** cobrindo todos os status desde já; cliente congela no planejamento; demais
  dados de destino congelam na saída para rota; cancelamento em rota fica para as fases de rota e
  ocorrência.
- **Operador opera entregas**; estrutura operacional segue com administrador e supervisor.
- **Ano do código em UTC**, independente do fuso da organização.

### Pendências conhecidas

| Item | Situação |
|---|---|
| CI nunca executada | exige `git push`, não autorizado |
| Criação de entregas serializada por organização | pela linha do contador, só durante o insert; medir na Fase 22 |
| Plano de consulta da lista não medido | índice `(organizacao_id, status, prometida_ate)` criado; `EXPLAIN` com volume na Fase 22 |
| Timeline devolvida inteira, sem paginação | limitada a uma entrega; reavaliar quando GPS e geofence gerarem eventos (Fases 6 e 7) |
| Cancelamento com motorista em rota | recusado nesta fase; decisão nas fases de rota e ocorrência |
| Log de erro do EF Core em violação de unicidade tratada | continua das fases anteriores — Fase 21 |
| Node 22.17.0 na máquina | Vite 8 pede ≥ 22.22.0; tudo passa, o aviso continua real |

### Commit

`feat: entrega como agregado central com timeline e codigo humano (Fase 3)`

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

## Fase 4 concluída — 2026-09-14

### Entidades e funcionalidades

| Pedido | Situação | Onde |
|---|:---:|---|
| Rota | ✅ | `Domain/Rotas/Rota.cs` — código `ROT-AAAA-NNNN`, data, hub opcional, motorista, veículo, saída, status |
| Parada | ✅ | `Domain/Rotas/Parada.cs` — sequência, ativa, instante e motivo da retirada |
| Associação RotaEntrega | ✅ | a própria parada (uma parada, uma entrega) — decisão no ADR 0013 |
| Criar rota | ✅ | `POST /api/rotas` |
| Adicionar e remover entregas elegíveis | ✅ | `POST /api/rotas/{id}/paradas` (várias, tudo ou nada) e `DELETE .../paradas/{entregaId}` |
| Ordenar paradas | ✅ | `PUT .../ordem` com versão |
| Atribuir motorista e veículo | ✅ | `PUT .../motorista`, `PUT .../veiculo` |
| Planejar saída | ✅ | `PUT .../saida` e confirmação em `POST .../planejamento` |
| Consultar sequência | ✅ | `GET /api/rotas/{id}` com paradas em ordem (código, status, destinatário, endereço, janela); `GET .../eventos`; `GET /api/rotas` com filtros |

### Regras, uma por uma

| Regra | Resultado | Evidência |
|---|:---:|---|
| Entrega não pode estar em duas rotas ativas | ✅ | índice único parcial `paradas (entrega_id) WHERE ativa` + conferência prévia; `EntregaNaoFicaEmDuasRotasAtivas`, `InclusoesSimultaneasDaMesmaEntregaEmRotasDiferentesTemUmVencedor` |
| Rota concluída não aceita alteração estrutural | ✅ | `RegrasDaRota.PermiteAlteracaoEstrutural` testada para os 5 status; `RotaCanceladaNaoAceitaAlteracaoEstruturalEDevolveAsEntregas` prova as 6 operações recusadas numa rota final |
| Motorista e veículo inativos não podem ser atribuídos | ✅ | regras da Fase 2 agora chamadas pela rota; `MotoristaEVeiculoInativosNaoSaoAtribuidos` (422) |
| Ordem de paradas versionada e registrada | ✅ | `versao_da_ordem` + evento com ordem anterior, nova e versão; reordenar exige a versão lida da rota |

Além das regras pedidas: motorista e veículo em no máximo uma rota ativa por dia (índice parcial),
entrega cancelada retirada da rota na mesma gravação, rota planejada que perde a última parada
volta para montagem, e cancelamento de rota devolvendo as entregas a `Criada`.

### Critério de aceite

> Supervisor consegue montar rota do dia e atribuí-la.

✅ `SupervisorMontaARotaDoDiaEAAtribui`, contra PostgreSQL real, com sessão de **Supervisor**:
cria a rota para amanhã com hub, inclui três entregas, reordena com a versão lida, atribui
motorista e veículo, planeja a saída e confirma o planejamento. A rota fica `Planejada`, as três
entregas ficam `Atribuida` com timeline `Criada → Planejada → Atribuida`, a timeline da rota registra
as sete etapas em ordem, a auditoria registra criação, atribuições e planejamento, e o Operador
consulta a mesma sequência, mas recebe 403 ao tentar criar rota.

### Testes pedidos pelo roadmap

| Pedido | Onde |
|---|---|
| duplicidade | `EntregaNaoFicaEmDuasRotasAtivas` — outra rota, mesma rota, lista mista tudo ou nada, liberação por cancelamento, entrega cancelada; `RotaTestes.EntregaRepetidaNaRotaEhRecusadaSemIncluirNada` |
| concorrência de atribuição | `AtribuicoesSimultaneasDoMesmoMotoristaNoMesmoDiaTemUmVencedor` (4 rotas, 1 vencedora); `InclusoesSimultaneasDaMesmaEntregaEmRotasDiferentesTemUmVencedor` (5 rotas, 1 parada ativa, 1 evento `Planejada`) |
| remoção | `RemocaoDevolveAEntregaERenumeraAsParadas`; `CancelarEntregaDeRotaPlanejadaRetiraAParadaEAVoltaParaMontagem` |
| reorder | `ReordenacaoExigePermutacaoExataEVersaoERegistraAsDuasOrdens` — faltando, repetida, estranha, versão velha |
| tenant isolation | `RotaDeOutraOrganizacaoRespondeComoInexistente` (10 operações, 404 idêntico ao inexistente, rota de B intacta) e `CadastrosDeOutraOrganizacaoNaoEntramNaRota` (entrega, motorista, veículo, hub) |

### Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 316 | ✅ |
| `TorreLogistica.ArchitectureTests` | 19 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 289 | ✅ |
| Frontend — `operacao` | 24 | ✅ |
| Frontend — `motorista` | 12 | ✅ |
| Frontend — `rastreamento` | 13 | ✅ |
| **Total** | **673** | **✅** |

Solução compila com 0 aviso e 0 erro; formatação verificada. Frontend sem alteração nesta fase.

### Security Gate 4

| Item | Resultado | Evidência |
|---|:---:|---|
| Isolamento entre tenants e IDOR | ✅ | 404 idêntico ao inexistente em leitura, timeline e nas 8 operações de montagem; entrega, motorista, veículo e hub de outra organização recusados como inexistentes |
| Autorização | ✅ | leitura A/S/O; montagem A/S; matriz verificada contra o roteamento |
| Mass assignment | ✅ | `status` e `organizacaoId` no corpo da rota respondem 400; status muda só por operação |
| Integridade entre agregados | ✅ | índices únicos parciais para entrega, motorista e veículo; versão da linha da rota e da entrega |
| Integridade do histórico | ✅ | timeline da rota recusa `UPDATE`, `DELETE` e `TRUNCATE`; parada retirada fica inativa, não é apagada |
| Dado pessoal | ✅ | eventos da rota com identificadores e códigos; log só com identificador e código |
| Segredos | ✅ | gitleaks v8.30.1: 0 achados em 1,38 MB |
| Dependências | ✅ | nenhum pacote novo; `dotnet list package --vulnerable --include-transitive` e `--deprecated`: nada nos 9 projetos; `npm audit`: 0 |

### Defeitos encontrados e corrigidos durante a fase

Nenhum defeito de comportamento. Os erros desta fase foram de compilação nos testes (expressão de
coleção sem tipo de destino e arrays constantes recusados pelo analisador), corrigidos antes da
primeira execução.

### Decisões

[ADR 0013](./docs/adr/0013-rotas-e-paradas.md); matriz atualizada em
[`docs/seguranca/matriz-de-autorizacao.md`](./docs/seguranca/matriz-de-autorizacao.md).

- **Parada é a associação rota–entrega**, 1:1, e fica inativa ao sair — não é apagada.
- **Regras que atravessam rotas ficam no banco**, em índices únicos parciais; a aplicação confere
  antes só para dar a mensagem.
- **A rota coordena as paradas; a aplicação coordena rota e entrega** na mesma gravação.
- **Só reordenar exige versão**; incluir, retirar e atribuir são comandos.
- **Data da rota com um dia de tolerância em UTC**, até existir fuso por organização.
- **Leitura de status compartilhada** (`Api/Comum/LeituraDeStatus`) entre entregas e rotas.

### Pendências conhecidas

| Item | Situação |
|---|---|
| CI nunca executada | exige `git push`, não autorizado |
| Fuso horário por organização | validações de data e saída usam UTC com tolerância de um dia; exatas quando a configuração existir |
| Duas inclusões simultâneas de entregas diferentes na mesma rota | a segunda recebe `409 conflito_de_versao` e repete; inclusão em lote reduz a chance |
| Iniciar e concluir rota | nascem com a execução pelo motorista; as tabelas de regra já cobrem os status |
| Log de erro do EF Core em violação de unicidade tratada | agora também nas corridas de rota — Fase 21 |
| Node 22.17.0 na máquina | Vite 8 pede ≥ 22.22.0; tudo passa, o aviso continua real |

### Commit

`feat: rotas e paradas com atribuicao e ordem versionada (Fase 4)`

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

## Fase 5 concluída — 2026-09-14

### Estados e comandos

| Pedido | Situação | Onde |
|---|:---:|---|
| 9 estados mínimos | ✅ | `StatusDaEntrega`, nomes do roadmap mantidos |
| Planejar, Atribuir | ✅ | desde a Fase 4, agora consultando a máquina de estados |
| IniciarRota | ✅ | `POST /api/motorista/rotas/{id}/inicio` — rota em andamento e entregas em rota |
| RegistrarChegada | ✅ | `POST /api/motorista/entregas/{id}/chegada` |
| RegistrarTentativaFrustrada | ✅ | `POST /api/motorista/entregas/{id}/tentativa-frustrada` com motivo |
| Reagendar | ✅ | `POST /api/entregas/{id}/reagendamento` com nova janela |
| Concluir | ✅ | `POST /api/motorista/entregas/{id}/conclusao`; rota: `POST /api/motorista/rotas/{id}/conclusao` |
| Cancelar | ✅ | desde a Fase 3, agora consultando a máquina de estados |
| Reatribuir (cenário obrigatório) | ✅ | `PUT /api/rotas/{id}/motorista` aceito também com a rota em andamento |
| Tabela única de transições | ✅ | `MaquinaDeEstadosDaEntrega`; um único método privado muda `Status` |

### Concorrência

| Pedido | Resultado | Evidência |
|---|:---:|---|
| Controle otimista | ✅ | versão da linha (`xmin`) em entrega e rota em todo comando |
| Operador reatribui enquanto motorista conclui | ✅ | `OperadorReatribuiEnquantoMotoristaConcluiApenasUmaSequenciaPrevalece` — seis rodadas concorrentes: nunca as duas vencem sobre a mesma entrega; quem perde recebe 409 e a sequência vencedora continua válida |
| Apenas uma sequência válida prevalece | ✅ | concluída pelo motorista original **sem** reatribuição, ou reatribuída **sem** conclusão do original — nunca as duas |
| Conflito com 409 ou contrato explícito | ✅ | `409 conflito_de_versao` na corrida; `409 entrega_reatribuida` para o motorista que perdeu a entrega; `CenarioObrigatorioEmCadaOrdemTemContratoExplicito` prova as duas ordens sem depender da sorte |

### Critérios de aceite

> Não deve existir endpoint genérico `PATCH status`.

✅ `NaoExisteEndpointGenericoDeStatus` lê o roteamento real: nenhum `PATCH`, nenhuma rota com
"status" no caminho, e a lista de comandos de escrita sobre entregas e rotas é exatamente a esperada
(18 operações, cada uma com nome). `status` no corpo continua recusado com 400.

> Toda mudança de status deve passar por caso de uso explícito.

✅ `ComandosDeStatusTestes` (arquitetura): `Status` sem setter público em entrega e rota, nenhum
método com "Status" no nome, e os métodos da entrega que produzem evento são exatamente os
`ComandoDaEntrega` mais criar e alterar dados — um método novo que mude status fora da máquina faz
o teste falhar.

### Testes pedidos pelo roadmap

| Pedido | Onde |
|---|---|
| todas transições permitidas | `MaquinaDeEstadosDaEntregaTestes` (tabela comando × status, 90 casos, e todo status alcançável); `MotoristaExecutaARotaDoInicioAoFimComTimelineCoerente` pela API |
| todas transições proibidas importantes | `TransicaoProibidaEhRecusadaComConflitoSemMudarNada` (9 casos, sem mudar status nem timeline); `TransicoesProibidasImportantesRespondem409SemMudarNada` (12 recusas pela API); nada sai de Entregue ou Cancelada |
| concorrência | cenário obrigatório concorrente e determinístico; toque duplo na conclusão com um único evento |
| retry frontend | `RepeticaoDeComandoPeloAplicativoNaoDuplicaEfeito` — sair, chegar, concluir e concluir rota repetidos respondem 200 sem novo evento |
| timeline coerente | `AssertTimelineCoerenteAsync` em todos os fluxos: sequência sem lacuna, último status resultante igual ao status atual, contador da entrega igual ao número de eventos |

"Retry frontend" foi interpretado como o reenvio de comando pelo aplicativo quando a resposta se
perde — o comportamento que o cliente depende. Ainda não há tela de execução; ela é da Fase 11.

### Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 430 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 307 | ✅ |
| Frontend — `operacao` | 24 | ✅ |
| Frontend — `motorista` | 12 | ✅ |
| Frontend — `rastreamento` | 13 | ✅ |
| **Total** | **808** | **✅** |

Solução compila com 0 aviso e 0 erro; formatação verificada. Frontend sem alteração nesta fase.

### Security Gate 5

| Item | Resultado | Evidência |
|---|:---:|---|
| Canais separados | ✅ | token do console em rota do motorista: 401; token do motorista em reatribuição: 401 |
| IDOR do motorista | ✅ | rota e entrega de outro motorista da mesma organização e de outra organização: 404 idêntico ao inexistente |
| Contrato de conflito sem vazamento | ✅ | `409 entrega_reatribuida` só para entrega que já foi daquele motorista, conferido na timeline |
| Conta sem cadastro | ✅ | motorista sem associação: 404 `motorista_nao_associado`, nada executado |
| Status sem atalho | ✅ | sem `PATCH`, sem rota de status, `status` no corpo recusado |
| Isolamento no reagendamento | ✅ | entrega de outra organização: 404 idêntico ao inexistente |
| Consulta crua com tenant | ✅ | a consulta de "já foi atribuído" usa SQL parametrizado com organização explícita na condição |
| Segredos | ✅ | gitleaks v8.30.1: 0 achados em 1,55 MB |
| Dependências | ✅ | nenhum pacote novo; `dotnet list package --vulnerable --include-transitive` e `--deprecated`: nada nos 9 projetos; `npm audit`: 0 |

### Defeitos encontrados e corrigidos durante a fase

Nenhum defeito de comportamento chegou à execução dos testes. Durante a escrita: um enum removido
por engano ao reescrever as regras, uma colisão de namespace (`Api.Motorista` escondendo a classe
`Motorista`) e dois defeitos no próprio teste de concorrência (asserção de contagem sem sentido e
`Assert.All` com lambda assíncrona não aguardada) — todos pegos pela compilação ou por revisão antes
da primeira execução.

### Decisões

[ADR 0014](./docs/adr/0014-maquina-de-estados-e-concorrencia.md); matriz atualizada em
[`docs/seguranca/matriz-de-autorizacao.md`](./docs/seguranca/matriz-de-autorizacao.md).

- **Máquina de estados em tabela**, sem biblioteca; regras derivadas leem a mesma tabela.
- **Concluir não exige chegada registrada** — a entrega aconteceu mesmo se o registro falhou.
- **Reatribuição aceita com rota em andamento**; demais mudanças estruturais continuam bloqueadas.
- **Idempotência pela máquina de estados** nos comandos do motorista; deduplicação por identificador
  de operação fica para as fases offline.
- **Rota concluída inativa as paradas**, liberando a entrega reagendada para outra rota.
- **Entrega guarda o motorista responsável**, que permanece após o resultado.

### Pendências conhecidas

| Item | Situação |
|---|---|
| CI nunca executada | exige `git push`, não autorizado |
| Tela de execução do motorista | Fase 11; o contrato de API existe e é testado |
| Leitura "rota do dia" pelo motorista | nesta fase o motorista recebe o identificador da rota; a consulta própria nasce com a PWA |
| Prova de entrega na conclusão | Fase 14 — vira pré-condição de `Concluir` |
| Chegada por geofence | Fase 7 — novo chamador de `RegistrarChegada` |
| Log de erro do EF Core em conflito tratado | agora também nas corridas de execução — Fase 21 |
| Node 22.17.0 na máquina | Vite 8 pede ≥ 22.22.0; tudo passa, o aviso continua real |

### Commit

`feat: maquina de estados da entrega e concorrencia na execucao (Fase 5)`

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

## Fase 6 concluída — 2026-09-14

### Contrato e persistência

| Pedido | Situação | Onde |
|---|:---:|---|
| `LocationEventId` | ✅ | `eventoDeLocalizacaoId`, gerado pelo aplicativo |
| Motorista | ✅ | da sessão — nunca do corpo (`motoristaId` no item responde 400) |
| Latitude, longitude | ✅ | `geography(Point,4326)` |
| Precisão | ✅ | `precisaoEmMetros` |
| `CapturedAt` | ✅ | `capturadaEm`, relógio do aparelho |
| `Sequence` | ✅ | `sequencia` |
| Velocidade e direção | ✅ | opcionais, com faixa validada |
| `ReceivedAt` pelo servidor | ✅ | `recebidaEm`, separado da captura |
| `posicoes` histórico append-only | ✅ | `UPDATE` recusado por trigger; `DELETE` reservado à retenção |
| `posicoes_atuais` estado atual | ✅ | uma linha por motorista, índice GiST |
| Batch para recuperação offline | ✅ | `POST /api/motorista/posicoes` com 1 a 500 posições e resultado por item |

### Invariantes, um por um

| Invariante | Resultado | Evidência |
|---|:---:|---|
| Duplicata não duplica histórico | ✅ | índice único organização + motorista + evento, `ON CONFLICT DO NOTHING`; `DuplicataNaoDuplicaHistorico` (reenvio isolado, repetição no lote e reenvio do lote inteiro) |
| Evento antigo entra no histórico, mas não regressa posição atual | ✅ | `UPSERT` condicional `(capturada_em, sequencia) <` no banco; `EventoForaDeOrdemEntraNoHistoricoMasNaoRegressaAPosicaoAtual`, `PosicaoAntigaNaoRegridePosicaoAtual...`, seis lotes simultâneos |
| Coordenadas inválidas são rejeitadas | ✅ | fora da faixa e `(0,0)` com motivo `coordenada_invalida` |
| Precisão fora de limite não atualiza estado confiável | ✅ | até 100 m move a posição atual; até 2 km só histórico (`Imprecisa`); acima, recusada |
| Motorista só envia para seu próprio contexto | ✅ | motorista resolvido pela sessão, só dentro da execução da própria rota; `MotoristaSoEnviaParaOProprioContexto` |

### Critério de aceite

> O sistema deve receber telemetria realista sem ainda depender de mapa.

✅ `RecebeTelemetriaRealistaComRecuperacaoOfflineEMetricas`, contra PostgreSQL + PostGIS real:
motorista em rota envia uma posição a cada 15 segundos, perde sinal por 10 minutos e, ao
reconectar, manda um lote de 45 posições embaralhadas com cinco reenvios e quatro leituras
imprecisas. Resultado: 40 aceitas, 5 duplicadas, 0 recusadas; histórico com as 52 posições em ordem
de captura; posição atual na última leitura, associada à rota; métricas de recebidas, duplicadas,
imprecisas, fora de ordem e atraso de ingestão batendo com a resposta; nenhuma coordenada no log.

### Testes pedidos pelo roadmap

| Pedido | Onde |
|---|---|
| 101, 103, 102 → histórico 101, 102, 103 e atual 103 | `EventoForaDeOrdemEntraNoHistoricoMasNaoRegressaAPosicaoAtual` (envios separados) e `MesmoCenarioNumLoteUnicoTemOMesmoResultado` (um lote) |
| duplicata | `DuplicataNaoDuplicaHistorico` |
| batch parcial | `LoteParcialGravaOsValidosERelataCadaRecusa` — 12 itens, 2 gravados, 10 recusas com motivo na ordem |
| timestamps futuros | `TimestampFuturoSoEhAceitoDentroDaToleranciaDoRelogio`; bordas no teste de unidade |
| posição antiga | `PosicaoAntigaNaoRegridePosicaoAtualEACapturaDecideMesmoComSequenciaReiniciada` |
| tenant/motorista indevido | `MotoristaSoEnviaParaOProprioContexto` — corpo, rota não iniciada, outro motorista, conta sem cadastro, canais, outra organização |

Além do pedido: seis lotes simultâneos do mesmo motorista com sobreposição (100 aceitas, 20
duplicadas, atual na sequência 100), limites do histórico, tamanho de lote, histórico recusando
`UPDATE` e limite de envio por motorista.

### Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 452 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 330 | ✅ |
| Frontend — `operacao` | 24 | ✅ |
| Frontend — `motorista` | 12 | ✅ |
| Frontend — `rastreamento` | 13 | ✅ |
| **Total** | **853** | **✅** |

Solução compila com 0 aviso e 0 erro; formatação verificada. Frontend sem alteração nesta fase.

### Security Gate 6

| Item | Resultado | Evidência |
|---|:---:|---|
| Contexto do motorista | ✅ | motorista e organização só da sessão; `motoristaId` no corpo: 400 |
| Coleta mínima de localização | ✅ | posição fora da execução de rota do próprio motorista é recusada no servidor |
| IDOR na leitura | ✅ | posição de motorista de outra organização: 404 idêntico ao inexistente |
| Autorização | ✅ | envio só pelo canal do motorista; posição atual A/S/O; histórico só A/S e por até 24 h |
| Rate limiting de telemetria | ✅ | limite por motorista (60/min); não bloqueia outro motorista no mesmo endereço (CGNAT) |
| Logs | ✅ | uma linha por lote, sem coordenada; teste procura coordenada no log |
| Integridade do histórico | ✅ | `UPDATE` recusado pelo banco |
| Métricas sem dado pessoal | ✅ | nenhuma dimensão por motorista ou organização |
| Segredos | ✅ | gitleaks v8.30.1: 0 achados em 1,75 MB |
| Dependências | ✅ | nenhum pacote novo; `dotnet list package --vulnerable --include-transitive` e `--deprecated`: nada nos 9 projetos; `npm audit`: 0 |

### Defeitos encontrados e corrigidos durante a fase

Nenhum defeito chegou à execução dos testes. Dois problemas de desenho foram pegos antes de
escrever a gravação:

1. **O limitador de requisições rodava antes da autenticação**, então não conseguia limitar por
   motorista — e limite por endereço bloquearia motoristas atrás do mesmo IP de operadora. Passou a
   rodar depois da autorização; os limites de login e renovação seguem por endereço e antes do endpoint
   (os testes da Fase 1 continuam verdes).
2. **Impasse entre lotes simultâneos do mesmo motorista** com posições em comum. Um *advisory lock*
   por motorista no início da transação enfileira os lotes daquele aparelho.

### Decisões

[ADR 0015](./docs/adr/0015-ingestao-de-localizacao.md); matriz atualizada em
[`docs/seguranca/matriz-de-autorizacao.md`](./docs/seguranca/matriz-de-autorizacao.md).

- **Gravação atômica em um comando**: histórico com `ON CONFLICT DO NOTHING` e posição atual com
  `UPSERT` condicional — a regra absoluta fica no banco, sem janela entre ler e gravar.
- **Ordem por captura, sequência desempata**, porque a sequência recomeça quando o aplicativo é reinstalado.
- **Política de aceitação no domínio**, com motivo para cada recusa e nada descartado em silêncio.
- **Coleta só durante a execução de rota**, conferida no servidor.
- **Ingestão síncrona**, sem fila: a escala de referência não pede, e o CLAUDE.md desaconselha
  cascata de mensagens para GPS comum.
- **Histórico permite `DELETE`** para a retenção futura; particionamento deixado possível.

### Pendências conhecidas

| Item | Situação |
|---|---|
| CI nunca executada | exige `git push`, não autorizado |
| Detecção de salto impossível entre posições | cálculo geodésico no PostGIS — Fase 7 |
| Retenção e limpeza do histórico | Fase 20; `DELETE` já permitido |
| Particionamento temporal | não ativo; caminho descrito no ADR 0015 |
| Exportação das métricas | OpenTelemetry na Fase 21; os instrumentos já existem |
| Carga de referência (500 motoristas) | medida na Fase 22 |
| Log de erro do EF Core em conflito tratado | continua das fases anteriores — Fase 21 |
| Node 22.17.0 na máquina | Vite 8 pede ≥ 22.22.0; tudo passa, o aviso continua real |

### Commit

`feat: ingestao de localizacao com historico e posicao atual que nao regride (Fase 6)`

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

## Fase 7 concluída — 2026-09-14

### Implementação, item por item

| Pedido | Situação | Onde |
|---|:---:|---|
| Pontos geográficos | ✅ | destino da entrega e posições em `geography(Point,4326)` |
| Distância | ✅ | `ST_Distance` sobre `geography`, em metros no elipsoide WGS 84 |
| Raio | ✅ | `ST_DWithin` a 300 m; margem de saída a 350 m |
| Geofence de destino | ✅ | `estados_de_geofence`, uma linha por entrega em execução com coordenada |
| Detecção `fora → dentro` | ✅ | `EstadoDeGeofence.Avaliar` com os resultados do PostGIS |
| Registro de entrada | ✅ | `eventos_de_geofence` (entrada e saída, com distância, raio e evento de localização), somente-inserção por trigger |
| Proteção contra evento repetido enquanto permanece dentro | ✅ | o estado lembra "dentro"; histerese impede entra-e-sai na borda |
| `EmRota → ProximaDoDestino` quando a regra permitir | ✅ | comando novo `RegistrarProximidade` na máquina de estados, na primeira entrada de entrega em rota |
| GPS antigo não produz transição retroativa | ✅ | só posição que avançou a posição atual é avaliada, e o estado recusa captura mais antiga que a última avaliada |

### Critério de aceite

> Geofencing deve ser comprovado por testes reais, não mockado.

✅ `GeofenceTestes` roda contra PostgreSQL + PostGIS real. Os pontos de teste são calculados pelo
próprio PostGIS com `ST_Project`, a distâncias exatas do destino — sem aproximação em graus — e a
distância e o dentro/fora vêm de `ST_Distance` e `ST_DWithin` sobre `geography`. Nenhuma parte da
geografia é simulada.

### Testes pedidos pelo roadmap

| Pedido | Onde |
|---|---|
| dentro | `ForaDentroEBordaComGeometriaReal` — 299 m entra, entrega vai a `ProximaDoDestino` |
| fora | idem — 2 km e 301 m sem evento, entrega `EmRota` |
| borda | 299 m × 301 m no PostGIS; 350 m × 351 m na margem de saída (unidade) |
| duplicado | `PermanecerDentroEPosicaoDuplicadaNaoRepetemEntrada` — trinta posições dentro e reenvio: uma entrada, uma proximidade |
| out-of-order | `PosicaoForaDeOrdemNaoProduzTransicaoRetroativa` — leitura antiga dentro do raio, em envio separado e no mesmo lote: nenhuma transição |
| geofence de outro tenant | `GeofenceDeOutraOrganizacaoNaoEhAfetadaNemVisivel` — destinos no mesmo ponto em duas organizações: só a do motorista muda; consulta cruzada 404 idêntico ao inexistente |
| reentrada quando aplicável | `SaidaComMargemEReentrada` — entrada, oscilação dentro da margem, saída, reentrada; entrega transiciona uma vez |

Além do pedido: entrega concluída e sem coordenada não avaliadas; chegada manual e entrada simultâneas
com uma única transição; eventos de geofence recusando `UPDATE`, `DELETE` e `TRUNCATE`.

### Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 469 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 340 | ✅ |
| Frontend — `operacao` | 24 | ✅ |
| Frontend — `motorista` | 12 | ✅ |
| Frontend — `rastreamento` | 13 | ✅ |
| **Total** | **880** | **✅** |

Solução compila com 0 aviso e 0 erro; formatação verificada. Frontend sem alteração nesta fase.

### Security Gate 7

| Item | Resultado | Evidência |
|---|:---:|---|
| Isolamento na consulta geográfica | ✅ | SQL cru com organização explícita e só entregas do motorista responsável; destino de outra organização no mesmo ponto não é tocado |
| IDOR na leitura | ✅ | geofence de entrega de outra organização: 404 idêntico ao inexistente |
| Autorização | ✅ | `GET /api/entregas/{id}/geofence` com `operacao:leitura`; mapa de rotas verificado |
| Dado pessoal | ✅ | eventos de geofence com distância e raio, sem coordenada; log de transição sem coordenada |
| Integridade do histórico | ✅ | eventos de geofence somente-inserção no banco |
| Injeção | ✅ | coordenadas, raio e lista de entregas como parâmetros tipados |
| Segredos | ✅ | gitleaks v8.30.1: 0 achados em 1,93 MB |
| Dependências | ✅ | nenhum pacote novo; `dotnet list package --vulnerable --include-transitive` e `--deprecated`: nada nos 9 projetos; `npm audit`: 0 |

### Defeitos encontrados e corrigidos durante a fase

1. **Lote de posições respondia 500 quando o motorista registrava chegada ao mesmo tempo.** Pego
   pelo teste de concorrência `ChegadaManualEEntradaNaGeofenceSimultaneasTransicionamUmaVez`. As duas
   gravações inseriam evento na timeline da entrega com a mesma sequência. Como o evento é inserido
   antes da atualização da linha da entrega, quem perde recebe **violação do índice único da
   sequência**, e não conflito de versão — e a ingestão só refazia o lote no segundo caso. A ingestão
   passou a tratar as duas formas como o mesmo conflito concorrente: refaz o lote com o estado novo
   (até três vezes) e só então responde 409.

### Decisões

[ADR 0016](./docs/adr/0016-geofence-de-destino.md); matriz atualizada em
[`docs/seguranca/matriz-de-autorizacao.md`](./docs/seguranca/matriz-de-autorizacao.md).

- **Avaliação na ingestão**, na mesma transação, para cada posição que avança a posição atual.
- **Distância e raio no PostGIS** (`geography`, `ST_Distance`, `ST_DWithin`), sem cálculo geodésico em C#.
- **Raio 300 m, saída a 350 m** — histerese contra oscilação na borda; raio gravado em estado e evento.
- **Entradas e saídas em tabela própria**, fora da timeline da entrega.
- **Proximidade como comando da máquina de estados**, equivalente à chegada manual e idempotente com ela.
- **Conflito com comando do motorista refaz o lote**, sem duplicar posição nem transição.

### Pendências conhecidas

| Item | Situação |
|---|---|
| CI nunca executada | exige `git push`, não autorizado |
| Raio por organização ou cliente | decisão de produto; o raio já é gravado em cada estado e evento |
| Geofence de hub (saída e retorno ao depósito) | mesmo desenho, quando pedido |
| Custo de uma consulta PostGIS por posição que avança | dezenas de entregas por motorista; medir na Fase 22 |
| Detecção de salto impossível entre posições | não implementada; `ST_Distance` já está disponível para ela |
| Log de erro do EF Core em conflito tratado | continua das fases anteriores — Fase 21 |
| Node 22.17.0 na máquina | Vite 8 pede ≥ 22.22.0; tudo passa, o aviso continua real |

### Commit

`feat: geofence de destino com PostGIS e transicao para proxima do destino (Fase 7)`

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

## Fase 8 concluída — 2026-09-15

### Eventos realtime mínimos

| Evento | Situação | Origem |
|---|:---:|---|
| `DriverPositionUpdated` | ✅ | posição atual do motorista que avançou — um aviso por motorista por lote, o mais recente |
| `DeliveryStatusChanged` | ✅ | todo evento de entrega que muda status, com o evento que causou e a sequência na timeline |
| `DeliveryRiskChanged` | nome reservado | produzido quando existir risco — Fase 9 (ETA e SLA) |
| `AlertCreated` | nome reservado | produzido quando existirem alertas — Fase 10 |
| `IncidentCreated` | nome reservado | produzido quando existirem ocorrências |

Os três últimos não têm produtor nesta fase; o nome está fixado para o console assinar desde já, e o
conteúdo nasce com quem os produz (ADR 0017). Não há contrato sem produtor.

### Arquitetura

| Pedido | Resultado | Evidência |
|---|:---:|---|
| Domínio não conhece SignalR | ✅ | teste de arquitetura existente; o domínio não mudou |
| Abstração de publicação em Application/Infrastructure | ✅ | `IPublicadorDeTempoReal` na `Application`; chamada pelo contexto de persistência, na `Infrastructure`, no único ponto pós-commit; implementação SignalR na `Api` (ASP.NET Core é proibido na `Infrastructure` — divergência do ADR 0005 registrada no ADR 0017) |

Nenhum caso de uso publica diretamente: o contexto recolhe os eventos de entrega e as sessões
revogadas ao gravar e despacha depois do commit — tentativa desfeita não avisa.

### Segurança

| Pedido | Resultado | Evidência |
|---|:---:|---|
| Grupos respeitam tenant | ✅ | o hub coloca a conexão só no grupo da organização **do token**; não há método para o cliente escolher grupo; `AvisoNaoVazaEntreOrganizacoes` |
| Motorista não assina canal administrativo | ✅ | hub com a política do console; token de motorista recebe 401 |
| Rastreamento público não usa canal interno | ✅ | sem sessão do console não há conexão (401) |

Além do pedido: sessão revogada derruba a conexão (logout, reuso, perfil, desativação); conexão fecha
no vencimento do token; token na query string só vale no hub; mensagem do cliente limitada a 4 KB.

### Critério de aceite

> Uma posição válida deve aparecer em cliente operacional conectado sem F5.

✅ `PosicaoValidaApareceNoClienteOperacionalConectadoSemRecarregar`: um operador conectado ao hub
por WebSocket, com cliente SignalR real, recebe `DriverPositionUpdated` com motorista, rota,
latitude, longitude, sequência e captura logo depois de o motorista enviar a posição pela API. Posição
atrasada e posição recusada, que não movem a posição atual, não avisam.

### Testes pedidos pelo roadmap

| Pedido | Onde |
|---|---|
| conexão autenticada | `ConexaoExigeSessaoDoConsoleEOHubNaoAceitaComando` — sem token, token de motorista e adulterado: 401; token na query string conecta no hub e não autentica rota da API; método do hub recusado |
| tenant isolation | `AvisoNaoVazaEntreOrganizacoes` |
| reconexão | `ReconexaoRetomaOCanalDaOrganizacao` — queda de rede do lado do cliente, reconexão automática e aviso recebido de novo |
| evento correto | `MudancaDeStatusChegaComOEventoCertoEOQueNaoMudaStatusNaoAvisa` — saída para rota e proximidade com status e evento corretos; alteração de dados e comando recusado sem aviso |
| ausência de vazamento entre organizações | `AvisoNaoVazaEntreOrganizacoes` — cada console só recebe avisos da própria organização |

Além do pedido: `SessaoEncerradaDerrubaAConexaoEAReconexaoEhRecusada` e ausência do token no log.

### Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 469 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 346 | ✅ |
| Frontend — `operacao` | 24 | ✅ |
| Frontend — `motorista` | 12 | ✅ |
| Frontend — `rastreamento` | 13 | ✅ |
| **Total** | **886** | **✅** |

Solução compila com 0 aviso e 0 erro; formatação verificada. Frontend sem alteração nesta fase.

### Security Gate 8

| Item | Resultado | Evidência |
|---|:---:|---|
| Autenticação do canal | ✅ | política do console; sessão conferida no banco a cada conexão e reconexão |
| Isolamento entre tenants | ✅ | grupo pela organização da sessão; teste com duas organizações conectadas |
| Autoridade separada do motorista | ✅ | token de motorista: 401 no hub |
| Canal público | ✅ | sem acesso anônimo ao hub |
| Sessão revogada | ✅ | conexões derrubadas no commit da revogação; reconexão recusada |
| Token em URL | ✅ | aceito só no caminho do hub, só sem cabeçalho; rota da API com `access_token` responde 401; token ausente do log |
| CORS | ✅ | só os cabeçalhos do cliente SignalR acrescentados à política existente |
| Abuso pelo cliente | ✅ | hub sem métodos; mensagem limitada a 4 KB; erros detalhados desligados |
| Dado pessoal no aviso | ✅ | sem nome, endereço ou telefone; detalhe só pela API |
| Segredos | ✅ | gitleaks v8.30.1: 0 achados |
| Dependências | ✅ | pacote novo `Microsoft.AspNetCore.SignalR.Client` 10.0.11, só no projeto de testes; `dotnet list package --vulnerable --include-transitive` e `--deprecated`: nada; `npm audit`: 0 |

### Defeitos e ajustes durante a fase

1. **Comentário falso em `Program.cs` desde a Fase 6.** Sobre `UseAuthentication` ainda dizia
   "limite antes da autenticação", mas o limitador passou a rodar depois da autorização naquela fase.
   Removido.
2. **Dois erros no próprio teste de reconexão.** A primeira versão simulava queda derrubando a
   conexão pelo servidor sobre long polling — o que o cliente recebe como **encerramento normal** e,
   corretamente, não tenta reconectar. Passou a usar WebSocket (o transporte do navegador) e a
   derrubar o socket do lado do cliente, que é a queda de rede real. Depois, a comparação dos
   identificadores de conexão antes e depois falhou porque, sem negociação, o cliente não recebe
   identificador; o teste passou a provar a reconexão pelo estado e pelo aviso recebido.
3. **Chave do mapa de autorização.** O endpoint `negotiate` do hub não tem restrição de método; o mapa
   declarado foi corrigido para `* /tempo-real/operacao/negotiate`.

### Decisões

[ADR 0017](./docs/adr/0017-tempo-real-da-operacao.md); matriz atualizada em
[`docs/seguranca/matriz-de-autorizacao.md`](./docs/seguranca/matriz-de-autorizacao.md).

- **Aviso nasce no contexto de persistência, só depois do commit**, sem publicação espalhada.
- **Grupo pela sessão; hub sem métodos.**
- **Token na query string só no hub.**
- **Sessão revogada derruba conexão**; token vencido fecha conexão.
- **Implementação na `Api`**, porque a `Infrastructure` não pode depender de ASP.NET Core.
- **Uma instância, sem backplane**, mantendo a decisão adiada do ADR 0005.

### Pendências conhecidas

| Item | Situação |
|---|---|
| CI nunca executada | exige `git push`, não autorizado |
| Mais de uma instância da API | exige backplane ou serviço gerenciado — Fase 25 |
| Fechamento da conexão no vencimento do token | configurado (`CloseOnAuthenticationExpiration`), mas sem teste automatizado: o temporizador usa o relógio do sistema e exigiria esperar 15 minutos |
| Console consumindo os avisos | a tela do mapa é da Fase 18; o contrato está pronto |
| Log de erro do EF Core em conflito tratado | continua das fases anteriores — Fase 21 |
| Node 22.17.0 na máquina | Vite 8 pede ≥ 22.22.0; tudo passa, o aviso continua real |

### Commit

`feat: tempo real da operacao com SignalR e aviso so depois do commit (Fase 8)`

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

## Fase 9 concluída — 2026-09-15

### Entregáveis

| Pedido | Resultado | Evidência |
|---|:---:|---|
| SLA: `PromisedFrom` e `PromisedUntil` | ✅ | já existia desde a Fase 3 como `JanelaDeEntrega` (início e fim em UTC, API `prometidaDe`/`prometidaAte`); a Fase 9 classifica contra ela |
| Fronteira `IRoutingProvider` | ✅ | `IProvedorDeRotas` na `Application`: N pontos → N − 1 trechos; responde só pelo deslocamento |
| Implementação controlada sem custo | ✅ | provedor `simulado`: distância geodésica do PostGIS × sinuosidade ÷ velocidade média, configuráveis; a previsão registra o nome |
| ETA determinístico: duração restante | ✅ | deslocamento acumulado desde a posição atual, pelo provedor |
| ETA: paradas anteriores | ✅ | paradas pendentes antes, em ordem da rota; parada sem coordenada conta o atendimento |
| ETA: tempo médio configurado por parada | ✅ | `TempoMedioPorParada` (5 min) |
| ETA: estado operacional | ✅ | em rota soma deslocamento; próxima do destino usa a chegada registrada e o atendimento que falta; sem posição ou sem coordenada fica sem previsão, com motivo |
| Histórico sem sobrescrever | ✅ | `registros_de_previsao` somente-inserção por trigger, fotografia completa com valores anteriores, janela e limiares |
| Classificação Normal, Atenção, Risco e Atrasada | ✅ | `RegrasDeSla`, com motivo em vocabulário fechado |
| Limiares configuráveis e documentados | ✅ | `Torre:Previsao` validado na subida; tabela em [ADR 0018](./docs/adr/0018-previsao-de-chegada-e-sla.md) |

### Critério de aceite

> A aplicação deve explicar por que uma entrega passou de Normal para Risco.

✅ `ExplicaPorQueAEntregaPassouDeNormalParaRisco`, contra API, processador em segundo plano e
PostgreSQL + PostGIS reais, com relógio controlado:

1. O motorista está a 10 km: a chegada prevista é exata ao segundo (1.000 s) e a situação é Normal.
2. Cem minutos depois, **sem nenhum evento**, a reavaliação periódica percebe a folga de 200 s e passa
   a entrega a Risco.
3. `GET /api/entregas/{id}/previsao` devolve o registro `SituacaoAlterada`, com situação anterior,
   limiares, janela e composição, e a explicação exata:

   > Passou de Normal para Risco porque a folga até o fim da janela prometida caiu para 3 min, abaixo
   > do limiar de risco de 5 min. A chegada prevista soma 17 min de deslocamento (10,0 km, pelo provedor
   > de rotas simulado) e nenhuma parada antes desta. A posição do motorista usada foi capturada 1 h 40
   > min antes do cálculo.

4. O histórico anterior continua byte a byte igual, e no fim da janela a entrega passa a Atrasada.

### Testes pedidos pelo roadmap

| Pedido | Onde |
|---|---|
| ETA | `CalculadoraDeChegadaTestes` (5 cenários); `ChegadaPrevistaSomaDeslocamentoParadasAnterioresETempoPorParada` — duas paradas, chegada registrada, conclusão que adianta a seguinte |
| janela | `RegrasDeSlaTestes` — cada limiar dos dois lados da borda, chegada depois da janela, janela encerrada, sem previsão |
| mudança de risco | critério de aceite; `MudancaDeRiscoChegaAoConsoleEmTempoReal` (`DeliveryRiskChanged`, e nenhum aviso para a primeira previsão Normal); `PrevisaoDaEntregaTestes` |
| histórico | `PrevisaoDaEntregaTestes` (mudança pequena sem registro, relevante com valor anterior, cálculo antigo ignorado, encerramento, fotografia imutável); `HistoricoDePrevisoesEhSomenteInsercao` (`UPDATE`, `DELETE`, `TRUNCATE`) |
| ausência de provider | `SemProvedorAPrevisaoUsaAContingenciaEDizPorque` |
| timeout/falha do provider | `FalhaDoProvedorCaiNaContingenciaEmLinhaReta`; `ProvedorQueNaoRespondeNaoSeguraAIngestaoECaiNaContingencia` — com o provedor travado, a ingestão de GPS responde, e a previsão sai no tempo limite |

Além do pedido: `PrevisaoDeOutraOrganizacaoNaoEhVisivel`, `ExplicacaoDaPrevisaoTestes`,
`OpcoesDePrevisaoTestes` e a rota nova na matriz de autorização.

### Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 504 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 361 | ✅ |
| Frontend — `operacao` | 24 | ✅ |
| Frontend — `motorista` | 12 | ✅ |
| Frontend — `rastreamento` | 13 | ✅ |
| **Total** | **936** | **✅** |

A solução compila com 0 aviso e 0 erro, e a formatação foi verificada. O frontend não mudou nesta fase.

### Security Gate 9

| Item | Resultado | Evidência |
|---|:---:|---|
| Autorização da consulta | ✅ | `operacao:leitura`; motorista e anônimo recebem 401; não há rota de escrita de previsão |
| Isolamento entre tenants | ✅ | filtro global nas duas tabelas novas; recálculo em segundo plano com contexto criado no tenant do pedido; entrega de outra organização responde 404 igual a inexistente |
| Leitura sem tenant | ✅ | só na reavaliação periódica, só identificadores, com `IgnoreQueryFilters()` explícito |
| Dependência externa | ✅ | tempo limite imposto por quem chama (`CancelAfter` + `WaitAsync`), contingência registrada e medida; provedor lento não segura requisição |
| Integridade do histórico | ✅ | trigger somente-inserção; índice único `(entrega_id, sequencia)`; versão da linha na previsão atual |
| Dado pessoal | ✅ | previsão, explicação e aviso sem coordenada, endereço ou nome; só durações, distâncias e regras |
| Configuração | ✅ | limiares e parâmetros validados na subida; configuração incoerente derruba o processo |
| Segredos | ✅ | gitleaks v8.30.1: 0 achados; provedor simulado não usa chave |
| Dependências | ✅ | pacote novo `Microsoft.Extensions.Hosting.Abstractions` 10.0.11 na `Infrastructure`, só as abstrações do `BackgroundService`; `dotnet list package --vulnerable --include-transitive` e `--deprecated`: nada; `npm audit`: 0 |

### Observabilidade

No medidor `TorreLogistica.Previsao`, sem organização nem entrega como dimensão:

- `eta.calculation.duration`;
- `eta.provider.fallbacks`, por motivo;
- `sla.situation.changes`, pela situação nova.

Nos logs: um aviso por falha ou tempo limite do provedor, um informativo por recálculo que grava
histórico e um erro por falha inesperada do processamento.

### Defeitos e ajustes durante a fase

1. **Falso erro no encerramento do processo.** A primeira execução completa da integração passou
   inteira, mas registrou um `Falha ao recalcular a previsão` com `EndOfStreamException`. A API de um
   teste estava sendo encerrada: o cancelamento fecha o socket no meio da leitura, e o Npgsql lança
   `NpgsqlException`, não `OperationCanceledException`. O filtro tratava isso como falha real. Agora,
   com o processo parando, qualquer exceção encerra o laço sem log de erro, porque nada foi confirmado e
   a reavaliação recalcula na próxima subida. Na nova execução completa não houve nenhum log desse tipo.
2. **Conexão aberta fora de transação.** O cálculo de distâncias do trajeto nasceu reaproveitando a
   conexão da transação da ingestão. Mas o provedor simulado e a contingência rodam fora de transação,
   então o método passou a abrir e devolver a própria conexão.
3. **Varredura da suíte inteira.** O banco de teste é compartilhado, e a reavaliação periódica de
   qualquer API de teste percorreria as rotas em andamento de todos os testes. A fábrica de teste
   configura 30 min por padrão, e os testes de previsão configuram 1 min com relógio controlado. Por
   isso a espera pela reavaliação no teste é de até 60 s: ela passa pelas rotas da suíte antes de chegar
   à do teste.

### Decisões

[ADR 0018](./docs/adr/0018-previsao-de-chegada-e-sla.md); matriz atualizada em
[`docs/seguranca/matriz-de-autorizacao.md`](./docs/seguranca/matriz-de-autorizacao.md).

- **Rota inteira, fora da requisição**: gatilho pós-commit, fila em memória com um consumidor, e
  reavaliação periódica para o tempo que passa sem evento e para o pedido perdido.
- **Contingência em linha reta pelo PostGIS**, com motivo gravado, em vez de ficar sem previsão.
- **Histórico só com mudança relevante**: inicial, situação, chegada ≥ 2 min, encerramento. Cada
  registro é fotografia completa.
- **Explicação sem horário de relógio**: fuso é assunto de apresentação.
- **Sem outbox**: a previsão se recalcula do estado confirmado.
- **Processador hospedado pela API**, porque o aviso de tempo real sai do processo do hub (ADR 0017).

### Pendências conhecidas

| Item | Situação |
|---|---|
| Provedor de rotas real | contrato pronto; escolher fornecedor tem custo e exige autorização |
| Padrões de velocidade, sinuosidade e limiares | ponto de partida urbano; recalibrar com dados reais — Fase 19 |
| Limiar por organização ou cliente, chegada antes da janela | decisão de produto ainda inexistente |
| Mais de uma instância da API | recálculo repetido entre instâncias (resultado correto, trabalho dobrado) e aviso sem backplane — Fase 25 |
| Reavaliação com mais de 5.000 rotas por ciclo | limite por ciclo; medir na Fase 22 |
| Gauges `deliveries_at_risk` e `deliveries_late` | exigem consulta agregada; entram com a exportação de métricas — Fase 21 |
| Console exibindo previsão e risco | contrato e aviso prontos; tela na Fase 18 |
| CI nunca executada | exige `git push`, não autorizado |
| Node 22.17.0 na máquina | Vite 8 pede ≥ 22.22.0; tudo passa, o aviso continua real |

### Commit

`feat: previsao de chegada e SLA explicaveis, com provedor de rotas e contingencia (Fase 9)`

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
| Última fase concluída | **Fase 9 — ETA e SLA** (2026-09-15) |
| Próxima fase | **Fase 10 — Motor de Alertas Operacionais** |
| Testes verdes | 936 — 504 unidade, 22 arquitetura, 361 integração, 49 frontend |

Comando para continuar:

> **siga para a próxima fase**
