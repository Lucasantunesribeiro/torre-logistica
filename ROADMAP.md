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
| 12 | Offline, Sincronização e Idempotência | ✅ |
| 13 | Ocorrências e Tentativas de Entrega | ✅ |
| 14 | Proof of Delivery | ✅ |
| 15 | Rastreamento Público | ✅ |
| 16 | API de Integração e Importação | ✅ |
| 17 | Webhooks e Backbone Assíncrono | ✅ |
| 18 | Console Operacional e Mapa | ✅ |
| 19 | Indicadores e Analytics | ✅ |
| 20 | Segurança e Privacidade | ✅ |
| 21 | Observabilidade | ✅ |
| 22 | Performance e Resiliência | ✅ |
| 23 | Simulador e Seed Narrativo | ✅ |
| 24 | UX Final e Modo Demonstração | ✅ |
| 25 | Infraestrutura e Deploy | ✅ |
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
| Padrões de velocidade, sinuosidade e limiares | ponto de partida urbano; **continua aberta depois da Fase 19** — recalibrar exige série de operação real, que o provedor simulado não produz |
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

## Fase 10 concluída — 2026-09-15

### Tipos iniciais

| Tipo | Situação | Regra | Severidade |
|---|:---:|---|:---:|
| `RiscoDeAtraso` | ✅ | previsão ativa em Risco (Fase 9) | Média |
| `EntregaAtrasada` | ✅ | previsão ativa Atrasada | Alta |
| `MotoristaOffline` | ✅ | rota em andamento, com pendência, sem posição há ≥ 10 min (desde a saída, se nunca enviou) | Alta |
| `ParadoTempoExcessivo` | ✅ | permanência num raio de 100 m ≥ 15 min, calculada no PostGIS sobre o histórico; ≥ 30 min atendendo parada; suprimido com motorista offline | Média |
| `TentativasExcedidas` | ✅ | tentativas sem sucesso ≥ 2 e entrega nem entregue nem cancelada | Alta |
| `OcorrenciaCritica` | tipo definido | tipo, severidade (Crítica), chave, ciclo de vida, API e aviso prontos; **a ocorrência que o produz nasce na Fase 13**, que tem o teste "ocorrência crítica" | Crítica |
| `DesvioRelevante` | não entra | exige traçado planejado, que o provedor simulado não produz; sem ele a regra alertaria atalho legítimo — não é confiável nem demonstrável, a condição do ROADMAP | — |

### Alerta

| Pedido | Resultado | Evidência |
|---|:---:|---|
| tipo, severidade, entrega, motorista | ✅ | `alertas_operacionais`, com rota; alvo coerente com o tipo por check constraint |
| evidência | ✅ | JSON com números, instantes e limites (evidência de abertura e última evidência); sem coordenada |
| aberto em, resolvido em, estado | ✅ | mais forma de resolução, quem resolveu, observação, reaberturas e última constatação |
| deduplicação | ✅ | chave tipo + alvo; índice único parcial `(organizacao_id, chave) WHERE estado = 'Aberto'` |
| ciclo de vida | ✅ | resolve sozinho; reabre em até 30 min; depois, alerta novo; resolução do operador não é desfeita enquanto a condição persiste; `eventos_de_alerta` somente-inserção por trigger |

### Critério de aceite

> Operador consegue identificar uma entrega problemática sem analisar manualmente todos os GPS.

✅ `OperadorIdentificaEntregaProblematicaSemAnalisarOsGps` — numa rota com duas entregas e o motorista a
20 km, o operador chama `GET /api/alertas?estado=Aberto` e recebe **um** alerta: `RiscoDeAtraso`, da
entrega problemática, com o código dela, folga de −200 s na evidência, nenhuma coordenada e a descrição
exata:

> Entrega em risco de atraso. Risco porque a chegada prevista fica 3 min depois do fim da janela
> prometida. A chegada prevista soma 33 min de deslocamento (20,0 km, pelo provedor de rotas simulado) e
> nenhuma parada antes desta.

A outra entrega não tem alerta, e o console conectado recebe `AlertCreated`.

### Testes pedidos pelo roadmap

| Pedido | Onde |
|---|---|
| cria | critério de aceite; `MotoristaParadoAbreAlertaPelaPermanenciaCalculadaNoPostgis`; `TentativasExcedidasAbreEResolveQuandoAEntregaEhCancelada` |
| não duplica | `MesmaCondicaoNaoDuplicaAlertaACadaAvaliacao` — três avaliações, um alerta e um evento; `CicloDeVidaDoAlertaTestes` — 47 constatações sem evento novo (R11) |
| resolve | automática quando a posição volta, quando o motorista anda e quando a entrega é cancelada; pelo operador em `OperadorResolveEAlertaNaoReabreEnquantoACondicaoPersiste` |
| reabre quando regra definir | `MotoristaOfflineAbreResolveReabreEViraAlertaNovoDepoisDaJanela` — Aberto, Resolvido, Reaberto, Resolvido; depois da janela, alerta novo |
| tenant isolation | `AlertaDeOutraOrganizacaoNaoApareceNemPodeSerResolvido` — ausente da lista, 404 idêntico a inexistente, resolução recusada |

Além do pedido: `CicloDeVidaDosAlertasEhSomenteInsercao`, `RegrasDeAlertaTestes` (bordas de cada regra),
`DescricaoDoAlertaTestes` e as três rotas na matriz de autorização.

### Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 527 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 381 | ✅ |
| Frontend — `operacao` | 24 | ✅ |
| Frontend — `motorista` | 12 | ✅ |
| Frontend — `rastreamento` | 13 | ✅ |
| **Total** | **979** | **✅** |

Solução compila com 0 aviso e 0 erro; formatação verificada. Frontend sem alteração nesta fase.

### Security Gate 10

| Item | Resultado | Evidência |
|---|:---:|---|
| Autorização | ✅ | leitura `operacao:leitura`; resolução `entregas:operacao`; motorista e anônimo 401; sem criação ou alteração de alerta pela API |
| Isolamento entre tenants | ✅ | filtro global nas duas tabelas; avaliação no tenant da rota; permanência no PostGIS com organização explícita; teste com duas organizações |
| Leitura sem tenant | ✅ | só identificadores de rota na reavaliação periódica, com `IgnoreQueryFilters()` explícito |
| Dado pessoal | ✅ | evidência, descrição e aviso sem coordenada, endereço ou dado do destinatário; nome do motorista só na API autorizada |
| Integridade | ✅ | ciclo de vida somente-inserção por trigger; um aberto por chave no banco; versão da linha; resolução concorrente responde 409 |
| Entrada | ✅ | observação até 280 caracteres, normalizada; filtros por nome exato de enumeração; paginação limitada |
| Configuração | ✅ | limites validados na subida |
| Segredos | ✅ | gitleaks v8.30.1: 0 achados |
| Dependências | ✅ | nenhum pacote novo; `dotnet list package --vulnerable --include-transitive` e `--deprecated`: nada |

### Observabilidade

Medidor `TorreLogistica.Alertas`: `alerts.opened` e `alerts.reopened` por tipo, `alerts.resolved` por tipo e
forma. Log informativo em abertura e reabertura, sem evidência.

### Defeitos e ajustes durante a fase

1. **Logs perdidos entre APIs de teste em sequência (defeito anterior, exposto agora).** A primeira execução
   completa falhou em `EntregasTestes.TimelineAuditoriaELogNaoGuardamDadoPessoal`, que passa isolado: o
   log da criação da entrega não estava no sink. Causa confirmada na documentação do
   `Serilog.Extensions.Hosting`: sem `preserveStaticLogger`, `UseSerilog` troca o `Log.Logger` global e os
   `ILogger` do host escrevem nele; o `Log.CloseAndFlushAsync()` do `finally` de uma API que termina fechava
   o logger global que a API seguinte já usava. O desligamento ficou mais longo com o processador em segundo
   plano, e a corrida passou a acontecer. Corrigido: cada host usa o próprio logger
   (`preserveStaticLogger: true`), e o log de subida sai por `aplicacao.Logger`. Em produção havia um só
   host, então o defeito não aparecia.
2. **Dois testes com token vencido.** Os testes de offline e de parado avançam o relógio além dos 15 minutos
   do token de acesso; o envio de posição recebeu 401. O defeito era do teste: o auxiliar agora entra de novo
   como motorista a cada envio.
3. **Ordem de importações** em `ProcessamentoDePrevisoes.cs`, apontada pela verificação de formatação.

### Decisões

[ADR 0019](./docs/adr/0019-motor-de-alertas-operacionais.md); matriz atualizada em
[`docs/seguranca/matriz-de-autorizacao.md`](./docs/seguranca/matriz-de-autorizacao.md).

- **Regras puras no domínio**, uma constatação por tipo e alvo, valendo ou não.
- **Uma chave por problema** e índice único parcial no banco.
- **Janela de reabertura** de 30 min: oscilação reabre; episódio novo é alerta novo.
- **Decisão do operador não é desfeita** enquanto a condição persiste.
- **Motor encadeado à previsão**, no mesmo processador, com a reavaliação periódica percebendo a ausência de
  evento (offline) e a saída da entrega da operação.
- **`AlertResolved` acrescentado** aos nomes do ADR 0017, para o console tirar o alerta da tela.

### Pendências conhecidas

| Item | Situação |
|---|---|
| Produtor de `OcorrenciaCritica` | Fase 13 |
| `DesvioRelevante` | depende de traçado planejado por provedor de rotas real |
| Notificação fora do console (e-mail, push) | fora desta fase |
| Limites por organização | decisão de produto inexistente; limites gravados na evidência |
| Mais de uma instância | avaliação repetida entre instâncias (resultado correto) e aviso sem backplane — Fase 25 |
| `Workers` com `AddSerilog` sem `preserveStaticLogger` | processo único, sem o problema; revisar quando hospedar jobs |
| Console exibindo alertas | Fase 18 |
| CI nunca executada | exige `git push`, não autorizado |

### Commit

`feat: motor de alertas operacionais com deduplicacao e ciclo de vida (Fase 10)`

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

## Fase 11 concluída — 2026-09-15

### Telas

| Tela pedida | Resultado | Onde |
|---|:---:|---|
| login | ✅ | `/entrar`, canal do motorista, token só em memória, renovação por cookie |
| rota do dia | ✅ | `/`: rota em andamento primeiro; veículo, saída, pendentes; próximas rotas |
| lista de paradas | ✅ | `/rotas/{id}/paradas`, na ordem da rota |
| próxima entrega | ✅ | cartão em destaque na rota do dia, com "Abrir entrega" |
| detalhe | ✅ | `/entregas/{id}`: endereço, janela, instruções, observações, abrir no mapa, ligar |
| iniciar rota | ✅ | botão na rota do dia |
| chegada | ✅ | "Cheguei ao destino" |
| tentativa frustrada | ✅ | `/entregas/{id}/ocorrencia`, motivo tipado obrigatório |
| concluir | ✅ | "Entrega concluída" com confirmação; "Encerrar rota" quando tudo tem resultado |
| ocorrências | ✅ | registro com motivo tipado e histórico no detalhe; hoje a ocorrência do domínio é a tentativa sem sucesso — tipos além dela (veículo, mercadoria, severidade) são da Fase 13 |
| estado de conexão | ✅ | cabeçalho: sem internet no aparelho, conectada, acordando, inalcançável; localização sempre visível na rota |

### Backend necessário para a PWA

A API do motorista só tinha comandos. Entraram três leituras próprias, com modelo sem dado do console:
`GET /api/motorista/rotas`, `GET /api/motorista/rotas/{id}` e `GET /api/motorista/entregas/{id}`.
Só o que é do motorista da sessão; o resto responde 404, e a entrega reatribuída, `409 entrega_reatribuida`.

### UX

- Uma coluna, ações com a largura inteira e alvo de toque de 3,5rem; fonte de 16px; contraste alto.
- Nenhum painel: a rota do dia mostra a próxima ação.
- Concluir pede confirmação; ocorrência exige motivo.
- Toda falha vira frase curta, dizendo o que fazer; comando recusado atualiza a tela.

### GPS

`watchPosition` com alta precisão só com rota em andamento e o aplicativo aberto. Lote a cada 15 s, com
UUIDv7 e sequência do aparelho; falha de envio mantém até 200 posições em memória e reenvia com os mesmos
identificadores.

**Limitações reais, documentadas no [ADR 0020](./docs/adr/0020-pwa-do-motorista.md) e ditas ao motorista:**

- não existe API web de localização em segundo plano: com a tela bloqueada ou o app em segundo plano, o
  navegador suspende as leituras;
- permissão negada só o motorista libera;
- Background Sync não é garantido;
- fechar o navegador perde o que está em memória, e a fila persistente é da Fase 12.

O aplicativo avisa "mantenha o aplicativo aberto", e o alerta de motorista offline (Fase 10) cobre a
ausência.

### Critério de aceite

> Motorista consegue completar fluxo básico usando somente a PWA.

✅ Nas duas pontas:

- **PWA** — `motorista completa o fluxo básico só pela PWA`: com servidor que aplica os comandos como a
  máquina de estados, o motorista inicia a rota, abre a próxima entrega, registra chegada, conclui com
  confirmação, abre a seguinte, registra ocorrência com motivo "Local fechado" e encerra a rota, até o
  estado "Nenhuma rota para você agora". Os comandos saem exatamente nesta ordem: início, chegada,
  conclusão, tentativa, encerramento.
- **API real** — `FluxoBasicoDoMotoristaSoComAsRotasDaPwa`: o mesmo fluxo usando só as rotas que a PWA
  chama, contra PostgreSQL real, até a rota sair da lista do dia.

### Testes pedidos pelo roadmap

| Pedido | Onde |
|---|---|
| navegação | lista de paradas → detalhe, links de mapa e telefone; tela inexistente; critério de aceite |
| autorização | sessão encerrada no meio da rota volta ao login; login só pelo canal do motorista; `MotoristaSoLeOQueEhDele` (outro motorista e outra organização 404, token do console 401); matriz de autorização |
| estados | sem rota; rota planejada → em andamento → tudo resolvido → encerrada; localização aguardando, ativa, bloqueada, indisponível e sem sinal |
| permission denied de geolocation | `localização negada avisa e não impede concluir a entrega`; `rastreador.test.ts` |
| erros | reatribuída explicada (PWA e API); comando recusado atualiza a tela; falha 500 com "Tentar de novo"; sem internet no aparelho |
| responsividade | `estilos.test.ts`: viewport, ações com largura inteira e alvo de toque, sem largura fixa em pixel nem conteúdo escondido, manifesto |

Além do pedido: localização só pedida com rota em andamento, reenvio de posições com os mesmos
identificadores, limite de pendentes, UUIDv7 do aparelho, renovação única de sessão.

### Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 527 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 394 | ✅ |
| Frontend — `operacao` | 24 | ✅ |
| Frontend — `motorista` | 41 | ✅ |
| Frontend — `rastreamento` | 13 | ✅ |
| **Total** | **1.021** | **✅** |

`npm run verificar` (lint, tipos, testes e build das três aplicações) sem erro; solução .NET com 0 aviso e
0 erro; formatação verificada.

### Security Gate 11

| Item | Resultado | Evidência |
|---|:---:|---|
| Autoridade separada | ✅ | leituras do motorista só com a política do motorista; token do console 401 |
| Isolamento | ✅ | motorista resolvido pela sessão; rota ou entrega de outro motorista ou organização 404 |
| Minimização | ✅ | modelo próprio sem cliente, versão ou auditoria; contato e instruções só no detalhe da própria entrega |
| Token no navegador | ✅ | só em memória; `localStorage` e `sessionStorage` vazios (teste); cookie HttpOnly no caminho do canal |
| Aparelho compartilhado | ✅ | sair limpa o cache de consultas |
| Localização | ✅ | pedida e coletada só com rota em andamento e app aberto; destino ao mapa externo só no toque |
| Entrada | ✅ | respostas validadas com Zod antes de chegar à tela; ocorrência só com motivo da lista |
| Segredos | ✅ | gitleaks v8.30.1: 0 achados; nenhum segredo no pacote da PWA |
| Dependências | ✅ | nenhum pacote novo; `npm audit`: 0 |

### Defeitos e ajustes durante a fase

1. **Teste com tipo de veículo inválido.** O cenário da leitura do motorista usava tipo `Van`, que não existe;
   a API respondeu 400, corretamente. Corrigido para `Utilitario`.
2. **Alerta errado encontrado pelo teste.** Dois testes procuravam "o" alerta da tela, mas com rota em
   andamento no jsdom (sem geolocalização) o aviso "este aparelho não informa localização" também é um
   alerta. Os testes passaram a procurar a mensagem esperada.
3. **CSS vazio no teste de responsividade.** O Vitest devolve CSS vazio mesmo com `?raw`; a folha de estilos
   passou a ser processada no teste (`css.include`), sem dependência nova.
4. **Consulta repetia erro 4xx.** O cliente da PWA repetia qualquer falha; entrega reatribuída ou inexistente
   não muda por insistência. Agora só repete erro de servidor ou de rede.

### Decisões

[ADR 0020](./docs/adr/0020-pwa-do-motorista.md); matriz atualizada em
[`docs/seguranca/matriz-de-autorizacao.md`](./docs/seguranca/matriz-de-autorizacao.md).

- **Leitura própria do motorista**, não as rotas do console.
- **Sessão paralela à do console**, sem pacote compartilhado ainda.
- **GPS só em primeiro plano**, com as limitações ditas ao motorista.
- **Sem service worker nesta fase**: sem a fila da Fase 12, abriria offline e falharia em cada ação.
- **Navegação pelo aplicativo de mapas do aparelho**, por link, sem provedor contratado.

### Pendências conhecidas

| Item | Situação |
|---|---|
| Operação offline (service worker, IndexedDB, `ClientOperationId`) | Fase 12 |
| Tipos de ocorrência além da tentativa sem sucesso | Fase 13 |
| Comprovante de entrega | Fase 14 |
| Verificação manual em aparelho real e navegador real | antes da release (CLAUDE.md, seção 84); nesta fase a validação é automatizada |
| Instalação em navegador que exige service worker | chega com a Fase 12 |
| Ícone em PNG 192/512 para lojas e instaladores antigos | só SVG nesta fase |
| Comandos devolvem o modelo do console | a PWA ignora e relê pela leitura própria; reduzir junto com a Fase 12 |
| CI nunca executada | exige `git push`, não autorizado |

### Commit

`feat: PWA do motorista com rota do dia, execucao, ocorrencia e GPS em primeiro plano (Fase 11)`

---

## Fase 12 concluída — 2026-09-15

### IndexedDB

Fila local `operacoes` no banco `torre-motorista`, com os campos pedidos:

| Pedido | Campo |
|---|---|
| `ClientOperationId` UUIDv7 | `id`, gerado no aparelho antes de qualquer envio |
| tipo | `tipo`: `IniciarRota`, `RegistrarChegada`, `ConcluirEntrega`, `RegistrarTentativaFrustrada`, `ConcluirRota` |
| payload | `payload`: `{ alvoId, motivo }` |
| createdAt | `criadaEm` (e `ordem`, a sequência das ações do motorista) |
| status | `Pending`, `Uploading`, `Synced`, `Conflict`, `Failed` |
| tentativas | `tentativas`, `ultimaTentativaEm` |

Além da fila: cópia das leituras (rota do dia, rota, entrega), posições não enviadas e identidade de exibição
(nunca token). Service worker guarda só a casca do aplicativo.

### Backend — exatamente uma vez

`POST /api/motorista/sincronizacao`, lote de 1 a 100, desfecho por operação. A tabela
`operacoes_do_cliente` (somente-inserção, única por organização + motorista + operação) recebe o registro
**na mesma transação** do efeito, pelo mesmo comando do caminho online. Repetição devolve o desfecho
registrado com `repetida: true`; repetições simultâneas entram numa fila por motorista e encontram o registro.
Conflito e recusa também ficam registrados e são definitivos. Operação com identificador reaproveitado para
outro pedido é `operacao_divergente`.

### Casos obrigatórios

| Caso | API real (PostgreSQL) | PWA |
|---|---|---|
| Resposta perdida: backend conclui, resposta cai, PWA repete → mesmo resultado, sem efeito duplicado | `RespostaPerdidaERepetidaNaoDuplicaEfeito`: 1 evento `Entregue`, 1 registro, segunda resposta `repetida` | `resposta perdida: a repetição recebe o mesmo resultado e nada é aplicado duas vezes` |
| Conflito real: offline conclui, operador cancela, reconecta → cancelamento preservado | `ConclusaoAtrasadaNaoSobrescreveCancelamento`: `Conflito transicao_invalida`, entrega `Cancelada`, repetição devolve o mesmo conflito | `conflito real: cancelamento feito enquanto o motorista estava sem internet não é sobrescrito`: aviso recuperável, tela atualizada, "Entendi" |

### Testes pedidos

| Pedido | Onde |
|---|---|
| offline | PWA `offline e reconexão…`: ações sem rede refletidas na tela, contador de pendentes, nenhum envio; `sincronizador.test.ts` "sem internet no aparelho não tenta enviar" |
| reconnect | mesmo teste: evento `online` envia a fila; `guardadosNoAparelho.test.ts` e leitura pela cópia com o instante dela |
| duplicate retry | API `RepeticoesSimultaneasTemUmUnicoEfeito` (6 envios simultâneos, 1 efeito); PWA resposta perdida; sincronizador reenvia com o mesmo identificador |
| batch | API `LoteAplicaNaOrdemEDevolveDesfechoPorOperacao` (dia inteiro num lote, reenvio inteiro sem efeito) e `OperacaoInvalidaDivergenteOuAlheiaEhRecusadaSemDerrubarOLote`; PWA um único lote em ordem; sincronizador em lotes de 100 |
| conflict | API cancelamento e reatribuição (`EntregaPassadaAOutroMotoristaEnquantoOfflineEhConflito`); PWA conflito real; sincronizador mapeia cada desfecho |
| crash/restart de browser | PWA `fechar o aplicativo sem internet: reabre com a rota guardada e envia a fila…`; sincronizador retoma `Uploading` interrompido; rastreador reenvia posições guardadas no disco |
| ação já sincronizada | PWA `ação já sincronizada não é enviada de novo`; API repetição devolve `repetida` sem novo evento |

Além do pedido: lote vazio, maior que 100 ou com campo desconhecido (400); identificador que não é UUIDv7,
operação com mais de 7 dias; operação de outra organização recusada sem efeito; trigger somente-inserção
(UPDATE, DELETE, TRUNCATE); projeção local sem contar duas vezes; sessão sem rede por identidade guardada;
saída sem rede confirmada no servidor antes da próxima renovação; service worker nunca intercepta a API.

### Critério de aceite

> Nenhuma ação crítica pode depender de "tomara que o POST não repita".

✅ Estrutural e testado:

- a PWA não chama mais nenhum comando direto — o teste do fluxo completo verifica que nenhuma chamada a
  `/inicio`, `/chegada`, `/conclusao` ou `/tentativa-frustrada` sai do aplicativo;
- toda ação nasce com `ClientOperationId` e o servidor grava o registro junto com o efeito, então repetir —
  por rede instável, resposta perdida, envio simultâneo, aba fechada no meio — nunca aplica duas vezes.

### Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 538 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 409 | ✅ |
| Frontend — `operacao` | 24 | ✅ |
| Frontend — `motorista` | 74 | ✅ |
| Frontend — `rastreamento` | 13 | ✅ |
| **Total** | **1.080** | **✅** |

`npm run verificar` (lint, tipos, testes e build das três aplicações) sem erro; solução .NET com 0 aviso e
0 erro; formatação verificada.

### Security Gate 12

| Item | Resultado | Evidência |
|---|:---:|---|
| Tenant e dono | ✅ | motorista e organização vêm da sessão; operação sobre entrega de outra organização é recusada sem efeito (teste) |
| Mass assignment | ✅ | lote com campo desconhecido (`motoristaId`) responde 400 |
| Repetição e corrida | ✅ | índice único + fila por motorista; 6 envios simultâneos, 1 efeito |
| Integridade do registro | ✅ | trigger `insufficient_privilege` para UPDATE, DELETE e TRUNCATE |
| Limite | ✅ | política própria por motorista (`SincronizacoesPorMinuto`), lote de até 100 |
| Operação antiga ou forjada | ✅ | UUIDv7 obrigatório, até 7 dias, até 2 min no futuro |
| Token no aparelho | ✅ | IndexedDB guarda identidade de exibição, nunca token; token segue só em memória |
| Aparelho compartilhado | ✅ | sair apaga cópias, posições e identidade; saída sem rede é confirmada no servidor antes de renovar |
| Service worker | ✅ | só casca de mesma origem; nunca API nem outra origem (teste) |
| Segredos | ✅ | gitleaks v8.30.1: 0 achados |
| Dependências | ✅ | `fake-indexeddb` 6.2.5 só em teste (Apache-2.0); `npm audit`: 0; .NET sem pacote vulnerável ou obsoleto |

### Defeitos e ajustes durante a fase

1. **Log de erro falso em repetição simultânea.** A primeira versão deixava a segunda requisição da mesma
   operação perder no índice único ou no controle de versão da entrega; o resultado estava certo, mas o EF
   registrava erro de banco. A transação passou a entrar numa fila por motorista e conferir o registro na vez;
   o índice único ficou como rede de segurança.
2. **Banco local preso ao teste anterior.** A conexão do IndexedDB era guardada por módulo e continuava
   apontando para a fábrica do teste anterior. A conexão agora é reaberta quando a fábrica muda.
3. **Saída sem rede reabria a sessão.** Descoberto ao desenhar a sessão offline: sair sem internet deixava o
   cookie válido, e a próxima abertura renovaria sozinha. A saída não confirmada fica anotada e é confirmada
   antes de qualquer renovação.

Observação fora da fase: ao desligar o host de teste, o EF às vezes registra "An error occurred using a
transaction" de um processamento em segundo plano cancelado; acontece igual em classes de teste de fases
anteriores, sem falha de teste.

### Decisões

[ADR 0021](./docs/adr/0021-operacao-offline.md).

- **Registro da operação na mesma transação do efeito**, e não inbox genérica nem `Idempotency-Key` por rota.
- **Um endpoint em lote, ordenado**, reaproveitando os comandos e a máquina de estados do caminho online.
- **Conflito definitivo**: repetir não tenta de novo contra um estado que mudou.
- **Envio pelo aplicativo**, sem depender de Background Sync.
- **IndexedDB sem biblioteca** e service worker só de casca.
- **Sessão sem rede** por identidade de exibição guardada por 7 dias.

### Pendências conhecidas

| Item | Situação |
|---|---|
| Comprovante de entrega offline | Fase 14 (o roadmap o põe com a prova de entrega) |
| Retenção de `operacoes_do_cliente` | com as políticas de retenção |
| Envio com o navegador fechado | não existe na web sem Background Sync garantido; envia na próxima abertura (ADR 0021) |
| Verificação manual em aparelho real, modo avião | antes da release (CLAUDE.md, seção 84) |
| CI nunca executada | exige `git push`, não autorizado |

### Commit

`feat: operacao offline com fila no aparelho e sincronizacao exatamente uma vez (Fase 12)`

---

## Fase 13 concluída — 2026-09-15

### Motivos tipados

| Pedido no roadmap | Como ficou |
|---|---|
| DestinatarioAusente | `DestinatarioAusente` |
| EnderecoNaoEncontrado | `EnderecoNaoLocalizado` (nome já gravado desde a Fase 5) |
| Recusado | `RecusadaPeloDestinatario` (idem) |
| LocalFechado | `LocalFechado` |
| ProblemaComVeiculo | **novo** |
| ProblemaComMercadoria | **novo** |
| Outro | **novo**, único que exige descrição |

Os cinco motivos anteriores não foram renomeados: já estão gravados como texto em `eventos_da_entrega`, em
`operacoes_do_cliente` e nas filas dos aparelhos. Renomear exigiria migração de dados e quebraria aparelho com
fila pendente, sem ganho de significado ([ADR 0022](./docs/adr/0022-ocorrencias-e-tentativas.md)). Continua
existindo `AcessoImpedido`, anterior ao roadmap desta fase.

### Ocorrências

Tabela `ocorrencias`, somente-inserção por trigger, com tudo que a fase pediu:

| Pedido | Campo |
|---|---|
| tipo | `tipo`: TentativaDeEntrega, ProblemaComVeiculo, ProblemaComMercadoria, AcidenteOuIncidente, DificuldadeDeAcesso, Outro |
| severidade | `severidade`: Baixa, Media, Alta, Critica — do catálogo por tipo e motivo, ou informada |
| observação | `observacao`, complementar; exigida só em tipo/motivo `Outro` |
| horário | `ocorrida_em` (quando aconteceu) **e** `registrada_em` (quando o servidor gravou) |
| localização quando apropriado | `localizacao` `geography(Point,4326)`, opcional |
| autor | `autor_usuario_id` e `origem` (Motorista ou Operação); mais rota e motorista envolvidos |

### Regras

- **Tentativa frustrada produz timeline**: status, evento `TentativaFrustrada` e ocorrência entram no mesmo
  commit, pelo comando do motorista. Repetir não gera evento nem ocorrência nova.
- **Texto livre nunca é a única estrutura**: o motivo é tipado e obrigatório; a descrição só é exigida quando o
  vocabulário diz `Outro` — garantido no domínio, no `CHECK` do banco e na PWA (o botão só habilita com a
  descrição preenchida).
- A ocorrência que não muda status tem rota própria, no console e no aplicativo; cada rota recusa o que é da
  outra (`tentativa_pelo_motorista`, `tentativa_tem_rota_propria`).
- **Alerta `OcorrenciaCritica`**, definido na Fase 10, ganhou produtor: ocorrência crítica com a entrega ainda
  em aberto abre alerta Crítica; resolve sozinho quando a entrega sai da operação.

### Testes pedidos

| Pedido | Onde |
|---|---|
| cada motivo | `CadaMotivoDaTentativaRegistraOcorrenciaComSeveridadeETimeline` (7 motivos, pela API real, com severidade e timeline); `OcorrenciaTestes` no domínio |
| reagendamento | `FalhaDeEntregaDeixaRastreabilidadeOperacionalCompleta` (tentativa crítica → reagendamento → timeline completa); `ReagendamentoValidaJanelaEIsolamento` |
| limite de tentativas | `TentativasExcedidasAbreEResolveQuandoAEntregaEhCancelada`; `TentativasExcedidasAteAEntregaSairDaOperacao` |
| ocorrência crítica | `OcorrenciaCriticaAbreAlertaEResolveQuandoAEntregaSaiDaOperacao` (API + motor de alertas); `OcorrenciaCriticaAbreEnquantoAEntregaContinuaNaOperacao` (regra) |
| autorização | matriz (`GET /api/ocorrencias`, `POST /api/entregas/{id}/ocorrencias`, `POST /api/motorista/entregas/{id}/ocorrencia`); `OcorrenciaDeOutraOrganizacaoNaoApareceNemPodeSerRegistrada` |

Além do pedido: descrição obrigatória em `Outro` (API e PWA), ocorrência do motorista que não muda status,
filtros por tipo e severidade, registro somente-inserção (UPDATE, DELETE, TRUNCATE), descrição fora da timeline
e fora da evidência do alerta, e a descrição viajando na fila offline como parte da identidade da operação.

### Critério de aceite

> Falha de entrega deixa rastreabilidade operacional clara.

✅ Cada falha responde, sem abrir o banco: **o que** (tipo e motivo tipados), **quando aconteceu** e **quando
foi registrada**, **onde** (coordenada, quando o aparelho informou), **quem** registrou e por qual canal,
**com que gravidade**, e **o que mudou na entrega** (timeline e status). O teste
`FalhaDeEntregaDeixaRastreabilidadeOperacionalCompleta` percorre isso ponta a ponta.

### Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 558 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 440 | ✅ |
| Frontend — `operacao` | 24 | ✅ |
| Frontend — `motorista` | 75 | ✅ |
| Frontend — `rastreamento` | 13 | ✅ |
| **Total** | **1.132** | **✅** |

`npm run verificar` (lint, tipos, testes e build das três aplicações) sem erro; solução .NET com 0 aviso e
0 erro; formatação verificada.

### Security Gate 13

| Item | Resultado | Evidência |
|---|:---:|---|
| Autoridade e canal | ✅ | console registra pelo `entregas:operacao`; motorista, só nas próprias entregas; cada canal recusa o tipo do outro |
| Isolamento | ✅ | ocorrência em entrega de outra organização responde como inexistente, e nada é gravado |
| Integridade do registro | ✅ | trigger `insufficient_privilege` em UPDATE, DELETE e TRUNCATE |
| Coerência no banco | ✅ | `CHECK` de motivo por tipo e de descrição obrigatória em `Outro` |
| Privacidade | ✅ | descrição não entra na timeline (só `comDescricao`) nem na evidência do alerta; coordenada não vai ao tempo real |
| Entrada | ✅ | tipo e severidade só por nome exato; descrição limitada a 500; instante entre 7 dias atrás e 2 min no futuro |
| Segredos | ✅ | gitleaks v8.30.1: 0 achados |
| Dependências | ✅ | nenhum pacote novo; `npm audit`: 0; 9 projetos sem pacote vulnerável ou preterido |

### Defeitos e ajustes durante a fase

1. **Endpoint novo sem `using`.** O `Program.cs` mapeava `MapearEndpointsDeOcorrencias` sem importar o
   namespace; o build acusou e a importação entrou.
2. **`exactOptionalPropertyTypes` recusou a descrição opcional.** O tipo do pedido da fila declarava
   `observacao?: string`, e a tela passava `string | undefined`. O tipo passou a declarar `| undefined`.
3. **Testes da fila e da projeção sem o campo novo.** O payload da operação ganhou `observacao`, e os dois
   testes que montavam payload à mão foram atualizados.

### Decisões

[ADR 0022](./docs/adr/0022-ocorrencias-e-tentativas.md); matriz atualizada em
[`docs/seguranca/matriz-de-autorizacao.md`](./docs/seguranca/matriz-de-autorizacao.md).

- **Ocorrência é entidade própria e somente-inserção**, e não mais um tipo de evento da timeline.
- **Motivos acrescentados, nenhum renomeado**, para não migrar dado gravado nem quebrar fila de aparelho.
- **Severidade do catálogo**, com possibilidade de informar outra.
- **Tentativa continua sendo comando**, porque muda status; ocorrência que não muda status tem rota própria.
- **Texto livre fora da timeline e fora do alerta**, por privacidade e porque timeline não se apaga.

### Pendências conhecidas

| Item | Situação |
|---|---|
| Foto ou anexo na ocorrência | Fase 14, com a prova de entrega |
| Ocorrência sem conexão (fora a tentativa) | exige rede; a tentativa, que é a crítica da rota, já vai pela fila |
| Fluxo de tratamento da ocorrência (atribuir, encerrar com parecer) | quem tem ciclo de vida é o alerta |
| Tela de ocorrências no console | Fase 18 |
| Retenção de `ocorrencias` | com as políticas de retenção |
| CI nunca executada | exige `git push`, não autorizado |

### Commit

`feat: ocorrencias da ultima milha com motivo tipado, severidade e alerta critico (Fase 13)`

---

## Fase 14 concluída — 2026-09-16

### Dados do comprovante

| Pedido no roadmap | Como ficou |
|---|---|
| nome de quem recebeu | `recebido_por`, obrigatório |
| timestamp | `registrado_em` (servidor) e `enviado_em` por arquivo (gravação no storage) |
| posição | `localizacao` `geography(Point,4326)`, quando o aparelho informa |
| foto | arquivo do tipo `Foto`, no storage |
| assinatura | tipo `Assinatura` aceito pela API; captura na tela ainda não existe no aplicativo |
| observação | `observacao`, opcional |

Mais o que a prova exige e o roadmap não listou: autor, motorista, rota, tipo de conteúdo real, tamanho real
e **SHA-256** de cada arquivo.

### Storage

`IObjectStorage` com três operações: autorizar envio, autorizar leitura, obter metadados. Arquivo **não**
entra no PostgreSQL e não passa pela API de negócio — o limite de corpo de 1 MB do Kestrel continua valendo.

O adaptador desta fase é **disco local com URL assinada servida pela própria API**. O roadmap diz "em
produção, direção provável: S3 ou compatível", e o [ADR 0007](./docs/adr/0007-storage-de-comprovantes-fora-do-banco.md)
registra que essa escolha é da **Fase 25**. Criar bucket agora seria recurso pago sem autorização e decisão
de fase futura; a abstração existe justamente para que a troca não alcance domínio, aplicação nem cliente.

### Upload

```text
1. POST /api/motorista/entregas/{id}/comprovante/autorizacao   → URL assinada curta
2. PUT  /api/arquivos/{chave}?expiraEm&assinatura&tipoDeConteudo&tamanhoMaximo   → direto ao storage
3. POST /api/motorista/entregas/{id}/comprovante   → confere objetos, grava metadados e conclui a entrega
4. GET  /api/entregas/{id}/comprovante   → metadados + URLs assinadas de leitura
```

A assinatura HMAC cobre operação, chave, tipo de conteúdo, tamanho máximo e expiração: trocar qualquer campo
invalida a URL. Os metadados gravados são os que o **storage** apurou, nunca os declarados pelo cliente.

### Segurança

Nada é público. Os endpoints de arquivo são anônimos por desenho — a credencial é a assinatura, emitida só
depois de autenticação, autorização e verificação de tenant, como numa URL pré-assinada de S3. Sem assinatura
válida e no prazo: 403, sem revelar se o objeto existe. Chave com travessia de diretório é recusada.

### Testes pedidos

| Pedido | Onde |
|---|---|
| upload | `ConclusaoComProvaGuardaMetadadosEServeArquivoSoPorUrlAssinada` (autorização → PUT → registro → leitura, com hash conferido) |
| tipo/tamanho | `TipoOuTamanhoForaDoAutorizadoEhRecusado` (tipo não aceito 422; tipo diferente do autorizado 415; acima do limite 413) e `ComprovanteTestes` no domínio |
| expiração | `UrlAssinadaExpiraEAssinaturaAdulteradaNaoVale` (relógio avançado; assinatura trocada) |
| tenant isolation | `ComprovanteDeOutraOrganizacaoNaoApareceNemPodeSerRegistrado` (404 igual a inexistente; arquivo de outra entrega recusado) |
| arquivo inexistente | `ArquivoInexistenteEChaveComTravessiaSaoRecusados` e `arquivo_nao_enviado` no registro |
| retry idempotente | `RepetirORegistroNaoDuplicaAProva` (um comprovante, um evento `Entregue`) |
| conclusão sem evidência quando a política exigir | `ComprovanteObrigatorioTestes.ConclusaoSemProvaEhRecusadaEComProvaPassa` |

### Critério de aceite

> Entrega concluída pode ser provada sem tornar arquivos públicos.

✅ O comprovante guarda quem recebeu, quando, onde e com que evidência; o arquivo só sai do storage por URL
assinada de curta duração, emitida a quem já passou pela autorização — e o teste do caminho feliz confere que
o conteúdo baixado é byte a byte o que subiu, enquanto a mesma URL sem assinatura responde 403.

### Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 571 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 462 | ✅ |
| Frontend — `operacao` | 24 | ✅ |
| Frontend — `motorista` | 76 | ✅ |
| Frontend — `rastreamento` | 13 | ✅ |
| **Total** | **1.168** | **✅** |

`npm run verificar` (lint, tipos, testes e build das três aplicações) sem erro; solução .NET com 0 aviso e
0 erro; formatação verificada.

### Defeitos encontrados e corrigidos durante a fase

| Defeito | Como apareceu | Correção |
|---|---|---|
| `GET /api/arquivos/{chave}` sem assinatura respondia **400**, não 403 | `ConclusaoComProvaGuardaMetadadosEServeArquivoSoPorUrlAssinada` | os parâmetros da assinatura passaram a ser opcionais no contrato: ausentes, a resposta é a mesma de assinatura inválida. O 400 do binding contava que a rota existe e o que ela espera — a matriz de autorização e o ADR 0023 já prometiam 403, e o código não cumpria |
| `ArquivoConfirmado` expunha `init` público no domínio | `TiposDoDominioNaoExpoemSetterPublico` | virou classe selada com propriedades somente-leitura |
| Teste de travessia não exercitava o endpoint | `ArquivoInexistenteEChaveComTravessiaSaoRecusados` | o cliente HTTP normalizava `..` antes do envio e a requisição nem chegava à rota; passou a usar a barra escapada |
| Teste de tenant não exercitava a validação | `ComprovanteDeOutraOrganizacaoNaoApareceNemPodeSerRegistrado` | registrava a prova legítima antes e caía no atalho de idempotência, que não monta comprovante nenhum; a tentativa com chave de outra organização passou a vir primeiro |

### Security Gate 14

| Item | Resultado | Evidência |
|---|:---:|---|
| Nada público | ✅ | leitura só por URL assinada curta; sem assinatura, 403 |
| Autorização e tenant | ✅ | registrar é do motorista da entrega; ler é do console; comprovante de outra organização responde como inexistente |
| Upload | ✅ | tipo e tamanho conferidos contra o autorizado; corte no limite; `Content-Type` diferente do autorizado é 415 |
| Path traversal | ✅ | chave validada segmento a segmento; `..` recusado |
| Integridade | ✅ | SHA-256 gravado; tabelas somente-inserção por trigger |
| Limite de corpo | ✅ | 1 MB na API de negócio; só a rota de arquivo aceita o tamanho autorizado |
| Cache e exposição | ✅ | `Cache-Control: no-store` e `Content-Disposition: attachment` no download |
| Segredos | ✅ | chave de assinatura do storage vem de configuração; efêmera só em desenvolvimento e teste |
| Custo | ✅ | nenhum recurso de nuvem criado: storage em disco local, S3 fica para a Fase 25 |

### Decisões

[ADR 0023](./docs/adr/0023-prova-de-entrega.md), implementando o [ADR 0007](./docs/adr/0007-storage-de-comprovantes-fora-do-banco.md).

- **Comprovante somente-inserção e único por entrega.**
- **Registro e conclusão no mesmo commit.**
- **Storage local nesta fase**, S3 na Fase 25, atrás de `IObjectStorage`.
- **Endpoints de arquivo anônimos por desenho**, com a assinatura como credencial.
- **Metadados vindos do storage**, não do cliente.

### Pendências conhecidas

| Item | Situação |
|---|---|
| Captura de assinatura na tela | API já aceita o tipo; a tela entra com o console e a UX final |
| Antivírus e remoção de EXIF | Fase 20 (segurança e privacidade) |
| Retenção e ciclo de vida do objeto | Fase 20, como o ADR 0007 registrou |
| Storage para várias instâncias | Fase 25, trocando o adaptador |
| CI nunca executada | exige `git push`, não autorizado |

### Commit

`feat: prova de entrega com comprovante e storage por url assinada (Fase 14)`

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

## O que foi feito

```text
console: POST /api/entregas/{id}/link-de-rastreamento          (entregas:operacao)
  → token de 32 bytes em Base64Url; o banco guarda só o SHA-256
  → revoga o anterior e grava o novo na mesma transação
  → devolve o valor UMA vez; perdido, é reemitido, nunca recuperado

destinatário: GET /api/publico/rastreamento/{token}            (anônimo, limite por endereço)
  → formato implausível é descartado antes do banco
  → hash do apresentado → organização e entrega → página
```

A página (`apps/rastreamento`, rota `/e/:token`) mostra código, status, janela, chegada prevista,
situação do SLA, bairro/cidade/UF, os marcos públicos e, depois da conclusão, a prova de entrega.

## Testes pedidos

| Pedido | Onde |
|---|---|
| token forte, só hash persistido | `TokenDeRastreamentoTestes` e `BancoGuardaSoOHashEReemitirDerrubaOLinkAnterior` (o hash gravado é conferido contra o SHA-256 do valor emitido; procurar o token na linha inteira não acha nada) |
| token não enumerável | 32 bytes de entropia; formato implausível recusado antes do banco; limite por endereço |
| resposta neutra | `TokenQueNaoAbreNadaRespondeSempreAMesmaCoisa` — malformado, desconhecido, revogado e expirado com resposta idêntica campo a campo |
| privacidade | `LinkMostraAEncomendaESilenciaSobreAOperacao` varre o JSON atrás de campo interno; `PosicaoApareceAproximadaSoEnquantoAEntregaEstaACaminho` prova o arredondamento e o descarte por idade |
| rate limiting | `RastreamentoPublicoComLimiteBaixoTestes.ConsultaPublicaTemLimitePorEndereco` (inclusive tentativa com outro token, que cai no mesmo limite) |
| tenant | `OutraOrganizacaoNaoEmiteLinkNemAlcancaAEntrega` (404 idêntico ao inexistente) |
| proteção do comprovante | `ProvaDaEntregaApareceDepoisDaConclusaoESoPorUrlAssinada` (a mesma chave sem assinatura continua 403) |
| auditoria sem o segredo | `EmissaoFicaNaAuditoriaSemOValorDoLink` |
| página | `apps/rastreamento` — link que não abre, falha de rede sem repassar mensagem do servidor, ausência de campo de busca e do próprio token na tela |

## Critério de aceite

> Destinatário consegue entender onde sua entrega está sem acesso ao sistema interno.

✅ Com o link, ele vê onde a encomenda está, quando deve chegar e quem recebeu — sem conta, sem
sessão e sem enxergar motorista, veículo, rota, endereço completo ou qualquer identificador interno.

## Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 606 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 480 | ✅ |
| Frontend — `operacao` | 24 | ✅ |
| Frontend — `motorista` | 76 | ✅ |
| Frontend — `rastreamento` | 17 | ✅ |
| **Total** | **1.225** | **✅** |

`npm run verificar` (lint, tipos, testes e build das três aplicações) sem erro; solução .NET com 0 aviso e
0 erro; formatação verificada.

### Defeitos encontrados e corrigidos durante a fase

| Defeito | Como apareceu | Correção |
|---|---|---|
| `setState` síncrono dentro do efeito na página pública | ESLint (`react-hooks`) reprovou a build do frontend | o resultado passou a carregar o token que o produziu; some o `setState` de limpeza e, de quebra, trocar de link deixa de exibir por um instante os dados do link anterior |
| Rota nova não declarada na lista fechada de comandos | `ExecucaoTestes.NaoExisteEndpointGenericoDeStatus` | a rota entrou na lista — o teste existe exatamente para obrigar essa decisão a ser consciente |
| Teste de vazamento com falso positivo | `LinkMostraAEncomendaESilenciaSobreAOperacao` acusava "rota" dentro do marco legítimo `SaiuParaRota` | passou a procurar nome de campo entre aspas, não substring de valor |
| Consulta de auditoria com coluna inexistente | `EmissaoFicaNaAuditoriaSemOValorDoLink` | a coluna é `alvo_tipo`, não `recurso` |

## Security Gate 15

| Item | Resultado | Evidência |
|---|:---:|---|
| Token forte | ✅ | 32 bytes de `RandomNumberGenerator`, Base64Url, mesmo mecanismo do token de renovação |
| Só hash persistido | ✅ | `octet_length = 32` por check constraint; teste confere o hash e procura o valor emitido na linha inteira |
| Token não enumerável | ✅ | espaço de 2^256; formato conferido antes do banco; limite por endereço fecha a força bruta |
| Resposta neutra | ✅ | quatro motivos distintos, uma resposta só — comparada campo a campo no teste |
| Autoridade pelo token | ✅ | filtro de tenant ignorado de propósito e substituído por comparação explícita com a organização do token |
| Privacidade de localização | ✅ | só a caminho, grade de 0,01°, descarte após 15 min; precisão anunciada nunca melhor que a real |
| Dados fora da página | ✅ | teste varre o JSON por nome de campo (`motoristaId`, `rotaId`, `logradouro`, `cep`, `id`, `dados`…) |
| Proteção do comprovante | ✅ | URL assinada curta; sem assinatura, 403; desligável por configuração |
| Cache | ✅ | `Cache-Control: no-store` na resposta pública |
| Rate limiting | ✅ | `limite-rastreamento-publico` por endereço, com 429 e `Retry-After` |
| Segredos | ✅ | nenhum; o token nunca entra em log nem em auditoria |
| Custo | ✅ | nenhum recurso novo, nenhuma dependência nova |

## Decisões

[ADR 0024](./docs/adr/0024-rastreamento-publico.md).

- **Token guardado só como hash**, com a consequência aceita: link perdido é reemitido, não recuperado.
- **Um link ativo por entrega**, garantido por índice único parcial — reemitir derruba o repassado adiante.
- **Uma resposta só** para malformado, desconhecido, expirado e revogado.
- **Posição grossa**: só a caminho, arredondada em ~1,1 km e descartada após 15 minutos.
- **Sem TanStack Query na página pública**: uma consulta só não paga uma dependência no pacote que o
  destinatário baixa.

## Pendências conhecidas

| Item | Situação |
|---|---|
| Envio do link ao destinatário (e-mail, SMS) | notificação está fora do escopo declarado (`CLAUDE.md`, seção 5) |
| Botão de emitir o link na tela | entra com o console, na Fase 18 |
| Mapa na página pública | Fase 18 traz o mapa; aqui a região é texto, de propósito |
| Ajuste da grade por operação | a política é constante do domínio; vira configuração se houver caso real |
| CI nunca executada | exige `git push`, não autorizado |

## Commit

`feat: rastreamento publico com token por hash e posicao aproximada (Fase 15)`

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

## O que foi feito

```text
console (administrador): POST /api/integracoes            → chave tlog.<identificador>.<segredo>, mostrada UMA vez
                         POST /api/integracoes/{id}/revogacao

ERP: POST /api/integracoes/v1/entregas                    (Authorization: Bearer <chave>, Idempotency-Key)
ERP: GET  /api/integracoes/v1/entregas/{id}               (acompanha sem abrir o console)
ERP: POST /api/integracoes/v1/importacoes/previa          (confere o arquivo sem gravar)
ERP: POST /api/integracoes/v1/importacoes                 (chave por linha = hash do arquivo + número)
```

A credencial é de máquina: esquema de autenticação próprio, sem sessão, sem perfil e sem canal. O
principal carrega a organização e o identificador da integração, e **nenhuma** reivindicação de usuário —
por isso a entrega criada por um ERP nasce com autor vazio na timeline, enquanto a auditoria guarda qual
credencial agiu.

## Testes pedidos

| Pedido | Onde |
|---|---|
| replay | `ReenvioDaMesmaChaveNaoCriaOutraEntrega` (200 em vez de 201; uma linha em `requisicoes_de_integracao`) |
| payload conflitante | `MesmaChaveComCorpoDiferenteEhConflito` (409) |
| importação | `ImportacaoConfereOArquivoAntesDeGravarEReenvioNaoDuplica` (prévia acusa; lote com erro não grava nada; reenvio do arquivo não duplica) e `PreviaDaImportacaoTestes` na unidade (cabeçalho, coluna, tipo, identificador repetido, limite de linhas) |
| tenant | `CredencialDeUmaOrganizacaoNaoAlcancaOutra` (404 idêntico ao de identificador inventado, e nada criado) |
| credencial revogada | `CredencialRevogadaEDesconhecidaRespondemIgual` (401 idêntico nos três casos) |
| rate limiting | política `limite-integracao` por credencial, com 429 e `Retry-After` |
| fila reprocessada | `MesmoIdentificadorDeOrigemComChaveNovaDevolveAEntregaQueJaExiste` |
| separação de autoridade | `TokenDoConsoleNaoValeNaApiExternaENemOContrario` e as linhas novas de `AutorizacaoTestes` |
| só hash persistido | `SoOHashDoSegredoEhGuardado` (confere contra o SHA-256 do segredo e procura a chave na linha inteira) |
| registro imutável | `RegistroDeIdempotenciaEhSomenteInsercao` (trigger recusa `UPDATE`) |
| formato da chave | `SegredosDeIntegracaoTestes` e `LeitorDeCsvTestes` na unidade |

## Critério de aceite

> Um ERP fictício consegue integrar sem depender da UI.

✅ Com uma chave emitida pelo administrador, o ERP cria entregas, acompanha o que criou e importa lotes por
arquivo — sem console, sem sessão e sem risco de duplicar pedido em retentativa ou fila reprocessada.

## Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 646 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 507 | ✅ |
| Frontend — `operacao` | 24 | ✅ |
| Frontend — `motorista` | 76 | ✅ |
| Frontend — `rastreamento` | 17 | ✅ |
| **Total** | **1.292** | **✅** |

`npm run verificar` (lint, tipos, testes e build das três aplicações) sem erro; solução .NET com 0 aviso e
0 erro; formatação verificada.

### Defeitos encontrados e corrigidos durante a fase

| Defeito | Como apareceu | Correção |
|---|---|---|
| Separador `_` da chave de API colidia com o alfabeto Base64Url | `SegredosDeIntegracaoTestes.GeraChaveComPrefixoIdentificadorESegredo` reprovou já na primeira execução | separador virou `.`; o defeito quebraria **apenas as chaves cujo segredo sorteasse `_`** — falha intermitente, que apareceria só para parte dos integradores, em produção |
| `throw;` usado como expressão dentro de `??` | compilação | o rethrow virou um `if` explícito, e o caso "bateu em índice que não é destes dois" passou a ser tratado à parte |
| Teste de tenant esperava 422 | `CredencialDeUmaOrganizacaoNaoAlcancaOutra` | o correto é 404 idêntico ao de identificador inventado, que é o padrão do projeto; a asserção ficou mais forte, comparando as duas respostas |
| Auxiliares de teste sem estado de instância e `OkAsync` inexistente na base | analisador CA1822 e compilação | auxiliares tornados estáticos e `OkAsync` escrito na própria classe, como nas demais |

## Security Gate 16

| Item | Resultado | Evidência |
|---|:---:|---|
| Credencial separada da identidade humana | ✅ | esquema próprio; token de pessoa não vale na API externa e a chave não vale no console |
| Segredo | ✅ | 32 bytes sorteados; só o SHA-256 é persistido; check de 32 bytes no banco |
| Chave reconhecível em vazamento | ✅ | prefixo fixo `tlog.`, que varredura de segredos consegue casar |
| Resposta neutra | ✅ | chave desconhecida, segredo errado e credencial revogada devolvem o mesmo 401 |
| Autorização | ✅ | emitir e revogar exige administrador; supervisor e operador recebem 403 |
| Tenant | ✅ | recurso de outra organização responde como inexistente, e nada é criado |
| Idempotência | ✅ | única por chave e por identificador de origem, gravada no mesmo commit do efeito |
| Registro imutável | ✅ | `requisicoes_de_integracao` e `referencias_externas_de_entrega` somente-inserção por trigger |
| Rate limiting | ✅ | por credencial, e não por endereço — datacenter compartilhado não penaliza terceiros |
| Upload | ✅ | limite de 1 MB do Kestrel, teto de linhas no arquivo e cabeçalho fixo; o leitor só interpreta colunas conhecidas |
| Segredos | ✅ | a chave nunca entra em log nem em auditoria; a auditoria guarda só o identificador público |
| Custo | ✅ | nenhuma dependência nova, nenhum recurso de nuvem |

## Decisões

[ADR 0025](./docs/adr/0025-api-de-integracao.md).

- **Credencial de máquina em esquema próprio**, sem sessão nem perfil.
- **Chave com parte pública e parte secreta**; SHA-256, não hasher de senha.
- **Autor opcional** em `IContextoDoUsuario`: integração não é pessoa, e usuário de serviço criaria conta fantasma.
- **Idempotência em duas camadas**, com o registro no mesmo commit da entrega — por um gancho transacional,
  já que aninhar transação é recusado pelo provedor.
- **Leitor de CSV próprio**, com prévia obrigatória e chave derivada do hash do arquivo.

## Pendências conhecidas

| Item | Situação |
|---|---|
| Tela de credenciais no console | entra com o console, na Fase 18 |
| Webhooks de saída | é a Fase 17 |
| Atualizar e cancelar entrega pela API externa | fora do pedido desta fase; o ERP cria e acompanha |
| Importação tudo-ou-nada num único commit | exigiria refatorar a criação de entrega; a prévia e a idempotência por linha dão a mesma garantia prática |
| CI nunca executada | exige `git push`, não autorizado |

## Commit

`feat: api de integracao com credencial de maquina e idempotencia (Fase 16)`

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

## O que foi feito

```text
SaveChanges do fato ──┬── entrega / evento / auditoria
                      └── linha no outbox              (MESMA transação)

despachante: reserva o outbox com FOR UPDATE SKIP LOCKED
  → cria uma entrega por assinatura interessada (única por assinatura + mensagem)

entregador: reserva, ARRENDA e libera o banco → POST assinado fora da transação
  → registra tentativa; falhou, adia com backoff; esgotou, vai para Falhada (visível)

console (administrador): assina, revoga, consulta histórico e REENVIA o que desistiu
```

> **Sobre o SQS.** O ROADMAP cita SQS, que é recurso pago. Nada de nuvem foi criado: a fila é uma tabela
> com `FOR UPDATE SKIP LOCKED` e os processadores rodam no próprio host, atrás da mesma separação que o
> ADR 0007 usou para o storage. A troca é decisão da Fase 25, com custo na mesa.

## Testes pedidos

| Pedido | Onde |
|---|---|
| duplicate delivery | `DespacharDuasVezesNaoGeraEntregaRepetida` (índice único por assinatura + mensagem) |
| worker crash | arrendamento: a reserva adia e libera o banco, e a entrega volta sozinha quando o prazo vence — provado por `AdiarPorArrendamento` na unidade e pelo fluxo de retentativa na integração |
| partial failure | o lote continua depois de uma entrega falhar; cada entrega tem estado próprio |
| HMAC | `AssinaturaHmacDeWebhookTestes` (corpo alterado, segredo errado, carimbo fora da janela, cabeçalho malformado) e a conferência real em `OEventoChegaAoAssinanteAssinadoEComIdentificadorParaDeduplicar` |
| endpoint 500 | `AssinanteQuebradoEhRetentadoEDepoisDesisteDeFormaVisivel` |
| timeout | `AssinanteQueNaoRespondeNoPrazoContaComoFalhaERetenta` |
| DLQ | estado `Falhada` com histórico completo, e volta só por `ReenvioManualColocaDeVoltaNaFilaEEntrega` |
| SSRF | `DestinoDeWebhookTestes` — loopback, privadas, link-local, metadados e multicast recusados |
| segredo protegido | `OSegredoNaoApareceEmConsultaNemNaAuditoria` |
| histórico imutável | `HistoricoDeTentativaEhSomenteInsercao` |
| tenant | `OutraOrganizacaoNaoEnxergaAssinaturaNemEntrega` |

## Critério de aceite

> Salvar estado e perder webhook não pode ser um failure mode silencioso.

✅ O evento nasce na mesma transação do fato, então não existe estado salvo sem aviso registrado. O que
não foi entregue tem estado, contagem de tentativas e histórico visíveis, e o que desistiu fica em
`Falhada` até uma pessoa mandar de novo — em nenhum ponto do caminho algo some sem deixar rastro.

## Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 689 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 518 | ✅ |
| Frontend — `operacao` | 24 | ✅ |
| Frontend — `motorista` | 76 | ✅ |
| Frontend — `rastreamento` | 17 | ✅ |
| **Total** | **1.346** | **✅** |

`npm run verificar` (lint, tipos, testes e build das três aplicações) sem erro; solução .NET com 0 aviso e
0 erro; formatação verificada.

### Defeitos encontrados e corrigidos durante a fase

| Defeito | Como apareceu | Correção |
|---|---|---|
| Índice único em `(entrega, número da tentativa)` | violação de chave duplicada na retentativa | o índice era incompatível com a entrega no mínimo-uma-vez que o próprio código documenta: arrendamento vencido gera tentativa legítima com o mesmo número. Virou índice de leitura |
| Assinante de teste dentro da API de teste | nenhuma entrega chegava | a fábrica de testes usa servidor de memória, que não escuta porta; o assinante virou um servidor HTTP próprio em porta efêmera, e agora o webhook percorre rede de verdade |
| Chave de criptografia efêmera por fábrica | `AuthenticationTagMismatchException` ao decifrar | o banco é compartilhado pela coleção, e o entregador é global por desenho: passou a haver uma chave por processo de teste, sorteada em tempo de execução |
| Asserção de "exatamente 6 tentativas" | 7 tentativas com as classes misturadas | o sistema promete "no mínimo 6"; a asserção passou a exigir isso e que o histórico bata com o contador |
| Docker desligado | 11 falhas idênticas na primeira execução | ambiente, não código: o serviço foi iniciado antes de seguir |

## Security Gate 17

| Item | Resultado | Evidência |
|---|:---:|---|
| Nada se perde | ✅ | outbox no mesmo commit; fila durável no banco; falha de rodada não derruba o serviço |
| Segredo do assinante | ✅ | cifrado com AES-GCM (precisa ser recuperável para assinar); ausente de resposta, log e auditoria; ilegível no banco |
| Chave de criptografia | ✅ | obrigatória fora de desenvolvimento e teste; efêmera só ali, com aviso |
| Assinatura | ✅ | HMAC-SHA256 sobre `t.corpo`; o carimbo impede reenvio eterno; conferência em tempo constante |
| SSRF | ✅ | destino **resolvido** conferido, sem seguir redirecionamento, com liberação local só em desenvolvimento e teste |
| Autorização | ✅ | assinar, revogar e reenviar exigem administrador |
| Tenant | ✅ | assinatura e entrega de outra organização respondem como inexistentes |
| Histórico | ✅ | `tentativas_de_webhook` somente-inserção por trigger |
| Entrega repetida | ✅ | índice único por assinatura + mensagem; o assinante ainda recebe o `X-Torre-Event-Id` para deduplicar |
| Custo | ✅ | nenhum recurso de nuvem; `Microsoft.Extensions.Http` já estava fixado centralmente |

## Decisões

[ADR 0026](./docs/adr/0026-webhooks-e-backbone-assincrono.md).

- **Outbox gravado pelo contexto de persistência**, não pelo caso de uso.
- **Fila no PostgreSQL** com `SKIP LOCKED`; SQS fica para a Fase 25.
- **Arrendamento** em vez de transação aberta durante o POST.
- **Desistência é estado visível**, não remoção; a volta é manual e auditada.
- **Segredo cifrado**, não hasheado — é o único jeito de continuar assinando.

## Pendências conhecidas

| Item | Situação |
|---|---|
| SQS, DLQ gerenciada e filas separadas | Fase 25, trocando o adaptador; hoje o estado `Falhada` cumpre o papel da fila morta |
| Tela de webhooks no console | entra com o console, na Fase 18 |
| Eventos além dos quatro mínimos | o vocabulário cresce quando houver assinante pedindo |
| Limpeza do outbox antigo | retenção entra na Fase 20, junto com a de GPS |
| CI nunca executada | exige `git push`, não autorizado |

## Commit

`feat: webhooks com outbox transacional e entrega confiavel (Fase 17)`

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

## O que foi feito

```text
/               Painel operacional — contadores e a fila do que precisa de gente
/mapa           Mapa da Operação (tela símbolo) — pontos ao vivo + lista sincronizada
/entregas       lista com filtro por status e busca por código
/entregas/:id   estado, previsão explicada, timeline, ocorrências, prova e link de rastreamento
/rotas          rotas do dia, com paradas pendentes na própria linha
/motoristas     lista e detalhe com a última posição conhecida
/alertas        abertos e resolvidos, com resolução que exige observação
/ocorrencias    registro da última milha
/integracoes    credenciais de máquina (chave mostrada uma vez)
/webhooks       assinaturas e fila de entregas, com reenvio do que desistiu
```

Além das oito telas do ROADMAP, a fase fechou **sete pendências** que fases anteriores registraram
apontando para cá: console consumindo os avisos de tempo real (Fase 8), previsão e risco na tela (Fase 9),
alertas (Fase 10), ocorrências (Fase 13), botão de emitir link de rastreamento (Fase 15), tela de
credenciais (Fase 16) e tela de webhooks com reenvio (Fase 17).

> **Sobre custo.** O mapa usa MapLibre GL, mas o **estilo vem de configuração** e não há provedor padrão
> embutido: sem ele os pontos aparecem sobre fundo neutro. Nenhum serviço pago foi contratado, nenhuma
> chave inventada. A escolha de provedor é decisão da Fase 25.

## Dependências novas

| Pacote | Justificativa (regra 71) |
|---|---|
| `maplibre-gl` | renderizar mapa vetorial com zoom, camadas e projeção é trabalho de anos; a alternativa real seria não ter mapa. OSS, sem chave, sem servidor próprio |
| `@microsoft/signalr` | é o cliente do protocolo que o servidor fala desde a Fase 8 (ADR 0017); reimplementar negociação, reconexão e fallback seria reescrever a biblioteca |

Ambas gratuitas, mantidas, sem serviço pago atrás, com versão fixada exata. `npm audit`: 0 vulnerabilidades.

## Testes pedidos

| Pedido | Onde |
|---|---|
| tela símbolo | `Mapa da operação` — monta, lista as entregas a caminho e avisa quando não há provedor configurado |
| navegação e sessão | recuperação por cookie, login, saída, rota inexistente |
| painel | contadores, fila de alertas e ocorrências, e o caso vazio ("a operação está limpa") |
| detalhe da entrega | previsão explicada, ocorrência, ausência de comprovante tratada como estado e não como erro |
| pendência da Fase 15 | emissão do link de rastreamento, com o valor mostrado uma vez |
| pendência da Fase 17 | reenvio da entrega de webhook que desistiu |
| pendência da Fase 10 | resolução de alerta com observação, conferindo o corpo enviado |

## Critério de aceite

> Usuário deve entender o estado da operação em menos de alguns segundos.

✅ A tela de abertura responde a uma pergunta só — "há algo exigindo ação agora?" — com quatro contadores
e a fila de alertas abertos, sem gráfico e sem rolagem. Do painel ao mapa e ao detalhe da entrega são dois
cliques, e cada linha de tabela já traz o que decide se aquele caso precisa de atenção.

## Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 689 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 518 | ✅ |
| Frontend — `operacao` | 30 | ✅ |
| Frontend — `motorista` | 76 | ✅ |
| Frontend — `rastreamento` | 17 | ✅ |
| **Total** | **1.352** | **✅** |

`npm run verificar` (lint, tipos, testes e build das três aplicações) sem erro; solução .NET com 0 aviso e
0 erro; formatação verificada.

### Defeitos encontrados e corrigidos durante a fase

| Defeito | Como apareceu | Correção |
|---|---|---|
| Teste de webhook instável na suíte completa | passava isolado e com quatro classes; falhava com 518 | o despachante varre o banco **global por desenho**, e um lote de 50 podia não alcançar a mensagem observada entre centenas de outros testes. O teste passou a despachar até esvaziar a própria organização, em vez de supor que uma rodada basta |
| Importação do MapLibre 6 | `tsc` acusou ausência de export default | a versão 6 exporta nomeadamente; passou a importar `Map` e `GeoJSONSource` |
| Matcher de texto no painel | `getByText` exato não casava | a descrição do alerta divide o item com selo e link — texto quebrado entre nós irmãos exige expressão regular |

## Security Gate 18

| Item | Resultado | Evidência |
|---|:---:|---|
| Nenhum segredo no pacote | ✅ | só variáveis `VITE_` de endereço; nenhuma chave de provedor embutida |
| Autorização | ✅ | o console lê o que a sessão permite; integrações e webhooks só respondem a administrador (a API recusa, não a tela) |
| Sessão | ✅ | token em memória, renovação por cookie; rota protegida leva ao login |
| Tempo real | ✅ | token do hub só na query string do caminho do hub (ADR 0017); queda é anunciada, não disfarçada |
| Dependências | ✅ | duas novas, ambas OSS e justificadas; `npm audit` 0 |
| Custo | ✅ | nenhum provedor de tiles contratado nem embutido como padrão |
| Dados de outra organização | ✅ | o console consome as mesmas rotas já cobertas pelos testes de isolamento do backend |

## Decisões

[ADR 0027](./docs/adr/0027-console-operacional-e-mapa.md).

- **Mapa sem fornecedor obrigatório**: estilo configurável, fundo neutro como padrão.
- **Aviso invalida consulta**; só posição entra direto no mapa.
- **Densidade sobre ornamento**: tabela, linha fina e cor com significado.
- **Painel sem gráfico**: série histórica é da Fase 19.
- **Estado de tela num componente só**, para onze telas errarem igual.

## Pendências conhecidas

| Item | Situação |
|---|---|
| Mapa de fundo | depende de provedor de tiles; a variável existe e a escolha é da Fase 25 |
| Traçado da rota no mapa | exige provedor de rotas real; hoje o simulado não produz geometria |
| Indicadores com série histórica | a Fase 19 entregou o painel do período; comparar períodos lado a lado continua fora de escopo |
| Edição de cadastros pelo console | a API já expõe; a fase pediu leitura e as ações que fechavam pendências |
| CI nunca executada | exige `git push`, não autorizado |

## Commit

`feat: console operacional com mapa, telas de leitura e acoes pendentes (Fase 18)`

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

## Entregável

| Peça | Arquivo |
|---|---|
| Consulta | `src/TorreLogistica.Application/Indicadores/ConsultaDeIndicadores.cs` |
| Endpoint | `src/TorreLogistica.Api/Indicadores/EndpointsDeIndicadores.cs` — `GET /api/indicadores?de=&ate=` |
| Índices | migration `IndicadoresOperacionais` |
| Tela | `apps/operacao/src/paginas/Indicadores.tsx`, rota `/indicadores` |

| Métrica pedida | Onde aparece |
|---|---|
| OTD | `pontualidadeEmPercentual` — cartão *Pontualidade* |
| first-attempt success | `sucessoNaPrimeiraTentativaEmPercentual` |
| atraso médio | `atrasoMedioEmMinutos`, contando só quem passou da janela |
| entregas por motorista | recorte com quantidade e pontualidade de cada um |
| tempo médio por parada | `tempoMedioPorParadaEmMinutos`, da chegada registrada à conclusão |
| ocorrências por motivo | recorte por tipo de ocorrência |
| SLA por cliente | recorte de pontualidade por cliente |
| tempo em rota | `tempoMedioEmRotaEmMinutos`, da saída à conclusão |
| entregas por rota | recorte pela rota que carregava a entrega na conclusão |

Nenhuma dependência nova, nenhum serviço novo, nenhum recurso pago.

## Regras da fase

| Regra do ROADMAP | Como foi cumprida |
|---|---|
| Não criar gráfico sem pergunta operacional clara | cada cartão e cada tabela trazem a pergunta impressa acima — *"Para qual cliente a promessa está sendo quebrada?"*, *"Qual rota concentra o atraso?"*. O único elemento gráfico é a barra proporcional atrás da quantidade, com o número escrito ao lado; nenhuma série temporal, pizza ou medidor |
| Sempre mostrar definição do indicador | `definicao` é campo da resposta da API, não texto do frontend, e aparece escrita embaixo do número — não em tooltip nem em página de ajuda |

## Decisões de cálculo

| Decisão | Por quê |
|---|---|
| Recorte pelo **instante da conclusão** | "como foi a semana" é sobre o que aconteceu nela, não sobre o que entrou |
| Cancelada fora do denominador | cancelamento não é falha de pontualidade; puniria a operação por decisão que não foi dela. Aparece como contagem própria |
| Sem base, valor **nulo** | `0%` e "não houve entrega" são fatos opostos; mostrar o mesmo símbolo faz agir sobre problema inexistente |
| `base` sempre visível | *atraso médio de 75 min* significa uma coisa sobre 2 entregas e outra sobre 200 |
| Teto de 186 dias no período | sem teto, um parâmetro de query string vira varredura da operação inteira |
| Agregação direta, sem projeção materializada | otimizar sem medição criaria dois lugares contando a mesma coisa; o caminho fica aberto se a Fase 22 medir necessidade |

## Testes pedidos

| Pedido | Onde |
|---|---|
| período | `PeriodoRecortaPeloInstanteDaConclusao` (início inclusivo, fim exclusivo) e `PeriodoInvertidoOuLongoDemaisERecusado` (422 antes de tocar no banco) |
| timezone | `MesmoInstanteEmFusosDiferentesDaOMesmoResultado` e `PeriodoDoIndicadorTestes`: o mesmo instante em UTC, Brasília e Lisboa dá o mesmo recorte; a mesma *hora local* em outro fuso, não |
| entrega cancelada | `CanceladaNaoEntraNaContaDePontualidade`: com ela no denominador, a pontualidade cairia de 100% para 50% |
| dados incompletos | `SemBaseOValorEVazioENaoZero`: todos os indicadores nulos com base 0; entrega concluída sem chegada registrada fica fora do tempo por parada, e a base diz isso |
| tenant isolation | `IndicadoresNaoVazamEntreOrganizacoes`: duas operações simultâneas no mesmo banco, números independentes |

## Critério de aceite

> Supervisor consegue responder onde a operação está falhando usando os indicadores.

✅ O teste de aceite monta uma operação com falha conhecida — duas entregas concluídas fora da janela, uma
tentativa frustrada reagendada e concluída numa segunda rota, uma cancelada — e confere que os números
apontam para ela: pontualidade 60% sobre base 5, sucesso na primeira tentativa 80%, atraso médio de 75 min
sobre as 2 que atrasaram, tempo por parada com base 1 (só uma entrega teve chegada registrada), e os
recortes dizendo qual rota, qual cliente e qual motorista. Toda entrega percorreu o fluxo real da API;
nada foi escrito direto no banco.

## Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 695 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 530 | ✅ |
| Frontend — `operacao` | 34 | ✅ |
| Frontend — `motorista` | 76 | ✅ |
| Frontend — `rastreamento` | 17 | ✅ |
| **Total** | **1.374** | **✅** |

`npm run verificar` (lint, tipos, testes e build das três aplicações) sem erro; solução .NET com 0 aviso e
0 erro; formatação verificada.

### Defeitos encontrados e corrigidos durante a fase

| Defeito | Como apareceu | Correção |
|---|---|---|
| Recorte por rota perdia as entregas que deram certo | o teste de aceite esperava duas rotas e encontrou uma | a consulta juntava pela **parada ativa**, apoiada no índice único parcial que garante uma só por entrega. Mas concluir a rota desativa todas as paradas dela — inclusive as das entregas concluídas. Passou a usar a parada que carregava a entrega **no instante da conclusão**: adicionada antes, ainda não removida. Índice novo `ix_paradas_entrega_adicionada_em` para sustentar a busca, já que o índice existente cobre só a parada ativa |
| Nome do motivo da ocorrência não traduzia para SQL | 500 em todos os sete testes | `grupo.Key.ToString()` sobre enum não é expressão que o PostgreSQL saiba montar; a conversão passou a acontecer depois da materialização |
| Diferença de instantes escrita como no SQL Server | `EF.Functions.DateDiffMinute` não existe no Npgsql | subtração de instantes, que o provedor traduz para intervalo |

## Security Gate 19

| Item | Resultado | Evidência |
|---|:---:|---|
| Autorização | ✅ | `operacao:leitura`; matriz de autorização cobre a rota nova, e o teste que enumera as rotas exige política declarada |
| Isolamento entre organizações | ✅ | `IndicadoresNaoVazamEntreOrganizacoes`, com duas operações simultâneas no mesmo banco |
| Exposição de dados | ✅ | a resposta é só agregado: nenhum endereço, coordenada, telefone ou identificador de destinatário; nem a lista de entregas por trás do número |
| Abuso por parâmetro | ✅ | período validado e limitado a 186 dias antes de qualquer consulta; `periodo_invalido` e `periodo_grande_demais` com 422 |
| Segredos | ✅ | nada novo; varredura sem achado |
| Custo | ✅ | nenhum serviço, dependência ou recurso pago |
| Desempenho | ✅ | três índices dedicados, dois deles parciais; agregação no banco, sem varredura por linha em C# |

## Decisões

[ADR 0028](./docs/adr/0028-indicadores-operacionais.md).

- **Agregação direta com índices dedicados**, sem projeção materializada — otimizar sem medição criaria duas verdades.
- **Definição viaja com o número**, no corpo da API, e aparece na tela embaixo dele.
- **Vazio não vira zero**: sem base, valor nulo.
- **Cancelada fora do denominador**, com contagem própria.
- **Rota da entrega é a do instante da conclusão**, não a parada ativa.

## Pendências conhecidas

| Item | Situação |
|---|---|
| Série histórica dos indicadores | o painel responde "como foi o período"; comparar períodos lado a lado exige duas chamadas e uma decisão de UX que a fase não pediu |
| Tempo por parada de entrega reagendada | a entrega guarda uma `ChegadaRegistradaEm` só; se houve chegada na tentativa frustrada, o tempo por parada conta daquela chegada até a conclusão de dias depois. Corrigir exige guardar a chegada por tentativa, mudança no agregado que a fase não pediu — e que é da alçada do domínio, não do indicador |
| Exportar para CSV | não pedido pela fase |
| Limiar de meta por indicador | pintar "abaixo da meta" exige meta configurável por organização — decisão de produto, não de cálculo |
| Recalibrar velocidade e sinuosidade da previsão | pendência aberta na Fase 9 e apontada para cá; continua aberta: recalibrar exige série de dados reais de operação, que o simulado não produz |
| CI nunca executada | exige `git push`, não autorizado |

## Commit

`feat: indicadores operacionais com definicao junto do numero (Fase 19)`

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

## O que esta fase construiu

A maior parte do escopo desta fase já existia — cada fase anterior tinha o próprio Security Gate, e é por
isso que o hardening não virou uma reescrita no fim. O que faltava, e foi feito aqui:

| Entregável | Arquivo |
|---|---|
| Política de retenção de GPS, configurável | `src/TorreLogistica.Application/Retencao/LimpezaPorRetencao.cs` |
| Limpeza em segundo plano | `src/TorreLogistica.Infrastructure/Retencao/ProcessamentoDeRetencao.cs` |
| Exclusão em lote e índice de recebimento | migration `RetencaoDeLocalizacao` |
| Prova de que segredo e coordenada não entram em log | `tests/TorreLogistica.IntegrationTests/RedacaoDeLogTestes.cs` |
| **Documento do modelo de segurança** | `docs/security-model.md` |

Nenhuma dependência nova, nenhum serviço novo, nenhum recurso pago.

## Escopo: onde cada item está provado

| Item | Situação | Onde |
|---|:---:|---|
| RBAC completo | ✅ | `AutorizacaoTestes`: matriz perfil × rota conferida contra o roteamento real |
| Tenant isolation | ✅ | `IsolamentoEntreTenantsTestes` + caso próprio em cada fase |
| CSRF | ✅ | cookie `SameSite` restrito e `Origin` exigido na renovação |
| CORS | ✅ | `SegurancaDeBordaTestes`: origem desconhecida não passa nem no preflight |
| CSP · HSTS · nosniff · framing · referrer | ✅ | `MiddlewareDeCabecalhosDeSeguranca`, cobertos por teste parametrizado |
| Rate limiting | ✅ | seis políticas por superfície; telemetria não usa a política de login |
| Upload security | ✅ | tipo por lista fechada, extensão derivada do tipo, teto de 5 MB, chave do servidor |
| Signed URLs | ✅ | URL de curta duração, 403 uniforme para ausente, inválida e expirada |
| Tracking tokens | ✅ | token forte, banco guarda só o SHA-256, resposta neutra |
| Secret hygiene | ✅ | hook local + varredura do histórico completo na CI |
| **Log redaction** | ✅ **novo** | `RedacaoDeLogTestes`: token público, chave de integração, segredo de webhook, sessão e coordenada |
| Session hardening | ✅ | token curto, renovação rotativa, detecção de reuso, revogação de família |
| Webhook signature | ✅ | HMAC-SHA256 com carimbo e tolerância de 5 min; segredo cifrado em repouso |
| Integration credentials | ✅ | chave em duas partes, conferida em tempo constante, mostrada uma vez |
| Brute force | ✅ | 5 falhas → bloqueio de 15 min; resposta e custo de hash iguais para conta inexistente |

## Privacidade de localização

As sete perguntas da fase estão respondidas na seção 7 de `docs/security-model.md`:

| Pergunta | Resposta |
|---|---|
| Quando pode coletar | motorista autenticado, primeiro plano, rota em andamento |
| Retenção | 30 dias no histórico bruto, configurável, com piso de 1 dia |
| Finalidade | operar a entrega em curso — mapa, geofence, previsão, motorista offline |
| Acesso | posição atual na leitura; histórico só na gestão, por período limitado |
| Minimização | coordenada, precisão, tempos e sequência; sem bateria, rede ou identificador de aparelho |
| Fora da jornada | não há coleta em segundo plano; sem rota, a posição não é associada |
| Localização pública aproximada | grade de 0,01° (≈1,1 km), só a caminho, nunca com captura acima de 15 min |

## Retenção

| Chave | Padrão |
|---|---|
| `Torre:Retencao:PosicoesBrutas` | 30 dias (piso de 1 dia) |
| `Torre:Retencao:Intervalo` | 6 horas |
| `Torre:Retencao:TamanhoDoLote` | 5.000 |
| `Torre:Retencao:LotesPorRodada` | 20 |
| `Torre:Retencao:LimparEmSegundoPlano` | ligado |

Métrica `retention.positions.deleted`. Decisões em [ADR 0029](./docs/adr/0029-retencao-de-localizacao.md).

## Critério de aceite

> Produzir documento `docs/security-model.md`.

✅ Produzido, com quinze seções: ativos e adversários, autoridades, isolamento, sessão, borda HTTP,
limites, localização (coleta, finalidade, minimização, acesso, retenção), rastreamento público,
comprovantes, integrações e webhooks, segredos, logs, auditoria, **o que ainda não está coberto** e os
princípios que decidem os casos novos. Cada seção termina apontando o teste que a sustenta — afirmação de
segurança sem teste com nome se desfaz na primeira refatoração.

## Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 695 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 536 | ✅ |
| Frontend — `operacao` | 34 | ✅ |
| Frontend — `motorista` | 76 | ✅ |
| Frontend — `rastreamento` | 17 | ✅ |
| **Total** | **1.380** | **✅** |

`npm run verificar` sem erro; solução .NET com 0 aviso e 0 erro; formatação verificada.

### Defeitos encontrados e corrigidos durante a fase

| Defeito | Como apareceu | Correção |
|---|---|---|
| **Opção de desligar serviço de fundo não tinha efeito** | a limpeza apagou as posições de um teste que a dava por desligada | o registro decidia ligar o `BackgroundService` lendo a configuração **no momento do registro**, quando a configuração do host ainda não está completa: a chave chegava depois e era ignorada em silêncio. A decisão passou para o início do `ExecuteAsync`, via `IOptions`. O mesmo padrão estava no processador de webhooks desde a Fase 17 — `Torre:Webhooks:ProcessarEmSegundoPlano` também era inócuo — e foi corrigido junto |

## Security Gate 20

| Item | Resultado | Evidência |
|---|:---:|---|
| Retenção não apaga o que sustenta a operação | ✅ | posição atual e eventos operacionais sobrevivem ao expurgo, com teste |
| Retenção não depende do relógio do cliente | ✅ | corte pelo recebimento, carimbado pelo servidor |
| Configuração errada não vira perda de dado | ✅ | piso de 1 dia, com teste |
| Limpeza não segura o banco | ✅ | lote de 5.000 com teto por rodada e aviso quando sobra |
| Segredo não entra em log | ✅ | `RedacaoDeLogTestes`, sobre os cinco fluxos que emitem segredo |
| Localização não entra em log | ✅ | coordenada ausente do texto registrado, com teste |
| Serviço de fundo desligável de verdade | ✅ | defeito acima, corrigido e usado pela própria suíte |
| Nenhum controle foi afrouxado | ✅ | nenhuma política, gate ou asserção de fase anterior removida |
| Custo | ✅ | nada novo |

## Decisões

[ADR 0029](./docs/adr/0029-retencao-de-localizacao.md).

- **Prazo de retenção é configuração**, com piso que protege a operação em curso.
- **Corte pelo recebimento**, não pela captura: prazo de guarda não é decisão do aparelho.
- **Só o rastro vence**; posição atual e evento operacional ficam.
- **Limpeza em lotes com teto**, e aviso quando a rodada não dá conta.
- **Serviço de fundo lê a própria opção em tempo de execução**, não no registro.

## Pendências conhecidas

| Item | Situação |
|---|---|
| CSP das aplicações web | depende de como forem servidas; Fase 25 |
| TLS, WAF e cabeçalhos da hospedagem | Fase 25 |
| Retenção de comprovantes | o arquivo vive enquanto a entrega existir; prazo próprio é decisão de produto |
| Retenção de requisições de integração | apagar a marca de idempotência reabre a porta para duplicata; a janela de garantia é decisão de produto |
| Rotação da chave de criptografia de webhook | hoje é troca manual com reescrita dos segredos |
| Inspeção do conteúdo do arquivo enviado | o tipo é conferido na autorização; o conteúdo real não é lido depois do upload direto |
| Pentest | Fase 24 |
| CI nunca executada | exige `git push`, não autorizado |

## Commit

`feat: retencao de localizacao e modelo de seguranca documentado (Fase 20)`

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

## Entregável

| Peça | Arquivo |
|---|---|
| Configuração de traces e métricas | `src/TorreLogistica.Api/Observabilidade/ConfiguracaoDeObservabilidade.cs` |
| Fonte de rastro do trabalho de fundo | `src/TorreLogistica.Application/Observabilidade/RastroDaOperacao.cs` |
| Medidas de estado e leitura do banco | `src/TorreLogistica.Application/Observabilidade/MedidasDaOperacao.cs` |
| Medição periódica | `src/TorreLogistica.Infrastructure/Observabilidade/ProcessamentoDeMedidas.cs` |
| Rastro atravessando a fila | coluna `rastro` no outbox e na entrega de webhook — migration `RastroNoOutbox` |

## OpenTelemetry

| Pedido | Como está instrumentado |
|---|---|
| HTTP | `AddAspNetCoreInstrumentation` (sonda de saúde filtrada) e `AddHttpClientInstrumentation` |
| DB | fonte `Npgsql`, publicada pelo próprio provedor desde a versão 7 — sem pacote extra e sem envelopar o driver |
| Workers | `outbox.despacho`, `webhook.entrega`, `retencao.limpeza`, pela fonte `TorreLogistica.Operacao` |
| SQS | não se aplica: a fila é o PostgreSQL (ADR 0026), e ela é justamente o trecho que o `traceparent` atravessa |
| SignalR | `signalr.connections` conta conexões abertas; o rastro por mensagem de hub fica de fora — ver pendências |
| Provider de rota | coberto por `AddHttpClientInstrumentation`, já que o provedor é chamado por HTTP |
| Webhooks | `webhook.entrega`, com tipo, status e erro, ligado ao rastro de origem |

Exportação por **OTLP**, protocolo aberto. Sem `Torre__Observabilidade__EnderecoOtlp` configurado, nada sai
do processo: os instrumentos funcionam e ninguém precisa subir coletor para rodar o projeto.

Amostragem configurável (`ProporcaoDeAmostragem`, padrão 1), com `ParentBasedSampler`: um rastro que
começou amostrado continua inteiro, sem buraco no meio.

## Correlation ID

```text
HTTP → domínio → outbox → processador → webhook
```

O `traceparent` do W3C é gravado **na mesma transação do fato**, na coluna `rastro` da mensagem do outbox,
e herdado pela entrega de webhook. É o que permite sair do identificador que o cliente tem em mãos e chegar
ao `POST` que falhou, dez minutos e um processo depois.

`TraceId` e `SpanId` entram no escopo de log ao lado do `IdDeCorrelacao`: log e rastro se apontam.

## Métricas

Nomes seguem a convenção do OpenTelemetry (ponto); o exportador converte para o estilo do destino.

| Pedida no ROADMAP | Instrumento | Situação |
|---|---|---|
| `tracking_positions_received_total` | `tracking.positions.received` | já existia (Fase 7) |
| `tracking_positions_duplicate_total` | `tracking.positions.duplicate` | já existia |
| `tracking_positions_out_of_order_total` | `tracking.positions.out_of_order` | já existia |
| `tracking_positions_rejected_total` | `tracking.positions.rejected` | já existia, com o motivo como atributo |
| `eta_calculation_duration` | `eta.calculation.duration` | já existia (Fase 9) |
| `drivers_online` | `drivers.online` | **novo** |
| `drivers_offline` | `drivers.offline` | **novo** |
| `deliveries_in_route` | `deliveries.in_route` | **novo** |
| `deliveries_at_risk` | `deliveries.at_risk` | **novo** |
| `deliveries_late` | `deliveries.late` | **novo** |
| `signalr_connections` | `signalr.connections` | **novo** |
| `outbox_pending` | `outbox.pending` | **novo** |
| `webhook_failures` | `webhook.failures` | **novo** |

As sete novas são medidas de **situação**, não de evento: respondem "quantos agora". Elas leem um retrato
em memória, atualizado a cada 30 segundos por um serviço de fundo — consultar o banco no instante da
raspagem ligaria a carga do sistema à configuração de quem observa.

## Logs

Já eram estruturados desde a Fase 1. Esta fase acrescentou o `TraceId` ao escopo e manteve a regra da Fase
20: token, URL assinada, segredo e coordenada não entram em log, com `RedacaoDeLogTestes` como guarda.

## Critério de aceite

> Uma entrega problemática deve ser rastreável pelos logs/traces sem abrir o banco manualmente.

✅ `EntregaProblematicaEhRastreavelDoInicioAoWebhookQueFalhou` reproduz o caso difícil — o cliente diz que
não foi avisado e, do lado de dentro, "a mensagem saiu". O motorista conclui a entrega com um `traceparent`
conhecido; o assinante responde 500. Partindo só desse identificador, o teste encontra: a requisição de
conclusão, o comando SQL que a gravou, e o `POST` ao assinante com status 500 e situação de erro — tudo no
mesmo rastro, com a fila do outbox no meio. O `TraceId` também aparece no log, fechando a ponte nos dois
sentidos.

## Dependências novas

| Pacote | Justificativa (regra 71) |
|---|---|
| `OpenTelemetry.Extensions.Hosting` | integra o SDK ao host; a alternativa é montar provider e exportador à mão |
| `OpenTelemetry.Instrumentation.AspNetCore` | rastro de requisição com semântica padronizada |
| `OpenTelemetry.Instrumentation.Http` | rastro das chamadas de saída — provedor de rotas e webhooks |
| `OpenTelemetry.Instrumentation.Runtime` | GC, threads e memória, que a Fase 22 vai precisar medir |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | protocolo aberto; nenhum SDK de fornecedor entra no código |

Todas OSS, mantidas pela CNCF, versão 1.19.0 fixada centralmente. Nenhum serviço pago. O comando SQL não
precisou de pacote: o Npgsql publica a própria fonte.

## Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 695 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 539 | ✅ |
| Frontend — `operacao` | 34 | ✅ |
| Frontend — `motorista` | 76 | ✅ |
| Frontend — `rastreamento` | 17 | ✅ |
| **Total** | **1.383** | **✅** |

`npm run verificar` sem erro; solução .NET com 0 aviso e 0 erro; formatação verificada.

### Defeitos encontrados e corrigidos durante a fase

| Defeito | Como apareceu | Correção |
|---|---|---|
| Duas suposições erradas no próprio teste | o assinante "quebrado" aparecia como tendo recebido, e o span do webhook vinha em dobro | o assinante registra o que chega mesmo respondendo 500 — recusa não é ausência —, e a rota iniciada no cenário também gera aviso. As asserções passaram a falar de status de erro e a recortar pelo tipo do evento, em vez de supor silêncio e evento único |

## Security Gate 21

| Item | Resultado | Evidência |
|---|:---:|---|
| Telemetria não carrega segredo | ✅ | os atributos dos spans são tipo, status e identificador; nenhum corpo, token ou URL assinada |
| Telemetria não carrega localização | ✅ | nenhuma tag de coordenada; `RedacaoDeLogTestes` continua verde |
| Nada sai do processo sem decisão explícita | ✅ | sem endereço OTLP configurado, não há exportação |
| Nenhum fornecedor embutido | ✅ | só OTLP; nenhum SDK proprietário, nenhuma chave |
| A coluna nova não é dado pessoal | ✅ | `rastro` guarda `traceparent`, que é identificador de diagnóstico sem conteúdo |
| Sonda de saúde fora do rastro | ✅ | filtro explícito, para não afogar o que importa |
| Dependências | ✅ | cinco pacotes OSS da CNCF, versão fixada; `npm audit` intocado |
| Custo | ✅ | nenhum serviço contratado; coletor é decisão da Fase 25 |

## Decisões

[ADR 0030](./docs/adr/0030-observabilidade.md).

- **OTLP e nada de fornecedor embutido**; sem endereço, nada sai do processo.
- **O rastro atravessa a fila dentro da mensagem**, gravado na transação do fato.
- **O despachante tem rastro próprio**: pendurar o lote numa das origens seria mentira.
- **Medidas de estado são fotografadas**, não consultadas na raspagem; falha mantém o retrato anterior.
- **Log e rastro se apontam**, pelo `TraceId` no escopo.

## Pendências conhecidas

| Item | Situação |
|---|---|
| Coletor e destino da telemetria | decisão de infraestrutura, Fase 25 |
| Rastro por mensagem de hub SignalR | a conexão é contada; instrumentar cada aviso exige envelopar o publicador, e o ganho não apareceu ainda |
| Exportação de logs por OTLP | o Serilog continua sendo o pipeline de log; unificar não ajuda o critério de aceite |
| Painel pronto (dashboard) | depende do coletor escolhido |
| Alerta sobre métrica | monitoramento externo, fora do escopo do projeto |
| CI nunca executada | exige `git push`, não autorizado |

## Commit

`feat: observabilidade com rastro que atravessa a fila do outbox (Fase 21)`

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

## Entregável

| Peça | Arquivo |
|---|---|
| Benchmark de ingestão e de volume | `tests/TorreLogistica.IntegrationTests/CargaDeIngestaoTestes.cs` |
| Resiliência de banco e de worker | `tests/TorreLogistica.IntegrationTests/ResilienciaTestes.cs` |
| **Números medidos e gargalos** | `docs/performance.md` |

Nenhuma dependência nova, nenhum serviço novo, nenhum índice novo — a medição não pediu nenhum.

## Cenário de carga

Meta: 500 motoristas a uma posição a cada 15 s ≈ **33 posições/s**. A mesma taxa foi produzida por 60
motoristas enviando com mais frequência, o que é **conservador**: concentrar a taxa em menos chaves aumenta
a disputa no `UPSERT` condicional da posição atual, que é onde poderia engasgar.

| Medida | Resultado |
|---|---|
| Vazão sustentada | **32,7 /s** |
| Latência p50 · p95 · p99 | **31,5 ms** · 48,2 ms · **245,3 ms** |
| Posições recusadas | 0 |
| Lista do mapa (p50/p95) | 4,0 ms / 4,4 ms |
| Posição atual (p50/p95) | 2,9 ms / 3,6 ms |

Sob volume de **um dia inteiro** (2.851.200 posições, 1.023 MB):

| Medida | Resultado |
|---|---|
| Histórico de um motorista por período | 0,19 ms, usando `ix_posicoes_motorista_id_capturada_em` |
| Limpeza por retenção (lote de 5.000) | 2,77 ms no plano, **12 ms** no `DELETE` real, usando `ix_posicoes_recebida_em` |
| Ingestão com a tabela cheia | p50 **9,7 ms** · p95 20,4 ms |
| Índices | **528 MB** — 52% do peso da tabela |

**O achado principal:** com a tabela 2.880 vezes maior, a ingestão ficou **três vezes mais rápida** (9,7 ms
contra 31,5 ms). O que custa não é o volume — é a concorrência. Isso muda para onde olhar.

## Testar

| Pedido | Situação |
|---|---|
| ingestão GPS | ✅ medida: 32,7/s sustentados, latência por percentil |
| atualização posição atual | ✅ dentro da mesma requisição medida; `UPSERT` condicional não engasgou com a taxa concentrada |
| geofence | ✅ avaliado na ingestão medida — faz parte do caminho cronometrado |
| SignalR | ⚠️ conexões contadas como métrica (Fase 21); latência de aviso sob muitos consoles **não** medida — ver pendências |
| listagem de mapa | ✅ 4,0 ms p50 sob carga |
| ETA | ✅ instrumentado desde a Fase 9 (`eta.calculation.duration`); o recálculo roda fora da requisição e não entrou no caminho cronometrado |
| outbox | ✅ plano medido; a fila esvazia, e por isso o planejador varre em vez de usar índice |
| banco | ✅ planos com estatísticas atualizadas sob volume de um dia |

## Resiliência

| Pedido | Situação |
|---|---|
| DB momentaneamente indisponível | ✅ **novo**: `ConexaoDerrubadaNaoDerrubaAOperacao` |
| SQS duplicado | ✅ não há SQS (ADR 0026); o equivalente — mensagem repetida — é coberto por `SincronizacaoTestes` |
| worker restart | ✅ **novo**: `QuedaNoMeioDoDespachoNaoPerdeNemDuplicaEvento` |
| provider de rota timeout | ✅ `ProvedorQueNaoRespondeNaoSeguraAIngestaoECaiNaContingencia` (Fase 9) |
| SignalR reconnect | ✅ o console anuncia a queda e continua correto pela API (ADR 0017) |
| storage falha | ⚠️ o armazenamento é disco local; a falha que importa é a do provedor de objeto, que só existe na Fase 25 |
| webhook indisponível | ✅ `AssinanteQuebradoEhRetentadoEDepoisDesisteDeFormaVisivel` (Fase 17) |

## Banco: particionamento **não** implementado

O ROADMAP autoriza particionar `posicoes` apenas se a medição justificar. Ela não justifica: histórico em
0,19 ms e limpeza em 12 ms por lote, com um dia de volume. O custo que a medição **de fato** achou é outro —
528 MB de índice por dia — e particionar não o resolveria, só o distribuiria.

Gatilho registrado para rever: se a limpeza passar a não acompanhar a entrada (o aviso *rodada encerrada no
teto de lotes*), particionar passa a valer, porque aí o expurgo vira `DROP PARTITION`.

## Critério de aceite

> Criar `docs/performance.md` com números reais e gargalos conhecidos.

✅ Criado, com o ambiente declarado (Ryzen 7 5700X3D, 32 GB, PostgreSQL em WSL2 com 7,8 GB), o comando para
reproduzir, os números das duas medições, os planos de consulta com os índices escolhidos, a decisão sobre
particionamento, quatro gargalos conhecidos em ordem de peso e uma seção do que **não** foi medido.

## Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 695 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 541 + 2 sob demanda | ✅ |
| Frontend — `operacao` | 34 | ✅ |
| Frontend — `motorista` | 76 | ✅ |
| Frontend — `rastreamento` | 17 | ✅ |
| **Total** | **1.385** | **✅** |

Os 2 benchmarks aparecem como pulados na suíte padrão — é por desenho, e o motivo está no ADR: benchmark
sob contenção mede errado. Eles rodaram, e os números deste documento vieram deles.

`npm run verificar` sem erro; solução .NET com 0 aviso e 0 erro; formatação verificada.

### Defeitos encontrados e corrigidos durante a fase

| Defeito | Como apareceu | Correção |
|---|---|---|
| **Teste de resiliência derrubava o teste seguinte** | rodando a classe, o segundo teste falhava ao montar o próprio cenário | matar conexões atingia o banco inteiro, e o pool do Npgsql é global por cadeia de conexão: as conexões mortas ficavam para quem viesse depois. A API de teste passou a se identificar por `Application Name`, e a queda agora atinge só as conexões dela |
| Medição enganosa de plano | os primeiros planos mostravam `Seq Scan` em tudo | com mil linhas o planejador varre porque é mais barato, e o número não diz nada sobre escala. A medição passou a semear um dia de volume, distribuído pela frota, com `ANALYZE` antes e plano medido a quente |
| Resumo de plano escondia o índice | o plano do histórico aparecia usando `pk_motoristas` | o resumo descia só pelo primeiro filho, que numa consulta com subconsulta é justamente o ramo errado; passou a percorrer a árvore inteira e listar todos os índices usados |

## Security Gate 22

| Item | Resultado | Evidência |
|---|:---:|---|
| O benchmark não afrouxa política | ✅ reprova se qualquer posição for recusada; nenhum limite foi elevado para a medição passar |
| Semeadura não burla o domínio | ✅ o SQL semeia só histórico de GPS, que é dado derivado; toda posição medida entrou pela API real |
| Sem dado pessoal na medição | ✅ coordenadas sintéticas; o relatório não traz identificador de pessoa |
| Teste destrutivo é contido | ✅ a queda atinge só as conexões da API de teste, identificadas por nome |
| Nenhum controle removido | ✅ nenhuma política, gate ou asserção de fase anterior foi alterada |
| Custo | ✅ nada novo; o benchmark roda no contêiner que a suíte já usa |

## Decisões

[ADR 0031](./docs/adr/0031-performance-e-resiliencia.md).

- **Não particionar `posicoes`**, com gatilho registrado para rever.
- **Não retentar `57P01`**: repetir escrita sozinho custa mais que um erro isolado em reinício planejado.
- **Benchmark desligado por padrão**, porque sob contenção ele mede errado.
- **Carga concentrada em menos motoristas**, que é o cenário mais difícil, não o mais fácil.

## Pendências conhecidas

| Item | Situação |
|---|---|
| p99 de 245 ms sob concorrência | gargalo número um; investigar exige as métricas de runtime contra um coletor, o que depende da Fase 25 |
| Índice ocupa metade da tabela | cinco índices, cada um com caso de uso real; cortar exige dado de uso, não opinião |
| Primeira operação após queda do banco falha | limitação aceita e documentada |
| Medição sem latência de rede | aplicação e banco na mesma máquina; o número real sai na Fase 25 |
| SignalR sob carga | exige múltiplos clientes reais; o ganho não justificou o custo nesta fase |
| Storage sob falha | o provedor de objeto só existe a partir da Fase 25 |
| CI nunca executada | exige `git push`, não autorizado |

## Commit

`feat: carga medida, resiliencia provada e decisao de nao particionar (Fase 22)`

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

## Entregável

| Peça | Arquivo |
|---|---|
| Roteiro determinístico | `src/TorreLogistica.Simulator/Cenario/RoteiroDaDemonstracao.cs` |
| Encenação contra a API real | `src/TorreLogistica.Simulator/Cenario/EncenacaoDaDemonstracao.cs` |
| Cliente HTTP da Torre | `src/TorreLogistica.Simulator/Cliente/ClienteDaApiDaTorre.cs` |
| Guia de operação | `docs/operacao/simulador.md` |
| Prova do critério de aceite | `tests/TorreLogistica.IntegrationTests/SimuladorTestes.cs` |

Nenhuma dependência nova, nenhum serviço novo, nenhum endpoint novo na API — o simulador usa os que já
existem, autenticado, como qualquer integrador.

## Simulator: projeto separado, sem acesso ao banco

O projeto continua sem referenciar Domain, Application ou Infrastructure, e o teste de arquitetura continua
guardando isso. Tudo acontece por HTTP: login, cadastros, rota, posições, chegada, conclusão, comprovante.

## Cenários obrigatórios

| # | História | Desfecho | Quem decide |
|---|---|---|---|
| A | Operação normal | `Entregue`, com chegada registrada pelo motorista | o motorista |
| B | Risco de atraso | motorista quase parado; a folga encolhe | **o servidor**, na reavaliação periódica |
| C | Motorista offline | uma posição e silêncio | **o servidor**, no motor de alertas |
| D | Tentativa frustrada | `TentativaFrustrada`, com motivo tipado | o motorista |
| E | Entrada no geofence | `ProximaDoDestino` **sem ninguém chamar chegada** | **o servidor**, na ingestão |
| F | Prova de entrega | `Entregue`, com quem recebeu, onde e quando | o motorista |

As três do servidor **não são encenadas**: o simulador cria a situação e espera. Se ele escrevesse o
alerta, a demonstração provaria que o simulador sabe escrever alerta.

## Determinismo

A semente decide a narrativa — destinos, ruas, bairros, ordem, janelas, distâncias. O que **não** se repete
são os campos que o sistema exige únicos (e-mail, placa, CNPJ), que carregam um carimbo da execução:
repetir a identidade faria a segunda encenação esbarrar na unicidade que o próprio sistema garante.

## Modo acelerado

`MultiplicadorDeTempo` comprime a espera **e a promessa**: a janela prometida é criada na mesma escala.
Comprimir só a espera faria toda entrega nascer atrasada já no primeiro quadro.

O multiplicador não alcança os temporizadores do servidor (reavaliação de previsão, limiar de offline) —
eles são configuração do ambiente, e o guia do simulador diz quais ajustar para ver as histórias B e C numa
apresentação curta.

## Reset

Não existe reset destrutivo, e isso é decisão. Cada execução monta o próprio palco; "reiniciar" é encenar
de novo. Apagar o palco anterior exigiria remover linhas de tabelas **somente-inserção** por desenho —
timeline, ocorrências, comprovantes, auditoria — e desligar esses gatilhos para uma demonstração seria
trocar uma garantia de verdade por uma conveniência. Limpar o acúmulo é assunto da Fase 24.

## Critério de aceite

> Resetar e reproduzir deve levar ao mesmo storytelling.

✅ `MesmaSementeContaAMesmaHistoria` encena duas vezes contra a API real e compara a narrativa inteira:
história, título, estado final e sequência de eventos de cada uma das seis entregas.

## Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 695 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `TorreLogistica.IntegrationTests` (PostgreSQL + PostGIS real) | 544 + 2 sob demanda | ✅ |
| Frontend — `operacao` | 34 | ✅ |
| Frontend — `motorista` | 76 | ✅ |
| Frontend — `rastreamento` | 17 | ✅ |
| **Total** | **1.388** | **✅** |

`npm run verificar` sem erro; solução .NET com 0 aviso e 0 erro; formatação verificada.

### Defeitos encontrados e corrigidos durante a fase

| Defeito | Como apareceu | Correção |
|---|---|---|
| **As histórias A e E eram a mesma coisa** | o teste esperava chegada registrada e encontrou proximidade detectada | o roteiro aproximava o motorista para dentro dos 300 m do geofence, e o sistema decidia sozinho antes de o motorista registrar chegada. A, D e F passaram a parar **fora** do raio: ali quem diz "cheguei" é o motorista, e a diferença entre "o app registrou" e "o sistema percebeu" fica visível |
| Janela prometida terminava antes de começar | 422 `janela_invalida` na criação das entregas | o multiplicador comprimia o fim da janela mas não o início; com 600× o fim caía antes do começo fixo de um minuto |
| Saída planejada nascia no passado | 422 `saida_no_passado` | `agora` era lido no início do preparo, e montar doze cadastros consumia a folga inteira; os instantes passaram a ser lidos no momento de cada chamada |
| Login do simulador recusado | 403 `origem_nao_autorizada` | a defesa de CSRF da Fase 3 funcionando. O simulador passou a **declarar** a própria origem, configurável — quem opera decide se ela entra na lista, em vez de o servidor abrir exceção |
| Estado final vinha do último evento | a história E terminava em `ProximidadeDetectada` em vez de `ProximaDoDestino` | timeline e status são coisas diferentes: uma conta o que aconteceu, o outro diz onde a entrega parou. O relato passou a consultar a entrega |

## Security Gate 23

| Item | Resultado | Evidência |
|---|:---:|---|
| Sem acesso ao banco | ✅ o projeto não referencia a persistência; teste de arquitetura guarda |
| Sem atalho no domínio | ✅ toda mudança de estado passa pela máquina de estados, autenticada |
| Defesa de CSRF preservada | ✅ o simulador declara origem; nenhuma exceção foi aberta no servidor |
| Senha fora do repositório | ✅ `Senha` vazia por padrão, e o simulador recusa começar sem ela |
| Dados fictícios | ✅ nomes, endereços e documentos gerados; nenhum dado de pessoa real |
| Poder do simulador | ✅ ele usa contas existentes; não cria organização nem administrador |
| Custo | ✅ nada novo |

## Decisões

[ADR 0032](./docs/adr/0032-roteiro-da-demonstracao.md).

- **Semente para a narrativa, carimbo para a identidade.**
- **O servidor é o protagonista**: risco, offline e geofence nascem nele, e o simulador só cria a situação.
- **A, D e F param fora do raio do geofence**, para não virarem a história E.
- **O multiplicador comprime a espera e a promessa**, senão a demonstração conta história falsa.
- **O simulador declara origem** em vez de o servidor abrir exceção.
- **Não existe reset destrutivo**, porque as tabelas centrais são somente-inserção por desenho.

## Pendências conhecidas

| Item | Situação |
|---|---|
| Acúmulo de encenações no ambiente de demonstração | limpar é assunto do modo demonstração (Fase 24); a saída provável é desativar a organização, não apagar linha |
| Histórias B e C em teste automatizado | dependem de temporizadores reais do servidor; o teste afirma que foram encenadas, e o resto está no guia |
| Comprovante com foto de verdade | o roteiro conclui com comprovante sem arquivo; subir imagem exigiria storage configurado, que é da Fase 25 |
| Rastreamento público na narrativa | o link é emitido pela API, mas o roteiro ainda não o exibe como parte da história |
| CI nunca executada | exige `git push`, não autorizado |

## Commit

`feat: simulador encena seis historias contra a api real, com roteiro reproduzivel (Fase 23)`

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

## Entregável

| Peça | Arquivo |
|---|---|
| Porta da demonstração | `src/TorreLogistica.Api/Demonstracao/EndpointsDeDemonstracao.cs` |
| Botão e oferta no console | `apps/operacao/src/paginas/Entrar.tsx`, `apps/operacao/src/infra/sessao.ts` |
| Plano das sete capturas | `docs/operacao/screenshots.md` |
| Provas | `tests/TorreLogistica.IntegrationTests/DemonstracaoTestes.cs` e 4 testes no console |

Nenhuma dependência nova, nenhuma migration, nenhum serviço novo.

## Entrada

> Botão: **Explorar demonstração**. Sem onboarding comercial.

O visitante pede uma sessão e **o servidor faz o login por ele** — a senha existe só no ambiente, nunca no
repositório, no pacote publicado ou na resposta da API. O que ele recebe é uma sessão comum: mesmo token
curto, mesmo cookie rotativo, mesmas regras de expiração. Nada de caminho paralelo de autenticação, que
seria uma segunda implementação de segurança com metade dos testes.

Quem decide se o botão aparece é a **API**, não uma variável de build: o console publicado é o mesmo em
todo lugar, e um botão ligado na compilação apareceria também onde a porta não existe.

## Demo: organização controlada, permissões adequadas

| Proteção | Como |
|---|---|
| Desligada por padrão | `Torre:Demonstracao:Habilitada` começa `false` |
| Ausente quando desligada | `POST /api/demonstracao/sessao` responde **404**, não 403 |
| Não vaza o alvo | desligada, a oferta não diz qual organização seria usada |
| Privilégio mínimo | o servidor **recusa** abrir a sessão se a conta for administradora |
| Origem conhecida | mesmo filtro do login humano; nenhuma exceção aberta |
| Limite de requisições | a mesma política do login |

Privilégio mínimo aqui não é recomendação em documento: o teste entra pela porta e confere que a sessão
abre a operação e recebe **403** em integrações e na criação de conta.

## Pausar · retomar · reiniciar: não implementados

O ROADMAP diz "pode haver". Não há, e a razão é arquitetural: o simulador é processo externo
([ADR 0006](./docs/adr/0006-simulador-externo.md)). Para a API pausá-lo, ela precisaria de um canal de
controle sobre ele, invertendo a dependência que aquela ADR estabeleceu — o simulador é cliente da API,
não subordinado dela.

O que existe no lugar é **encenar de novo**: a mesma semente conta a mesma história (Fase 23).

## Refinos

Medidos antes de mexer, e só onde havia lacuna:

| Item | Situação |
|---|---|
| Empty states, loading, erros | ✅ já existiam: componente `Estado` desde a Fase 18 |
| Reconnect e indicador offline | ✅ já existiam: console (Fase 18) e PWA (Fases 11–13) |
| **Foco visível** | ⚠️ **corrigido**: 0 regras de foco no console e no rastreamento; `:focus-visible` acrescentado |
| **Tabela em tela estreita** | ⚠️ **corrigido**: a tabela densa rola na horizontal dentro da seção, em vez de esconder coluna — esconder tiraria do operador justamente o dado que ele foi buscar |
| Responsividade geral | ✅ o console já colapsava a 900px; os recortes usam `auto-fit` |
| Animação e movimento | ✅ **zero** animações no projeto inteiro, então `prefers-reduced-motion` não tem o que desligar |
| `lang` e título | ✅ `pt-BR` e título próprio nas três aplicações |
| Consistência visual | ✅ variáveis de cor compartilhadas; a Fase 18 fixou a direção |

## Screenshots planejadas

As sete estão planejadas em [`docs/operacao/screenshots.md`](./docs/operacao/screenshots.md), com o que
precisa estar na tela, como preparar o palco e as regras (sem dado real, sem token na barra, viewport
declarado, estado cheio).

**Não foram capturadas neste commit**: exigem os três frontends e a API no ar simultaneamente com o
simulador encenando, e o ambiente público é decisão da Fase 25. Capturas de um ambiente local meio montado
envelheceriam e divergiriam do que o visitante vê ao clicar no botão — que é pior que não tê-las.

## Critério de aceite

> Produto deve parecer operação real sem depender de explicação externa.

✅ O visitante chega sem credencial, clica em **Explorar demonstração** e cai numa operação acontecendo:
mapa com motoristas e destinos, entregas com previsão explicada, alertas com evidência, indicadores com a
definição ao lado do número. Não há cadastro, não há tour, não há texto pedindo para imaginar. O que falta
para a frase ficar completa é o ambiente público — Fase 25.

## Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 695 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `TorreLogistica.IntegrationTests` — classes desta fase | 4 | ✅ |
| `TorreLogistica.IntegrationTests` — `AutorizacaoTestes` (enumera todas as rotas) | 221 | ✅ |
| `TorreLogistica.IntegrationTests` — suíte completa | **não concluída nesta rodada** | ⚠️ |
| Frontend — `operacao` | 38 | ✅ |
| Frontend — `motorista` | 76 | ✅ |
| Frontend — `rastreamento` | 17 | ✅ |
| **Executado e verde** | **1.053** | **✅** |

`npm run verificar` sem erro; solução .NET com 0 aviso e 0 erro; formatação verificada.

> **A execução completa da suíte de integração ficou pendente.** Ela foi interrompida pelo sistema por
> falta de memória da máquina — não por falha de teste, e nada nela indica defeito. O que a fase mexeu
> está coberto: as quatro provas novas passaram, e `AutorizacaoTestes`, que enumera **todas** as rotas e
> reprova qualquer uma sem política declarada, passou com 221. A rodada completa precisa ser refeita antes
> de considerar a regressão verificada.

## Security Gate 24

| Item | Resultado | Evidência |
|---|:---:|---|
| Nenhuma credencial no repositório | ✅ `Senha` vazia no `.env.example`; a porta não abre sem ela |
| A senha não sai do servidor | ✅ a resposta traz sessão, nunca credencial |
| Privilégio mínimo é exigido | ✅ conta administrativa recusada, com teste |
| Porta ausente em ambiente comercial | ✅ desligada por padrão; 404 quando desligada |
| Defesa de CSRF preservada | ✅ mesmo filtro de origem do login |
| Limite de requisições | ✅ política do login aplicada às duas rotas |
| A resposta não ensina o contrato | ✅ desligada, não revela a organização; 404 em vez de 403 |
| Matriz de autorização | ✅ as duas rotas declaradas como anônimas, com o motivo |

## Decisões

[ADR 0033](./docs/adr/0033-entrada-da-demonstracao.md).

- **O servidor faz o login pelo visitante**, e a sessão é comum.
- **Desligada por padrão e ausente quando desligada** — 404, não 403.
- **Conta administrativa é recusada**: erro de configuração vira indisponibilidade, não exposição.
- **Quem decide sobre o botão é a API**, não uma variável de build.
- **Pausar e retomar não existem**: dariam à API controle sobre o simulador, invertendo a ADR 0006.

## Pendências conhecidas

| Item | Situação |
|---|---|
| Capturas de tela | planejadas; exigem ambiente público (Fase 25) |
| Vídeo curto de demonstração | Fase 26, pelo mesmo motivo |
| Acúmulo de encenações | a saída provável é desativar a organização antiga, não apagar linha |
| Auditoria automatizada de acessibilidade | não há ferramenta no projeto; o que foi feito veio de inspeção dirigida, não de varredura |
| Entrada de demonstração na PWA do motorista | o botão hoje é só do console; a PWA exige uma conta de motorista vinculada a uma rota em andamento |
| CI nunca executada | exige `git push`, não autorizado |

## Commit

`feat: entrada de demonstracao com privilegio minimo e refinos de ux (Fase 24)`

---

# FASE 25 — INFRAESTRUTURA E DEPLOY ✅

> **Em andamento.** O gate de arquitetura está cumprido e a infraestrutura está escrita e validada, mas a
> fase **não fecha** enquanto não houver ambiente provisionado e aplicação funcionando no publicado.
> Provisionar exige autorização explícita, que não foi dada.

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

> **Nada foi provisionado.** A autorização desta fase foi para **escrever** a infraestrutura, não para
> aplicá-la. Nenhum recurso existe, nenhuma cobrança começou, nenhum deploy aconteceu.

## Gate de arquitetura

A comparação está em [`docs/cost-model.md`](./docs/cost-model.md), feita com os números medidos na Fase 22 —
não com estimativa.

**A descoberta que decidiu a fase:** *scale-to-zero não serve para este sistema*. O processo que atende
HTTP é o mesmo que despacha o outbox a cada 5 s, reavalia previsão a cada minuto, roda o motor de alertas
e apaga rastro vencido. Um serviço que dorme **para de avaliar SLA e de despachar webhook** — a entrega
que entraria em risco às três da manhã só seria marcada quando alguém abrisse o console. Somado à conexão
persistente do SignalR, que cai junto, o caminho é **baixo custo ocioso com processo sempre vivo**.

| Opção | Resultado |
|---|---|
| AWS Lambda | descartada: conexão persistente não cabe no modelo de invocação — é o que o ROADMAP alerta |
| AWS App Runner | descartada: suporte a WebSocket **não confirmado na documentação oficial** |
| Aurora Serverless v2 | descartada: a capacidade mínima contínua custa mais que uma instância pequena |
| ECS Fargate + ALB | descartada: o balanceador sozinho custa mais que todo o compute do projeto |
| Kubernetes gerenciado | descartada: plano de controle pago e operação que um monólito não pede |
| AWS Lightsail + RDS | viável, preço fixo a favor; perdeu na diversificação de nuvem do portfólio |
| EC2 + RDS | viável, mais barato na fatura; manter SO e TLS é custo que não aparece nela |
| **Azure Container Apps + PostgreSQL Flexible Server** | **escolhida** — decisão de produto, tomada pelo Lucas |

## Banco, storage, secrets, mensageria

| Pedido | Como ficou |
|---|---|
| PostgreSQL + PostGIS gerenciado | Flexible Server 17, com `azure.extensions = POSTGIS` — sem isso o `CREATE EXTENSION` da migration falha |
| Storage objeto privado | Storage Account com `allowBlobPublicAccess: false` e contêiner `None` |
| Secrets | Key Vault com RBAC e identidade gerenciada: ninguém guarda credencial para ler credencial |
| Mensageria | **SQS não se aplica**: a fila é o próprio PostgreSQL (ADR 0026), e a arquitetura final não é AWS |
| Frontends | arquivos estáticos, sem segredo e sem contêiner; o destino é decisão à parte |

## IaC

| Arquivo | O que é |
|---|---|
| `infra/main.bicep` | ambiente, API, banco, storage, cofre e workspace |
| `infra/parametros.exemplo.json` | parâmetros, com a senha **vazia** de propósito |
| `infra/README.md` | como conferir e aplicar, e o que o template deliberadamente não faz |
| `.github/workflows/deploy.yml` | publicação **manual**, com confirmação digitada e autenticação federada |
| `src/TorreLogistica.Api/Dockerfile` | imagem neutra: a mesma sobe em qualquer serviço de contêiner |
| `.dockerignore` | **corrigiu um defeito real** — ver abaixo |

**O Bicep não foi compilado nem validado.** A Azure CLI não está instalada nesta máquina, e afirmar que um
template está correto sem a ferramenta que o valida seria inventar. O primeiro passo de quem aplicar é
`az bicep build`, e está escrito no `infra/README.md`.

## A imagem foi construída e exercitada

Não é template: foi construída e posta para rodar.

| Prova | Resultado |
|---|---|
| Tamanho | **196 MB** (Alpine; o projeto compila com `InvariantGlobalization`, então não carrega ICU) |
| Usuário do processo | **UID 1654**, sem privilégio |
| `GET /health/live` com o banco inacessível | **200** — o processo está vivo, e a sonda não mente |
| `GET /health/ready` com o banco inacessível | **503** — a dependência crítica está fora, e a sonda diz |
| Serviços de fundo sem banco | erram, registram e **não derrubam o processo** |

A última linha é a resiliência da Fase 22 aparecendo onde importa: se o banco demorar a aceitar conexão no
arranque, o contêiner não entra em ciclo de reinício.

## Custo

[`docs/cost-model.md`](./docs/cost-model.md) fixa o que **não** muda — as quantidades medidas:

| Parcela | Driver medido |
|---|---|
| Banco | ~1 GB/dia, ~30 GB em regime pela retenção de 30 dias |
| Compute | 0,5 vCPU e 1 GB atendem 33 req/s com p50 de 10 ms |
| Tráfego | ~17 GB/mês de entrada no pico da meta |
| Objeto | até 5 MB por entrega concluída com foto |

**Não há tabela de preços**, e isso é decisão: preço unitário muda, varia por região e depende de
compromisso de uso. Cravado aqui, estaria errado em poucos meses — e alguém decidiria com base nele sem
reconferir. Com as quantidades acima, a calculadora oficial dá o número do dia.

Observabilidade e filas não aparecem no custo porque não são serviços contratados: a telemetria só sai do
processo quando alguém aponta um coletor (ADR 0030), e a fila é o PostgreSQL (ADR 0026).

## Critérios de aceite

| Pedido | Situação |
|---|---|
| app pública · API pública protegida · realtime · workers · storage · DB · health · logs · HTTPS | **descritos na IaC, não provisionados** — o critério só fecha com o ambiente no ar |
| secrets fora do código | ✅ já verdadeiro: Key Vault na IaC, senha vazia no exemplo, nenhum segredo versionado |

> A fase cumpriu o **gate** — comparar antes de provisionar — e deixou a infraestrutura escrita. O critério
> de aceite completo depende de aplicar, e aplicar exige autorização explícita que não foi dada.

## Execução

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| `TorreLogistica.UnitTests` | 695 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| Imagem da API | construída, subida e exercitada | ✅ |
| `deploy.yml` e parâmetros | sintaxe validada | ✅ |
| `infra/main.bicep` | **não validado** — Azure CLI ausente na máquina | ⚠️ |

Nenhum código de aplicação mudou nesta fase. A suíte de integração completa continua pendente desde a
Fase 24, quando foi interrompida por falta de memória do sistema.

### Defeitos encontrados e corrigidos durante a fase

| Defeito | Como apareceu | Correção |
|---|---|---|
| **A imagem não compilava** | `Unable to find fallback package folder 'C:\Program Files (x86)\...'` dentro do contêiner Linux | o `obj/` da máquina Windows era copiado para dentro da imagem, com os caminhos absolutos dela. Faltava `.dockerignore` — e o erro que aparecia falava de pacote não encontrado, escondendo a causa |
| Compilação reprovava por regra do analisador | `CA1710: rename ExcecaoDeDominio to end in 'Exception'` | o `.editorconfig` não era copiado, e é ele que carrega as regras. Sem ele, a compilação no contêiner reprovava por regras que a solução configurou para não valerem num domínio em português |

## Security Gate 25

| Item | Resultado | Evidência |
|---|:---:|---|
| Nenhum recurso criado | ✅ nada foi provisionado; nenhuma cobrança iniciada |
| Nenhum segredo no repositório | ✅ senha do banco é parâmetro `@secure()` e está vazia no exemplo |
| Segredo não vai para a imagem | ✅ `.dockerignore` exclui `.env` e `appsettings.Development.json` |
| Processo sem privilégio | ✅ UID 1654, verificado no contêiner em execução |
| Storage privado | ✅ `allowBlobPublicAccess: false` e contêiner com acesso `None` |
| TLS obrigatório | ✅ `allowInsecure: false` no ingress; `minimumTlsVersion: TLS1_2` no storage |
| Credencial de deploy | ✅ federada por OIDC; nenhum segredo de longa duração |
| Deploy acidental | ✅ só `workflow_dispatch`, com confirmação digitada |
| Banco exposto | ⚠️ a regra de firewall libera serviços do Azure, o mínimo que funciona sem VNet; rede privada custa mais e fica registrada como pendência |

## Decisões

[ADR 0034](./docs/adr/0034-hospedagem.md).

- **Azure Container Apps com réplica mínima 1** — e o "1" é a decisão, não o serviço.
- **Scale-to-zero recusado**: o sistema trabalha quando ninguém olha.
- **Teto de uma réplica** até existir backplane de SignalR (ADR 0017).
- **Papéis, migrations e frontends ficam fora do template**, cada um por um motivo.
- **Deploy só à mão**, com confirmação digitada e autenticação federada.

## Pendências conhecidas

| Item | Situação |
|---|---|
| Provisionar o ambiente | **exige autorização explícita**; nada foi criado |
| Validar o Bicep | `az bicep build` — a Azure CLI não está nesta máquina |
| Backplane do SignalR | sem ele, o teto continua em uma réplica |
| Rede privada para o banco | hoje a regra libera serviços do Azure; VNet custa mais |
| Destino dos frontends | decisão à parte; são estáticos e sem segredo |
| Medição com latência de rede | os números da Fase 22 não têm rede no meio |
| Capturas e vídeo | dependem do ambiente no ar (Fases 24 e 26) |
| Suíte de integração completa | pendente desde a Fase 24 |
| CI nunca executada | exige `git push`, não autorizado |

## Commit

`feat: gate de arquitetura, imagem testada e infraestrutura escrita sem aplicar (Fase 25)`

---

## Revisão da Fase 25 — separação API / Workers

> A fase permanece 🟨. O que esta revisão entrega é a correção arquitetural exigida e a **validação** da
> infraestrutura. Nenhum recurso foi criado.

### A correção

Até aqui a API hospedava quatro laços de segundo plano: despacho do outbox, reavaliação de previsão (com
o motor de alertas dentro), limpeza por retenção e medição do estado da operação. Isso amarrava as duas
coisas — a borda não podia escalar sem duplicar o trabalho de fundo, e o trabalho de fundo não podia parar
sem derrubar a borda.

A separação é **estrutural, não configuracional**:

| | Antes | Agora |
|---|---|---|
| Registro das dependências | API | API **e** Workers — a API lê o que eles produzem |
| Registro dos laços | API | **só** Workers, via `AdicionarProcessamentoDe…` |

Uma opção de configuração poderia ser ligada por engano. A ausência de uma chamada, não.

Nenhuma classe de domínio, caso de uso ou contrato mudou: o que se moveu foi **onde os laços são
registrados**.

### O acoplamento que decidiu como fazer

A fila de recálculo de previsão é **memória da instância**, e o próprio código já dizia por quê:

> *"um pedido perdido numa queda do processo é coberto pela reavaliação periódica, que relê do banco as
> rotas em andamento. Por isso não há outbox aqui — previsão se recalcula; não é efeito que não pode se
> perder."*

Ou seja: a fila é um atalho de latência dentro do processo que a consome, e quem garante que nenhuma
previsão fica velha é a reavaliação periódica. Por isso a fila foi **junto** com o laço, para os Workers.
Registrada na API, ela acumularia pedidos que ninguém leria — e passaria a avisar sobre descarte de
trabalho que nunca foi dela.

### Retenção: avaliada para Container Apps Job, mantida nos Workers

`Microsoft.App/jobs` com `cron` seria a forma canônica para algo que roda a cada 6 horas. Foi **recusado**:
os Workers já estão sempre vivos por causa do outbox, que roda a cada 5 segundos. Um Job traria uma
terceira imagem, um terceiro recurso e um terceiro caminho de configuração para executar trabalho que o
processo existente faz numa rodada de **12 ms** (medido na Fase 22).

Gatilho para rever, registrado: se o outbox virar fila nativa com escala por evento, os Workers deixam de
precisar estar sempre vivos — e aí a retenção passa a valer como Job.

### Caminho para escalar o SignalR além de uma réplica

Está escrito em `infra/README.md` e na ADR 0034, **sem implementar backplane agora**:

1. ligar um backplane (Azure SignalR Service em modo *Default*, ou Redis);
2. subir `maxReplicas` da API.

Nada além disso precisa mudar — e é consequência direta desta separação: com os laços fora da API,
escalar a borda não multiplica o trabalho de fundo. Antes, subir uma réplica significaria dois
despachantes de outbox e duas reavaliações concorrentes.

A ordem importa: subir a réplica antes do backplane produz um sistema que parece funcionar e mente para
metade dos operadores.

### Evidências da validação

**Ferramenta.** A Azure CLI não está instalada e não há `winget` nesta máquina. Em vez de instalar um MSI
de sistema, foi baixado o **Bicep CLI standalone** (executável único, oficial, gratuito) para o diretório
temporário da sessão: `Bicep CLI version 0.47.16 (3f73e1a234)`.

| Verificação | Resultado |
|---|---|
| `bicep build infra/main.bicep` | **0 erros, 0 avisos** |
| `bicep lint infra/main.bicep` | **sem achados** |
| Recursos no ARM compilado | **12**, listados em `infra/README.md` |
| Parâmetros | 7; `senhaDoBanco` é `securestring` |
| Segredo fixo | **nenhum** — varredura por padrões de senha, chave e credencial |
| Recurso pago criado na validação | **nenhum** — a compilação é local e offline |
| `what-if` | **não executado**: exige assinatura autenticada e grupo de recursos existente |

**Imagens.** As duas foram construídas e postas para rodar contra o banco de desenvolvimento real.

| Prova | API | Workers |
|---|---|---|
| Tamanho | 196 MB | **155 MB** (`runtime`, não `aspnet`: não há servidor HTTP) |
| Usuário | UID 1654 | UID 1654 |
| Portas publicadas | 8080 | **nenhuma** |
| Com banco real | migrations aplicadas; `/health/ready` **200** | vivo, assumindo os laços |
| Sem banco | `/health/live` **200**, `/health/ready` **503** | encerra de propósito (falha rápida) |
| "Falha na rodada" no log sem banco | **0** | é quem as registra |

A última linha é a prova da separação: a imagem anterior, com os laços dentro, enchia o log de
`Falha na rodada de despacho do outbox`. A atual não registra nenhuma.

**Comportamento preservado.** As 5 suítes que dependem dos laços foram executadas **antes e depois** da
mudança, com o mesmo resultado: 33 provas, 1 falha. A falha
(`MotoristaParadoAbreAlertaPelaPermanenciaCalculadaNoPostgis`) foi confirmada como **pré-existente** —
reproduzida no código anterior, guardado com `git stash`.

> A causa suposta aqui — "dependência de ordem" — estava **errada**. O preflight encontrou a causa real
> (estrangulamento de recálculo decidido pelo relógio de processamento) e a corrigiu. Está logo abaixo.

## Preflight da Fase 25

> A fase continua **🟨**. Este preflight fechou tudo o que podia ser fechado sem tocar na conta do Azure:
> suíte completa verde, causa raiz do teste que falhava, região definida, custo com preço do dia e sizing
> medido. **Nada foi provisionado, nenhum login foi feito.**

### 1. Suíte completa — verde

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| Integração (**completa**, PostgreSQL + PostGIS reais) | **550** | ✅ 0 falha, 2 puladas |
| `TorreLogistica.UnitTests` | 695 | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `apps/operacao` · `apps/motorista` · `apps/rastreamento` | 38 · 76 · 17 | ✅ |
| Formatação | `dotnet format --verify-no-changes` | ✅ |
| **Total** | **1.398** | ✅ |

A suíte de integração completa estava pendente desde a Fase 24, quando o sistema a interrompeu por falta
de memória. Rodou inteira agora, em 932 s. As 2 puladas são os benchmarks sob demanda (`TORRE_CARGA=1`),
que continuam desligados por decisão da Fase 22.

### 2. `MotoristaParadoAbreAlertaPelaPermanenciaCalculadaNoPostgis` — causa raiz e correção

A hipótese anterior ("dependência de ordem") estava **errada**. O teste falhava de forma reprodutível, e a
causa foi encontrada instrumentando o motor de alertas e a fila de recálculo.

**O que acontecia.** A última das três posições do teste nunca produzia avaliação nenhuma. A sonda mostrou
o motivo exato: o pedido de recálculo dela era **estrangulado** por
`Torre:Previsao:IntervaloMinimoEntreRecalculosPorPosicao`, que vale 30 s.

```text
posição 2 enviada  → pedido entra na fila         (relógio em t+8 min)
Tempo.Advance(8 min)                              (relógio salta para t+16 min)
pedido da posição 2 é processado                  → registra "último recálculo = t+16 min"
posição 3 enviada  → pedido processado            → t+16 − t+16 = 0 s < 30 s → DESCARTADO
```

O estrangulamento decide pelo relógio **do momento em que processa**, não do momento em que o pedido
nasceu. Em produção isso é inofensivo: o relógio anda sozinho e um pedido processado com um instante de
atraso lê praticamente o mesmo "agora"; o que escapa é recuperado pela reavaliação periódica. Sob relógio
falso, que salta 8 minutos de uma vez enquanto um pedido espera na fila, o pedido antigo é processado já
com o relógio no instante da posição seguinte — e queima a janela de 30 s que ela precisaria.

E, como o relógio só anda quando o teste o manda andar, depois da última posição não havia mais nenhuma
varredura periódica para recuperar. O alerta nunca abria.

**Não é bug de produção.** O estrangulamento é deliberado (posição GPS chega a cada poucos segundos;
recalcular a cada uma não muda decisão e custa consulta ao provedor) e seu prejuízo é limitado pela
reavaliação periódica, que roda a cada minuto.

**Correção — no teste, e sem enfraquecer nada.** `AlertasTestes` passou a desligar o estrangulamento:

```csharp
["Torre:Previsao:IntervaloMinimoEntreRecalculosPorPosicao"] = "00:00:00",
```

Nenhuma asserção mudou, nenhum teste foi pulado ou desabilitado, e nenhuma cobertura se perdeu — não havia
teste algum sobre o estrangulamento (verificado por varredura). O que ele era nesta classe é um confundidor
invisível: decidia o resultado por qual lado da troca de vez da fila o salto do relógio caía. As 11 provas
de `AlertasTestes` passam, e a suíte completa também.

**Melhoria de produção que a investigação rendeu.** O descarte era **silencioso** — foi preciso instrumentar
o código para vê-lo. Passou a registrar em `LogDebug` qual rota teve recálculo dispensado e por qual
intervalo. Trabalho que some sem deixar rastro custa uma investigação inteira quando alguém pergunta por
que a previsão não mudou.

### 3. Região: **East US**

Preços de varejo consultados em **21/09/2026** pela Azure Retail Prices API (pública, sem autenticação).

| Item | East US | Brazil South |
|---|---:|---:|
| PostgreSQL B1ms (hora) | US$ 0,017 | US$ 0,035 (**+106 %**) |
| Armazenamento do banco (GB/mês) | US$ 0,115 | US$ 0,2185 (**+90 %**) |
| Container Apps — vCPU ativo / ocioso / memória | idênticos | idênticos |
| Log Analytics — ingestão (GB) | US$ 2,30 | US$ 4,60 (**+100 %**) |
| Saída acima de 100 GB (GB) | US$ 0,08 | US$ 0,12 |

O compute — maior parcela variável — **custa o mesmo nas duas**. A diferença se concentra no banco, que é
justamente o que roda 730 h/mês. Brazil South sai 45–50 % mais caro. Disponibilidade não desempata: os
quatro serviços existem nas duas. Latência favorece Brazil South (~15 ms contra ~130 ms de São Paulo), mas
o que ela atrasa num ambiente de demonstração é a primeira impressão de quem abre o mapa, não uma decisão
operacional.

Registrado que a escolha vale **para o portfólio**: uma implantação comercial atendendo operação brasileira
inverteria o peso.

### 4. Estimativa mensal (East US, preços de 21/09/2026)

| Componente | A — ociosa | B — moderado | C — teto conservador |
|---|---:|---:|---:|
| PostgreSQL B1ms (730 h) | 12,41 | 12,41 | 12,41 |
| Armazenamento do banco (64 GiB) | 7,36 | 7,36 | 7,36 |
| Backup acima da franquia de 64 GB | 0,00 | 0,00 | 3,42 |
| Container Apps — API + workers | 10,21 | 14,47 | 34,02 |
| Container Apps — requisições | 0,00 | 0,00 | 1,20 |
| Container Registry Basic | 5,07 | 5,07 | 5,07 |
| Blob dos comprovantes | 0,00 | 0,04 | 0,42 |
| Log Analytics | 0,00 | 0,00 | 23,00 |
| Key Vault | 0,06 | 0,60 | 3,00 |
| Saída para internet | 0,00 | 0,00 | 12,00 |
| **Total (US$/mês)** | **35,11** | **39,95** | **101,89** |

Franquias aplicadas: Container Apps (180.000 vCPU·s, 360.000 GiB·s, 2 M requisições), Log Analytics (5 GB
e 31 dias de retenção), backup do PostgreSQL (100 % do storage provisionado), saída (100 GB). Sonda de
saúde não é cobrada.

**Faixa esperada: US$ 35 a US$ 40/mês.** Dominam três parcelas fixas: banco (US$ 19,77), Container Apps
(US$ 10–14) e registro (US$ 5,07). A única que pode explodir é o Log Analytics.

### 5. Sizing da API: reduzido para 0,25 vCPU / 0,5 GiB, com medição

A Fase 22 **não** respondia a pergunta: mediu latência, plano de consulta e crescimento do banco, nunca
CPU nem memória do processo. A medição foi feita agora, com a imagem que iria para o Azure rodando sob o
teto exato do Container App, contra PostGIS real, com o simulador encenando as seis histórias **e** seis
consoles lendo ao mesmo tempo por 100 s.

| | **0,25 vCPU / 0,5 GiB** | 0,5 vCPU / 1 GiB |
|---|---:|---:|
| Requisições / erros | 2.215 / **0** | 2.219 / 0 |
| p50 · p95 · p99 | 8,0 · 51,0 · 102,1 ms | 9,6 · 52,5 · 142,5 ms |
| Memória | 181 MiB (35 % do teto) | 184 MiB (18 % do teto) |
| Demonstração | 101 s, 6/6 histórias | 94 s, 6/6 histórias |
| Arranque até `/health/ready` | 18 s | 9 s |

Mesma memória nos dois, latência indistinguível, 2,8× de folga. O único preço é o arranque dobrar — e com
`minReplicas: 1` isso acontece em deploy, não em visita. **Economia: US$ 5,91 a US$ 19,71/mês** conforme o
cenário; 12 % a 21 % da conta. Gatilhos para voltar atrás estão em `docs/cost-model.md`.

### 6. Defeito de infraestrutura encontrado: faltava o registro

O workflow de deploy publicava em `<registro>.azurecr.io` e o `main.bicep` **não criava registro nenhum** —
a infraestrutura descrita não bastava para o deploy descrito. Corrigido com um Container Registry Basic
(US$ 5,07/mês), sem usuário administrador, com pull por identidade gerenciada. A ordem obrigatória do
primeiro provisionamento (criar → atribuir `AcrPull` → reaplicar) está em `infra/README.md`.

### Os 17 recursos que seriam criados

| # | Tipo | SKU / configuração |
|---|---|---|
| 1 | `OperationalInsights/workspaces` | PerGB2018, retenção 30 dias |
| 2 | `DBforPostgreSQL/flexibleServers` | **Standard_B1ms** (Burstable), PG 17, 64 GiB, backup 7 dias, sem HA |
| 3 | `…/configurations` | `azure.extensions = POSTGIS` |
| 4 | `…/databases` | `torre_logistica`, UTF8 |
| 5 | `…/firewallRules` | serviços do Azure |
| 6 | `Storage/storageAccounts` | **Standard_LRS**, sem acesso público, TLS 1.2 |
| 7-8 | `…/blobServices` e `…/containers` | contêiner `comprovantes`, acesso `None` |
| 9 | `ContainerRegistry/registries` | **Basic**, sem usuário administrador |
| 10 | `KeyVault/vaults` | **standard**, RBAC, soft delete 7 dias |
| 11 | `App/managedEnvironments` | consumo (sem workload profile dedicado) |
| 12 | `App/containerApps` — **API** | 0,25 vCPU / 0,5 GiB, min 1, max 1, ingress HTTPS |
| 13 | `App/containerApps` — **workers** | 0,25 vCPU / 0,5 GiB, min 1, max 1, **sem ingress** |
| 14-17 | `Authorization/roleAssignments` | papéis mínimos: dado no contêiner, delegação na conta, `AcrPull` no registro (ver adiante) |

Validação: `bicep build` e `bicep lint` sem erro nem aviso; `senhaDoBanco` continua `securestring`; nenhum
segredo fixo no template; **nenhum recurso pago criado** — a compilação é local e offline.

### Observações registradas neste preflight

| Achado | Onde apareceu | Destino |
|---|---|---|
| Corpo JSON malformado devolve **500**, não 400 | login com senha contendo retorno de carro durante a medição | **corrigido** logo abaixo |
| Provedor de armazenamento local falha em contêiner | a API tenta criar `/aplicacao/armazenamento` sem permissão | **corrigido** logo abaixo — e o defeito era maior: não existe adaptador de Blob |

### O que falta para a fase fechar (🟨)

| Item | Bloqueio |
|---|---|
| ~~Provisionar os 17 recursos~~ | ✅ **executado** e desfeito por decisão de custo — ver a seção final |
| ~~`az deployment group what-if`~~ | ✅ **executado**: 17 Create, 0 Modify, 0 Delete |
| Ambiente publicado | **redesenho necessário**: a demonstração precisa caber em US$ 0/mês |
| Destino dos frontends | decisão à parte; são estáticos e sem segredo |
| Medição com latência de rede | só no ambiente publicado |

## Correção dos dois defeitos do preflight

> Ainda **🟨**, ainda **nada provisionado**. Estes eram os dois achados que o preflight registrou sem
> corrigir. Ambos tinham a mesma forma: o sistema errava **tarde**, quando errar cedo era possível.

### Defeito 1 — corpo JSON ilegível virava erro do servidor

**Causa raiz, e ela é diferente do que o preflight supôs.** O problema não era só "falta tratamento":
era `RouteHandlerOptions.ThrowOnBadRequest`, que o ASP.NET Core liga **só em Development**. Isso produzia
dois comportamentos para a mesma requisição:

| Ambiente | O que acontecia |
|---|---|
| Development | a falha de leitura virava exceção, caía no manipulador genérico e respondia **500** |
| Testing e Production | não virava exceção; respondia **400**, mas fora do contrato da API — sem `codigo`, sem `type` |

Os dois errados, de formas diferentes — e foi a segunda que o preflight não viu, porque só observou o
contêiner em Development.

**Correção, centralizada em dois pontos e em nenhum endpoint:**

1. `ThrowOnBadRequest = true` em **todo** ambiente, em `Program.cs`. Dev e produção passam a percorrer o
   mesmo caminho.
2. `ManipuladorDeCorpoInvalido`, primeiro da cadeia de `IExceptionHandler`, antes do de domínio e do
   genérico. Responde `400 corpo_invalido` — ou `413 corpo_grande_demais`, respeitando o status que a
   própria exceção carrega.

**A guarda contra o efeito colateral óbvio.** O manipulador trata **só** `BadHttpRequestException`, que a
plataforma lança exclusivamente ao ler a requisição. Um `JsonException` nosso, ao serializar resposta,
continua sendo 500. Há teste para os dois lados: um que exige 400 e outro que exige que a rota de falha
interna continue em 500.

A mensagem da exceção não vai para o corpo — ela nomeia o parâmetro do endpoint, o caminho dentro do JSON
e a posição do byte. Fica no log, com o identificador de correlação, que a resposta preserva.

> O `catch (BadHttpRequestException)` do endpoint de upload de arquivo **fica**: ali a exceção significa
> uma coisa específica — arquivo maior que o autorizado — e devolve `413 arquivo_grande_demais`. Não é
> duplicação do tratamento genérico; é um significado local que vence antes dele.

### Defeito 2 — armazenamento local falhava em uso, não na subida

**Causa raiz, também maior do que o preflight registrou.** Eram três coisas empilhadas:

1. O diretório padrão do adaptador local é uma pasta sob o diretório do processo, que no contêiner
   pertence ao root. O processo roda sem privilégio (UID 1654) e não consegue criar nada ali.
2. O adaptador é um singleton resolvido **sob demanda**, e o `Directory.CreateDirectory` morava no
   construtor dele. A falha só aparecia na primeira entrega com comprovante — em produção, horas depois
   da implantação, para um motorista na rua.
3. E o que o preflight não viu: o Bicep passava `Torre__Armazenamento__Conta` e
   `Torre__Armazenamento__Contedor`, e **nenhuma das duas existe no código**. Não há opção com esse nome
   nem adaptador de Blob — só o local. As variáveis pareciam configuração e não configuravam nada: a
   aplicação usaria disco local no Azure, em silêncio, com a Storage Account vazia para sempre.

**Correção:**

| Peça | O que mudou |
|---|---|
| `Torre:Armazenamento:Provedor` | novo, explícito: `local` ou `blob`. Não se deduz mais |
| `PermitirLocalForaDeDesenvolvimento` | novo. Sem ele, `local` fora de Development/Testing **recusa subir** |
| `DiretorioEfetivo` | o padrão passou a morar nas opções, para validação e adaptador usarem o mesmo caminho |
| `ValidacaoDeOpcoesDeArmazenamento` | roda em `ValidateOnStart`: cria o diretório e **grava uma sonda real** |
| `Provedor=blob` | falha na subida com "ainda não tem adaptador" — nunca cai no local por baixo do pano |
| Dockerfiles (API e workers) | criam `/var/tmp/torre-logistica/armazenamento` como root e o entregam ao `$APP_UID` |
| `main.bicep` | trocou as duas variáveis inexistentes pelas duas reais, com o custo declarado em comentário |

`/var/tmp` e não `/tmp`: o padrão de diretórios do Linux reserva `/var/tmp` para dado temporário que
sobrevive a reinício de processo, e alguns runtimes montam `/tmp` em memória.

### Evidência — contêineres contra PostgreSQL + PostGIS real

Imagens reconstruídas: API **196 MB**, workers **155 MB** (mesmos tamanhos de antes).

| Verificação | Resultado |
|---|---|
| Produção **sem** declarar armazenamento | `OptionsValidationException` na subida, **código de saída 1** |
| Produção com a declaração do Bicep | sobe; `/health/live` **200** e `/health/ready` **200** |
| Diretório no contêiner | `drwxr-xr-x app app /var/tmp/torre-logistica/armazenamento`; escrita OK como `uid=1654(app)` |
| JSON quebrado, chave sem valor, tipo incompatível, corpo vazio | **400 `corpo_invalido`** nos quatro, em Production |
| JSON válido | segue o fluxo: **401 `credenciais_invalidas`**, com `idDeCorrelacao` presente |
| API executa laço de fundo | **0 ocorrências** no log |
| Workers assumiram os laços | `ProcessadorDeWebhooks` e `ServicoDeVerificacaoDeInfraestrutura` ativos |
| Workers têm porta publicada | **nenhuma** (`portas=map[]`) |
| Usuário dos dois processos | UID **1654**, sem privilégio |
| Simulador contra a imagem | 6/6 histórias encenadas, saída 0 |

> O simulador não grava arquivo nenhum no storage — ele envia `arquivos = []` de propósito. Zero
> arquivos no diretório é o comportamento correto dele, não falha de armazenamento. A prova funcional
> do adaptador vem de `ComprovantesTestes`, na suíte de integração, que sobe uma foto de verdade.

### Testes acrescentados

| Onde | Quantos | O que travam |
|---|:---:|---|
| `ContratoDeErrosHttpTestes` | 6 | 4 casos de corpo ilegível (sintaxe, chave sem valor, tipo incompatível, vazio), JSON válido seguindo o fluxo, e falha interna continuando 500 |
| `ArmazenamentoNaSubidaTestes` | 9 | provedor local nos dois ambientes de desenvolvimento, diretório sem permissão, provedor desconhecido, `blob` recusado enquanto não havia adaptador, produção sem e com declaração, caminho efetivo, e as validações antigas continuando válidas |

### Bateria completa depois das duas correções

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| Integração (completa, PostgreSQL + PostGIS reais) | **556** | ✅ 0 falha, 2 puladas, 752 s |
| `TorreLogistica.UnitTests` | **704** | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `apps/operacao` · `apps/motorista` · `apps/rastreamento` | 38 · 76 · 17 | ✅ |
| `dotnet format --verify-no-changes` | — | ✅ |
| `bicep build` · `bicep lint` | — | ✅ 0 erro, 0 aviso, sem achados; 13 recursos |
| **Total** | **1.413** | ✅ |

São 15 provas a mais que o preflight: 6 de contrato de erro e 9 de armazenamento na subida.

### O que continua pendente

| Item | Situação |
|---|---|
| Adaptador de Blob para comprovantes | ✅ **implementado** — ver a seção seguinte |

## Adaptador de Azure Blob Storage para comprovantes

> Ainda **🟨**, ainda **nada provisionado**. Este era o último bloqueio técnico da fase: o ambiente
> publicado não tinha persistência durável de prova de entrega.

### O que existia e o que passou a existir

| | Antes | Agora |
|---|---|---|
| Adaptadores de `IObjectStorage` | um: disco local | **dois**: disco local e Azure Blob |
| Em produção | disco do contêiner — sumia no reinício | Blob privado, com identidade gerenciada |
| `Provedor=blob` | recusava subir: não havia adaptador | sobe, com a configuração conferida na subida |
| Qual provedor subiu | não aparecia em lugar nenhum | anunciado no arranque, sem segredo no texto |

### Arquitetura

```text
aparelho pede autorização
  → API assina SAS de Create+Write para AQUELA chave, válida por minutos
aparelho envia a foto DIRETO ao Storage — a API nunca vê os bytes
API registra o comprovante
  → ObterAsync lê tamanho e tipo REAIS e calcula o SHA-256 baixando o objeto uma vez
  → o resumo vira metadado do próprio objeto; leituras seguintes não baixam nada
console pede o comprovante
  → API assina SAS de Read, nova a cada leitura
```

A fronteira Azure termina em `ArmazenamentoBlobDeObjetos`: nenhum tipo do SDK atravessa `IObjectStorage`,
e o teste de arquitetura passou a proibir pacotes `Azure.*` no domínio.

### Pacotes

| Pacote | Versão | Para quê |
|---|---|---|
| `Azure.Storage.Blobs` | 12.29.2 | o adaptador |
| `Azure.Identity` | 1.21.0 | identidade gerenciada da Container App |
| `Testcontainers.Azurite` | 4.15.0 | emulador oficial na suíte de integração |

Custo: a imagem da API foi de 196 MB para **202 MB**, e a dos workers de 155 MB para **161 MB**.

### Permissões no Azure

| Identidade | Papel | Escopo |
|---|---|---|
| API | `Storage Blob Data Contributor` | **o contêiner** `comprovantes`, não a conta |
| API | `Storage Blob Delegator` | a conta — é operação de conta, não existe por contêiner |
| API | `AcrPull` | o registro |
| Workers | `AcrPull` | o registro |

Os workers **não** recebem papel no Storage: nunca resolvem `IObjectStorage`. Os papéis passaram a viver
no template, o que muda um pré-requisito: quem aplica precisa de `User Access Administrator` ou `Owner` no
grupo de recursos. Em troca, o primeiro provisionamento deixou de ser um procedimento de três passos.

### Como o acesso temporário funciona

Com identidade gerenciada não existe segredo para assinar. O adaptador pede ao Azure uma **chave de
delegação de usuário**, válida por pouco tempo, e assina a SAS com ela. A SAS resultante carrega os
poderes da identidade e nada além disso, e o Azure a invalida quando a delegação vence. A chave é cacheada
em memória e renovada com folga — pedi-la a cada autorização seria uma ida de rede por foto.

No banco fica **só a chave lógica** do objeto. Nenhuma URL, permanente ou não: toda URL nasce na hora da
leitura, depois de a aplicação ter conferido autorização e tenant.

### Três descobertas que mudaram o código

| Descoberta | Como apareceu | O que mudou |
|---|---|---|
| **SAS não prende tipo nem tamanho** | teste esperava `403` ao subir `text/plain` com assinatura de `image/jpeg`; o emulador respondeu `201` | o `ContentType` do construtor é override de resposta de leitura, não regra de escrita. Removido do envio, e o teste passou a travar a verdade: o Storage aceita, o **registro** recusa com `tipo_de_arquivo_nao_aceito` |
| **`BlobSasBuilder` tem versão própria** | a SAS saía com `sv=2026-06-06` mesmo com o cliente fixado em `V2025_07_05` | a versão configurada passou a ser aplicada também na assinatura |
| **Endereço de emulador precisa ser IP** | upload devolvia `403`, e a URL saía sem o segmento do contêiner | com um *hostname*, o SDK usa o estilo de produção e lê o primeiro segmento do caminho como contêiner, não como conta. Só afeta emulador — em produção o endereço é `https://conta.blob.core.windows.net` |

A primeira é a mais importante: o teste **falhou pelo motivo certo** e impediu que uma afirmação falsa
sobre segurança entrasse no código e na documentação.

### Prova ponta a ponta, com reinício

PostgreSQL + PostGIS reais, Azurite, imagem real da API e imagem real dos workers.

```text
1. login na organização semeada
2. cliente, destinatário, entrega, motorista, veículo, rota iniciada
3. autorização de envio  → URL aponta para o Storage, não para a API
4. PUT da foto DIRETO no Storage                      → 201 Created
5. conclusão com o comprovante                        → entrega Entregue
6. leitura pela API                                   → 10.240 bytes, sha256 e96760a8…
7. docker restart do contêiner da API
8. leitura de novo                                    → 10.240 bytes, sha256 e96760a8…
```

| Verificação | Resultado |
|---|---|
| Binário idêntico depois do reinício | **sim**, byte a byte |
| Mesma chave lógica | sim |
| URL reemitida a cada leitura | sim — assinatura e prazo novos |
| Objeto no Storage | 2 blobs listados no contêiner `comprovantes` |
| Arquivos no disco da API | **0** |
| Workers | vivos, sem porta publicada, anunciando o mesmo storage |

A última linha da tabela é o ponto: o arquivo não depende do sistema de arquivos efêmero da API.

### Testes acrescentados — 30

| Onde | Quantos | O que travam |
|---|:---:|---|
| `ArmazenamentoNaSubidaTestes` (unidade) | +13 | seleção do provedor blob, configuração ausente por nome, nome de contêiner inválido, chave de conta recusada em produção, chave ausente, autenticação desconhecida, versão de serviço inválida, endereço derivado da conta, e blob não cobrando diretório local |
| `ComprovantesNoBlobTestes` (Azurite) | 8 | fluxo completo pelo Storage, resumo calculado do objeto e guardado como metadado, leitura sem assinatura recusada, banco sem URL, arquivo inexistente impedindo o registro, isolamento por organização, tipo fora da política, e tipo divergente aceito pelo Storage mas recusado no registro |
| `AdaptadorDeBlobTestes` (Azurite) | 5 | assinatura vencida recusada (relógio no passado, sem esperar), objeto inexistente, metadados reais, assinatura de envio que não serve para ler, e nenhuma URL sem prazo |
| `DependenciasEntreCamadasTestes` | — | passou a proibir pacotes `Azure.*` no domínio |

### O que só o Azure real pode provar

| Item | Por quê |
|---|---|
| Chave de delegação de usuário (`GetUserDelegationKey`) | o emulador não implementa. Os testes assinam com chave de conta, e a escolha entre os dois caminhos é coberta por unidade |
| Suficiência dos papéis | o emulador não avalia RBAC. Que `Storage Blob Data Contributor` no contêiner e `Storage Blob Delegator` na conta bastem é afirmação a conferir no provisionamento |
| `DefaultAzureCredential` resolvendo a identidade da Container App | exige a plataforma real |
| HTTPS obrigatório na SAS | em produção o endereço é HTTPS e a assinatura exige `https`; contra o emulador, que fala HTTP, essa cláusula fica desligada |

### Bateria completa depois do adaptador

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| Integração (completa, PostgreSQL + PostGIS + Azurite reais) | **569** | ✅ 0 falha, 2 puladas, 865 s |
| `TorreLogistica.UnitTests` | **717** | ✅ |
| `TorreLogistica.ArchitectureTests` | 22 | ✅ |
| `apps/operacao` · `apps/motorista` · `apps/rastreamento` | 38 · 76 · 17 | ✅ |
| `dotnet format --verify-no-changes` | — | ✅ |
| `bicep build` · `bicep lint` | — | ✅ 0 erro, 0 aviso, sem achados; **17 recursos** |
| **Total** | **1.439** | ✅ |

São 26 provas a mais que a correção anterior: 13 de unidade e 13 de integração contra o emulador.

## Divisão do composition root: cada processo registra só o que usa

> Ainda **🟨**, ainda **nada provisionado**. Este era o último achado estrutural da fase.

### Causa raiz

`AdicionarCamadaDeApplication()` era um registro **único e indivisível** de cerca de quarenta casos de
uso. O processo de trabalho o chamava inteiro para obter **seis**, e arrastava junto os que dependem de
`IContextoDoUsuario` e `IEmissorDeTokenDeAcesso` — duas abstrações registradas dentro do assembly
`TorreLogistica.Api` (`ConfiguracaoDeAutenticacao` e `Program.cs`), que o processo de trabalho nem
referencia e portanto **não tem como satisfazer**.

O que fazia o sintoma aparecer só em um ambiente:

| Ambiente | `ValidateOnBuild` | O que acontecia |
|---|---|---|
| `Production` | desligado | o processo subia carregando um grafo impossível; só quebraria se alguém resolvesse um daqueles serviços |
| `Development` | **ligado** | o contêiner validava cada descritor na construção e o processo **não subia** |

Ou seja: o defeito estava sempre lá; o que mudava era quem olhava. Por isso os testes novos ligam a
validação **explicitamente**, em vez de depender do ambiente em que a suíte roda.

### Como os registros ficaram separados

A divisão não é organizacional — separa casos de uso por **do que eles dependem**.

| Registro | O que entra | Quem chama |
|---|---|---|
| `AdicionarCasosDeUsoDaOperacao()` | o que funciona sem ninguém autenticado: `RecalculoDePrevisoes`, `ConsultaDeRotasParaReavaliacao`, `MonitoramentoOperacional`, `DespachoDeWebhooks`, `EntregaDeWebhooks`, `LeituraDoEstadoDaOperacao` e as métricas. Depende de persistência, relógio, identificador e opções | **os dois** |
| `AdicionarCasosDeUsoDaBorda()` | os ~34 que alcançam `IContextoDoUsuario` ou `IEmissorDeTokenDeAcesso` — login, cadastro, ingestão, consultas do console | **só a API** |
| `AdicionarCamadaDeApplication()` | as duas metades somadas | **só a API** |
| `AdicionarProcessoDeTrabalho()` | o composition root do processo de fundo: metade de operação + infraestrutura + dependências dos laços + os quatro laços | **só os workers** |

A lista de operação é curta de propósito: cada linha é uma dependência que os laços realmente resolvem.
Acrescentar ali algo que só a borda usa recoloca o problema que a divisão resolveu.

### Um segundo corte que a correção expôs

Ao rodar o contêiner dos workers em `Production`, o processo morreu com
`Torre:Armazenamento:ChaveDeAssinatura é obrigatória`. A causa foi introduzida na correção anterior: o
`AnuncioDoArmazenamento`, criado para dizer na subida qual storage está atendendo, resolvia
`IObjectStorage` — e o registro do storage morava no `AdicionarCamadaDeInfrastructure` compartilhado.

Resultado: o processo de trabalho exigia uma **credencial de assinatura de URL que nunca usaria**, para
um serviço que nunca resolveria. O mesmo defeito, em escala menor.

Corrigido com o mesmo princípio: o storage saiu para `AdicionarArmazenamentoDeObjetos()`, chamado só pela
API. `IObjectStorage` tem exatamente dois usos no sistema — `GestaoDeComprovantes` e
`GestaoDoRastreamentoPublico` —, ambos atrás de requisição autenticada.

Consequência visível no `main.bicep`: a aplicação de workers **não recebe mais variável de armazenamento
nenhuma**. O menor privilégio deixou de ser declaração de intenção e virou consequência de o processo não
conhecer o recurso.

### Composition root testável

O registro dos workers saiu das instruções de nível superior do `Program.cs` para
`ComposicaoDoProcessoDeTrabalho.AdicionarProcessoDeTrabalho()`. O motivo é prático: um composition root
escrito em instruções de nível superior só existe quando o processo sobe, e um teste que o recriasse à mão
estaria testando a cópia — que sai de sincronia no primeiro registro esquecido. Com o registro num método,
o processo e o teste compõem **exatamente o mesmo grafo**.

### Prova: workers em `DOTNET_ENVIRONMENT=Development`

```text
dotnet run --project src/TorreLogistica.Workers
```

| Verificação | Resultado |
|---|---|
| Sobe | **sim** — zero erros de resolução no log |
| Permanece vivo | sim, até o `timeout` do teste encerrar o processo |
| Abre porta | **nenhuma** — `netstat` filtrado pelo PID: 0 em escuta |
| Conecta ao PostgreSQL/PostGIS | sim: *"Banco alcançável; os laços de fundo assumem daqui"* |
| Executa os laços | **sim**: 2 conexões e **22 transações confirmadas em 20 s** |

As 22 transações são a prova positiva que faltava. Os laços são silenciosos quando não há trabalho — o log
não serve de evidência —, mas o contador de transações do PostgreSQL mostra as rodadas acontecendo.

### Contêineres

Imagens reconstruídas: API **202 MB**, workers **161 MB**.

| Verificação | Resultado |
|---|---|
| API em `Production` | `/health/live` **200**, `/health/ready` **200** |
| Workers em `Production`, **sem nenhuma variável de armazenamento** | vivos, `portas=[]` |
| Laços nos workers em contêiner | 2 conexões, **22 transações em 20 s** |
| Workers mencionam armazenamento no log | **0 vezes** — o processo nem sabe que storage existe |
| API anuncia o storage na subida | sim |

### Testes acrescentados — 8

| Onde | Quantos | O que travam |
|---|:---:|---|
| `ComposicaoDoProcessoDeTrabalhoTestes` (arquitetura) | 6 | host real construído com `ValidateOnBuild` + `ValidateScopes` em `Development` **e** `Production`; nada da sessão humana registrado; nenhum tipo do grafo pedindo dependência de requisição no construtor; os seis casos de uso dos laços resolvendo; os cinco serviços hospedados presentes |
| `ComposicaoDaApiTestes` (integração) | 2 | o `Program.cs` real da API subindo em `Development` com validação ligada; e a API **não** hospedando nenhum dos quatro laços |

Os dois testes de "nada da sessão humana" olham ângulos diferentes: um vê o que está **registrado**, o
outro o que os registrados **exigem** no construtor. Juntos fecham os dois caminhos pelos quais uma
dependência de requisição voltaria a entrar no grafo de fundo.

Nenhuma implementação falsa foi registrada para satisfazer o contêiner, e nenhuma validação foi
desligada — era exatamente o que o pedido proibia, e é o que teria mascarado o problema.

### Bateria completa depois da divisão

| Suíte | Provas | Resultado |
|---|:---:|:---:|
| Integração (completa, PostgreSQL + PostGIS + Azurite reais) | **571** | ✅ 0 falha, 2 puladas, 814 s |
| `TorreLogistica.UnitTests` | 717 | ✅ |
| `TorreLogistica.ArchitectureTests` | **28** | ✅ |
| `apps/operacao` · `apps/motorista` · `apps/rastreamento` | 38 · 76 · 17 | ✅ |
| `dotnet format --verify-no-changes` | — | ✅ |
| `bicep build` · `bicep lint` | — | ✅ 0 erro, 0 aviso, sem achados; 17 recursos |
| **Total** | **1.447** | ✅ |

São 8 provas a mais que o adaptador de Blob: 6 de composição do processo de trabalho e 2 da API.

## Provisionamento real no Azure: executado, e desfeito por decisão de custo

> A Fase 25 **continua 🟨**. A arquitetura foi validada contra o Azure real; o ambiente foi destruído por
> decisão explícita de custo, e a estratégia de deploy da demonstração precisa ser redesenhada.

### O que foi executado

Autorização de provisionamento dada, Azure CLI 2.90.0 instalada, autenticada na assinatura
**Azure for Students** (`3a7e3e97-119f-45a4-8367-41a8c2123feb`), papel **Owner** confirmado, 7 providers
registrados, Resource Group criado, `what-if` executado e deployment aplicado.

### Três descobertas que só o Azure real revelou

**1. A região aprovada era impossível — por dois motivos independentes.**

| Restrição | Evidência |
|---|---|
| PostgreSQL Flexible Server | `az postgres flexible-server list-skus --location eastus` → *"Provisioning is restricted in this region"*, 0 edições retornadas |
| Azure Policy da assinatura | `Allowed resource deployment regions` → `listOfAllowedLocations = [canadacentral, eastus, eastus2, southafricanorth, southcentralus]` |

O cruzamento das duas peneiras deixou **duas** regiões viáveis: `canadacentral` e `southafricanorth`.
`westus3` — a primeira alternativa, de preço idêntico a East US — passou no teste do PostgreSQL e foi
barrada pela política, que só apareceu no `what-if`.

Escolhida **Canada Central**: US$ 41,78/mês contra US$ 39,95 do plano original, latência equivalente.

**2. O `what-if` fez o trabalho dele.** Com os 17 recursos aprovados: **17 Create, 0 Modify, 0 Delete**,
SKUs corretos, tudo na região certa. Foi ele que pegou a política de regiões **antes** de existir meio
ambiente provisionado.

**3. O deployment falhou por um limite de plataforma que nenhum teste local poderia prever.**

```
ExpressEnvironmentFeatureNotSupported
'System-assigned managed identity for container registry authentication' is not
supported for container app 'torrelog-api' on express environments.
```

O Azure passou a criar ambientes de Container Apps no modo **express** por padrão. Ambiente express recusa
autenticação no registro por identidade **atribuída pelo sistema** — que é exatamente o desenho aprovado.

Duas hipóteses testadas, uma descartada:

| Hipótese | Teste | Resultado |
|---|---|---|
| O ambiente vira express por não declarar `workloadProfiles` | declarei no Bicep, apaguei o ambiente, reapliquei | **errada** — mesmo erro |
| — | criei ambiente pela CLI com `--enable-workload-profiles true` e comparei o JSON | idêntico; `environmentType` nulo em 4 versões de API |
| O limite é só da identidade *do sistema* | `az containerapp create --registry-identity <identidade-de-usuário>` | **confirmada** — `Succeeded` |

A correção seria trocar identidade do sistema por identidade de usuário. Ela **não foi aplicada**: a
decisão de custo chegou antes.

### O ambiente foi destruído

Decisão explícita: a Torre Logística não pode gerar custo mensal nem consumir crédito da assinatura.

| Recurso criado | Destruído |
|---|---|
| `torrelog-pg` — PostgreSQL 17 Flexible Server, Standard_B1ms, 64 GiB | ✅ |
| `torrelogregistro` — Container Registry Basic | ✅ |
| `torrelogarquivos` — Storage Account Standard_LRS | ✅ |
| `torrelog-logs` — Log Analytics PerGB2018 | ✅ |
| `torrelog-cofre` — Key Vault standard | ✅ removido **e expurgado** do soft delete |
| `torrelog-ambiente` — Container Apps managed environment | ✅ |
| `torrelog-identidade` — identidade gerenciada de usuário | ✅ |
| Role assignment `AcrPull` | ✅ |
| Resource Group `torre-logistica-rg` | ✅ |

Verificação independente, em toda a assinatura:

```
Storage Accounts       total= 0 | da Torre: NENHUM
Container Registries   total= 0 | da Torre: NENHUM
PostgreSQL servers     total= 0 | da Torre: NENHUM
Recursos (assinatura)  total= 6 | da Torre: NENHUM
role assignments       total= 4 | apontando para escopo da Torre: 0 | órfãs: 0
Key Vaults em soft delete: nenhum
```

Os 6 recursos restantes são todos do projeto `tenant-core`, **não tocado**.

**Custo recorrente da Torre Logística no Azure: zero.**

### O que o Azure real provou, e o que continua sem prova

| Item | Situação |
|---|---|
| Template Bicep aplica no Azure real | ✅ 12 dos 17 recursos criados sem erro |
| PostgreSQL 17 Burstable B1ms com 64 GiB | ✅ criado |
| ACR Basic sem usuário administrador | ✅ criado; imagens enviadas e digests conferidos |
| Storage privado, Key Vault RBAC, Log Analytics | ✅ criados |
| Identidade gerenciada **de usuário** puxa do ACR | ✅ provado |
| Identidade gerenciada **do sistema** puxa do ACR | ❌ recusado por ambiente express |
| `GetUserDelegationKey` e RBAC de blob no Azure real | ❌ **continua sem prova** — depende das apps no ar |
| SignalR através do Container Apps | ❌ sem prova |
| Migrations contra o PostgreSQL do Azure | ❌ sem prova |

### Imagens publicadas antes da destruição

Construídas do commit `edd929685ac4` e enviadas ao ACR:

```
torre-logistica-api     sha256:e67d272f076a6173d54d56b35a698714f95cfd0a45dcc1fdeef61bfedfb49256
torre-logistica-workers sha256:e9003d8e761c548d47b031aebc9303f03c0cb7a2074969114d22705c5bf8f3bb
```

O registro foi destruído junto; os digests ficam como registro de que o caminho de build e push funciona.

### O que falta para a Fase 25 fechar

| Item | Situação |
|---|---|
| Arquitetura de produção | ✅ desenhada, validada e **exercitada contra o Azure real** |
| Ambiente de produção no ar | ❌ e **não haverá** enquanto valer a restrição de custo zero |
| Estratégia de deploy da **demonstração** | ❌ **a redesenhar**, com restrição rígida de US$ 0/mês |
| Identidade de usuário no Bicep | ❌ correção conhecida, não aplicada |

## Redesenho da demonstração: US$ 0,00 numa máquina Always Free da Oracle

> A Fase 25 **continua 🟨**. A arquitetura de demonstração está desenhada, construída e validada
> localmente. **Nada foi provisionado na Oracle**, e provisionar exige autorização explícita.

A restrição virou absoluta: a demonstração não pode ter custo recorrente nem consumir crédito de
nenhum provedor. A arquitetura de produção (`infra/`, Azure) fica intacta como referência; a de
demonstração nasce separada, em `infra-demo/`.

Decisão em [ADR 0035](./docs/adr/0035-demonstracao-de-custo-zero.md); provas, comandos e passo a passo
em [`infra-demo/README.md`](./infra-demo/README.md).

### Por que uma máquina virtual, e não um plano gratuito de aplicação

A Torre Logística pede cinco coisas que os planos gratuitos habituais recusam:

| Exigência | Por quê | O que os planos gratuitos fazem |
|---|---|---|
| Processo sempre vivo | outbox a cada 5 s, previsão a cada minuto, motor de alertas | dormem quando ociosos |
| Conexão persistente | SignalR para o console (ADR 0017) | não suportam WebSocket, ou cobram |
| PostgreSQL **com PostGIS** | geofence é `ST_DWithin` no banco (ADR 0002) | Postgres sem extensão, ou nenhum |
| **Dois** processos separados | a própria Fase 25 os separou | um processo por aplicação |
| Disco que sobrevive | comprovantes e histórico de posição | sistema de arquivos efêmero |

E, entre os níveis gratuitos, a palavra que decide é **Always**: AWS e Azure expiram em 12 meses; o
`e2-micro` do Google é permanente mas tem 1 GB. O Ampere A1 da Oracle não tem prazo e dá 2 OCPU.

### Os dois gates que podiam invalidar tudo

**1. Custo zero.** Recurso a recurso, com a citação da documentação da Oracle para cada linha, a conta
fecha — e a linha apertada é uma só: **1.460 de 1.500 OCPU-hora por mês**, folga de 40 horas. É isso
que faz 2 OCPUs ser teto, não preferência, e `variables.tf` recusa o valor antes do `plan`.

Ficaram de fora, mesmo estando no nível gratuito: o balanceador (o Caddy já faz o trabalho, e o
gratuito traria teto de 10 Mbps) e o Object Storage (os comprovantes cabem no disco de 50 GB).

**2. ARM64.** Aqui apareceu o achado que redesenhou a pilha:

```text
docker manifest inspect postgis/postgis:17-3.5  →  linux/amd64, e nada mais
```

A imagem PostGIS usada em desenvolvimento e nos testes **não existe para arm64** — o README do projeto
diz isso com todas as letras. As alternativas multiarquitetura de terceiros se declaram experimentais
("*Status: Experimental*", "*(test) Docker image*"), o que não serve para o banco de uma demonstração
pública.

A saída foi construir o que o Dockerfile oficial constrói — pacotes PGDG sobre a imagem oficial do
PostgreSQL — também para arm64, partindo de bookworm porque o bullseye do upstream é Debian 11, cujo
LTS terminou em agosto de 2026. Isso troca PostGIS 3.5 por 3.6, e a diferença foi conferida contra o
caso de fronteira que o próprio ROADMAP exige: **299 m dentro, 301 m fora**.

### A emulação não servia, e a compilação cruzada resolveu

A primeira tentativa de construir para arm64 emulou o SDK inteiro por QEMU. Foi interrompida depois de
**1 h 30 min sem terminar a primeira imagem**.

Os três Dockerfiles .NET passaram a fazer compilação cruzada — `FROM --platform=$BUILDPLATFORM` no
estágio de compilação e `-a $TARGETARCH` no `restore` e no `publish`, que é o caminho documentado pela
Microsoft. A mesma imagem passou a levar **146 segundos**. O estágio Node da imagem web recebeu o mesmo
tratamento, por outro motivo: o que ele produz não tem arquitetura.

### Três defeitos que a fase encontrou e corrigiu

| Defeito | Onde | Por que importava |
|---|---|---|
| A API não tratava `X-Forwarded-For` | `src/TorreLogistica.Api/Seguranca/ProxyReverso.cs` (novo) | atrás do Caddy, o limite de tentativas de login por endereço contaria todo o tráfego num balde só: o primeiro visitante a errar a senha cinco vezes bloquearia todos os outros |
| `Referrer-Policy` do site não sobrescrevia a do trecho importado | `infra-demo/Caddyfile` | o rastreamento público saía com `strict-origin-when-cross-origin`, e o token do link poderia vazar no `Referer` |
| `header /index.html Cache-Control` não pegava nada | `infra-demo/Caddyfile` | quem abre o site pede `/`, não `/index.html`; uma implantação não chegaria a quem já visitou |

O primeiro é o que o ROADMAP já registrava como pendência desde a Fase 8 (*"o limite por endereço verá
o do proxy até os cabeçalhos encaminhados serem tratados — Fase 25"*). Ele nasce **desligado**, e ligá-lo
sem declarar de quem confiar **derruba a subida** — porque "ligado e inseguro" é pior que desligado: cada
atacante escolheria o próprio endereço e o limite deixaria de existir.

Um quarto achado veio dos próprios testes: a validação das redes confiáveis estava dentro de um
`Configure`, que é preguiçoso — uma rede mal escrita derrubaria a **primeira requisição de um visitante**
com a subida já dada como bem-sucedida. Passou a ser analisada na subida.

### O elenco fictício precisava existir antes da história

Um banco recém-criado não tem organização nenhuma, e sem organização não há conta: nem a do visitante
da demonstração, nem a que o simulador usa para montar o palco. O semeador que fazia isso só rodava em
Development.

Ele passou a rodar também fora, **por decisão escrita**: `PermitirForaDeDesenvolvimento`, no mesmo
padrão já aprovado do armazenamento local. Ligado sem essa autorização num ambiente publicado, a API
**recusa subir** — porque semear em silêncio e não semear em silêncio escondem igualmente a decisão de
quem a tomou.

### O que foi validado localmente

| Prova | Resultado |
|---|---|
| Os cinco artefatos rodam em arm64 | ✅ `docker image inspect` → `linux/arm64` nos cinco |
| A pilha sobe e fica saudável | ✅ banco, API e web com sonda verde; workers vivo (sem sonda, por desenho) |
| Só o Caddy publica porta | ✅ banco, workers e API com **nenhuma** porta publicada |
| O banco não é alcançável pela borda | ✅ redes separadas, a do banco `internal: true` |
| A borda roteia os quatro nomes | ✅ três aplicações certas + `/health/ready` da API |
| Recarregar rota interna não dá 404 | ✅ nas três aplicações |
| PostGIS real na imagem construída | ✅ `postgis 3.6 USE_GEOS=1 USE_PROJ=1`; 299 m dentro, 301 m fora |
| Migrations e semeadura na subida | ✅ 40 tabelas, 2 organizações, 5 usuários |
| As seis histórias | ✅ simulador com saída 0; 6 entregas, 32 eventos, 6 alertas, 1 ocorrência |
| Comprovante sobrevive a reinício | ✅ mesma impressão após `restart` **e** após `down`/`up` |
| Banco sobrevive a `down`/`up` | ✅ 6 entregas e 32 eventos depois de destruir os contêineres |
| SignalR através do proxy | ✅ WebSocket declarado; caiu no reinício da API e **reconectou em ~4 s** |
| Memória | ✅ pico de **346 MiB** durante as histórias; tetos declarados somam 2,6 GB de 4 GB |
| Terraform | ✅ `fmt` sem mudanças, `validate` OK com `oracle/oci v7.32.0` |
| Formatação .NET | ✅ `dotnet format --verify-no-changes`: 0 de 347 arquivos |

### A prova que quase passou sem provar nada

A primeira verificação de persistência de comprovante comparou **0 arquivos antes com 0 arquivos
depois** e se declarou aprovada. A causa: o roteiro do simulador registra o comprovante com
`arquivos = []` — ele encena a história, não o envio do arquivo.

A persistência foi então provada exercitando o caminho que um motorista de verdade percorre, através
do Caddy: sessão de motorista → autorização de envio → `PUT` dos bytes na URL assinada → arquivo em
disco, com dono 1654, idêntico depois de reiniciar e depois de destruir a pilha.

### Uma afirmação deste documento que a medição desmentiu

Antes de medir, este roadmap e o ADR 0035 diziam que pedir 4 GB em vez de 12 GB tirava a máquina da
faixa de "ociosa" que autoriza a Oracle a recuperá-la. **Está errado.** A pilha usa ~0,4 GB, o que dá
10 % de 4 GB — abaixo do limiar de 20 %. Pedir menos memória triplica a utilização medida (3 % → 10 %)
e continua sem cruzar a linha.

O risco de recuperação é **real e fica declarado**. A resposta é tornar a reconstrução barata —
Terraform e compose versionados, procedimento escrito — e **não** gerar tráfego artificial para
enganar o critério.

### O que falta para a Fase 25 fechar

| Item | Situação |
|---|---|
| Arquitetura de produção | ✅ desenhada, validada e exercitada contra o Azure real |
| Arquitetura de demonstração | ✅ desenhada, construída e validada localmente |
| Ambiente de demonstração **no ar** | ✅ publicado em 28/09/2026 |
| Domínio e registros DNS | ✅ quatro registros A, TTL 60, conferidos em 4 resolvedores |
| Declarações de cota conferidas contra a conta real | ✅ cota efetiva 1 para o shape gratuito, 0 para os pagos |

## O ambiente publicado — Fase 25 ✅

Publicado em **28/09/2026**, em `VM.Standard.E2.1.Micro` Always Free na Oracle Cloud
(`sa-saopaulo-1`), com custo recorrente de **US$ 0,00**.

| Endereço | |
|---|---|
| `operacao.torre.lucasafvr.com.br` | console operacional |
| `motorista.torre.lucasafvr.com.br` | PWA do motorista |
| `rastrear.torre.lucasafvr.com.br` | rastreamento público |
| `api.torre.lucasafvr.com.br` | API e SignalR |

Todos em `137.131.167.193`, atrás de um Caddy com certificado Let's Encrypt.

### O que foi comprovado na infraestrutura pública, não em laboratório

| Critério | Prova |
|---|---|
| DNS | 16 de 16 consultas (4 nomes × `ns1`, `ns2`, `1.1.1.1`, `8.8.8.8`) devolvendo só o IP da VM, TTL 60 |
| HTTPS | certificado Let's Encrypt `CN=api.torre.lucasafvr.com.br`, válido até 27/12/2026; `http` responde 308 |
| API | `/health/live` e `/health/ready` em 200; raiz protegida devolve 401 |
| Workers | processo separado, 0 reinícios, 0 erros |
| PostgreSQL/PostGIS | PostGIS **3.6** com `USE_GEOS=1 USE_PROJ=1`; fronteira 299 m dentro / 301 m fora |
| SignalR | WebSocket sobre TLS através do Caddy, conexão estabelecida e encerrada |
| Seis histórias | todas concluídas pelo simulador, como cliente HTTP real |
| Prova de entrega | autorização 200 → envio pela URL assinada → **201**, 160 bytes |
| Rastreamento público | token válido 200, token inválido **404** |
| Persistência | `down`/`up` completo: banco e arquivos idênticos (2 arquivos, 270 bytes) |
| Segurança | só 22/80/443 escutando; 111, 5432, 8080 e 2019 filtrados; rede do banco `internal=true`; CSP, HSTS, `X-Frame-Options: DENY`, `nosniff`; nenhum cabeçalho de versão vazando |
| Memória | pico de **701 MiB** de 954, com 295 MiB em swap; em repouso 593 MiB; **0 OOM** |
| Custo | `billing-type: ALWAYS_FREE`; armazenamento em 97 GB de 200 gratuitos |

### Latência medida pela internet pública

| Rota | p50 | p95 | p99 |
|---|---|---|---|
| `/health/ready` | 20 ms | 95 ms | 117 ms |
| `GET /api/entregas` | 47 ms | 222 ms | 329 ms |

Melhor que a estimativa local de pior caso (p99 de ~450 ms), porque a emulação não concedia o burst
que o shape entrega de verdade — durante as histórias os workers chegaram a **272% de um núcleo**.

### O que o ambiente público NÃO é

Nó único, 1 GB de RAM, 1/8 de OCPU de linha de base, burst não garantido, sem SLA e sujeito a
recuperação pela Oracle se ficar ocioso. É uma demonstração de portfólio funcionando de verdade,
não a arquitetura recomendada para um cliente real — essa continua sendo a do Azure, em `infra/`.

### Pendência de segurança, fora dos critérios da fase

A credencial técnica `torre-capacity-watcher`, criada só para vigiar capacidade A1, **ainda existe**.
Nenhuma vigília está rodando e a VM já foi criada, então ela não tem mais função. Removê-la exige
sessão humana da OCI com privilégio administrativo — o usuário técnico não pode remover a si mesmo.

## Commits

`refactor: separa lacos de fundo da api em processo de workers proprio (Fase 25)`
`test: corrige causa raiz do alerta de permanencia e congela sizing e regiao (Fase 25)`
`fix: corpo json ilegivel responde 400 e armazenamento falha na subida (Fase 25)`
`feat: adaptador de azure blob storage para comprovantes, com identidade gerenciada (Fase 25)`
`refactor: divide o composition root para cada processo registrar so o que usa (Fase 25)`
`docs: registra o provisionamento real no azure e sua destruicao por custo (Fase 25)`
`fix: api atras de proxy le o endereco real e semeia o elenco por decisao escrita (Fase 25)`
`build: imagens .net compilam cruzado para arm64 em vez de emular o sdk (Fase 25)`
`feat: infraestrutura de demonstracao de custo zero em oracle always free (Fase 25)`
`docs: separa a arquitetura de producao da de demonstracao e corrige a conta de ociosidade (Fase 25)`

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
| Última fase concluída | **Fase 18 — Console Operacional e Mapa** (2026-09-20) |
| Próxima fase | **Fase 19 — Indicadores e Analytics** |
| Testes verdes | 1.352 — 689 unidade, 22 arquitetura, 518 integração, 123 frontend |

Comando para continuar:

> **siga para a próxima fase**
