# Ambiente local de desenvolvimento

## Pré-requisitos

| Ferramenta | Versão | Observação |
|---|---|---|
| .NET SDK | 10.0.400 | fixada em `global.json` |
| Node.js | ≥ 22.22.0 | Vite 8 exige; ver "Armadilhas" |
| Docker | com contêineres Linux | banco local e Testcontainers |
| PowerShell | 5.1 ou superior | para `scripts/testar.ps1` |

## Primeira execução

```bash
# 1. Variáveis de ambiente locais
cp .env.example .env
# edite .env e troque TORRE_POSTGRES_SENHA por um valor local qualquer

# 2. Banco PostgreSQL + PostGIS
docker compose up -d
docker compose ps        # precisa aparecer "healthy"

# 3. Schema
dotnet dotnet-ef database update \
  --project src/TorreLogistica.Infrastructure \
  --startup-project src/TorreLogistica.Infrastructure

# 4. Dependências do frontend
npm install
```

## Rodando

```bash
# API em http://localhost:5080
dotnet run --project src/TorreLogistica.Api

# Aplicações web
npm run dev:operacao        # http://localhost:5173
npm run dev:motorista       # http://localhost:5174
npm run dev:rastreamento    # http://localhost:5175

# Workers
dotnet run --project src/TorreLogistica.Workers

# Simulador: confere se a API está pronta
dotnet run --project src/TorreLogistica.Simulator
```

### Contas locais para login

A semeadura de desenvolvimento cria duas organizações fictícias, só em `Development` e só se
habilitada. A senha comum das contas vem do ambiente, nunca do repositório:

```bash
export Torre__Desenvolvimento__Semeadura__Habilitada=true
export Torre__Desenvolvimento__Semeadura__SenhaInicial="<uma senha local com 12+ caracteres>"
```

| Organização (slug) | Conta | Perfil |
|---|---|---|
| `transportadora-aurora` | `helena.duarte@aurora.test` | Administrador |
| `transportadora-aurora` | `marcos.vieira@aurora.test` | Supervisor |
| `transportadora-aurora` | `paula.siqueira@aurora.test` | Operador |
| `transportadora-aurora` | `rafael.mendes@aurora.test` | Motorista (só entra pela PWA) |
| `logistica-boreal` | `tiago.fontes@boreal.test` | Administrador |

A operação é idempotente: organização que já existe não é recriada. Sem chave de assinatura
configurada, a API gera uma chave efêmera em `Development` — as sessões não sobrevivem a
reinício da API.

O login do console exige a organização, o e-mail e a senha. O cookie de renovação é `Secure` e
`SameSite=Strict`: funciona em `http://localhost` porque navegadores tratam `localhost` como
contexto seguro, e porque `localhost:5173` e `localhost:5080` são o mesmo site.

A API lê `Torre__BancoDeDados__CadeiaDeConexao` do ambiente. Em um terminal que não
carregou o `.env`, exporte a variável antes de rodar — a aplicação **falha na subida**
se a cadeia de conexão estiver ausente, de propósito.

## Testes

```bash
# Todas as suítes do backend
powershell -ExecutionPolicy Bypass -File scripts/testar.ps1

# Uma suíte só
powershell -ExecutionPolicy Bypass -File scripts/testar.ps1 -Suite unit

# Frontend
npm run test
npm run lint
npm run typecheck

# Tudo do frontend em sequência
npm run verificar
```

### Por que não `dotnet test`

Os projetos de teste usam xunit.v3 sobre o Microsoft.Testing.Platform. No SDK 10.0.400
com xunit.v3 4.0.0, o wrapper `dotnet test` encerra com **"Zero testes executados"**
(código 5), enquanto o mesmo executável descobre e roda todas as provas quando invocado
direto.

`scripts/testar.ps1` roda os executáveis. Isso é caminho suportado pela plataforma de
teste e não esconde falha: o código de saída continua reprovando a execução.

O runner está selecionado em `global.json`:

```json
"test": { "runner": "Microsoft.Testing.Platform" }
```

Sem essa seção, `dotnet test` falha antes de executar qualquer coisa, porque o SDK 10
removeu o caminho antigo (VSTest).

## Armadilhas desta máquina

Três foram encontradas durante a Fase 0. As duas primeiras já estão contornadas no
repositório; a terceira é um aviso.

### 1. PostgreSQL nativo ocupando a porta 5432

Há uma instalação nativa do PostgreSQL escutando na 5432. No Windows, ela e o
redirecionamento do Docker conseguem escutar **ao mesmo tempo**, e a conexão vai para a
errada sem erro visível — o sintoma é `28P01: autenticação do tipo senha falhou`, com a
senha certa.

Por isso o contêiner do projeto usa a porta **55432**. Para confirmar quem está na 5432:

```bash
netstat -ano | grep ":5432"
```

### 2. `DOCKER_HOST` com quatro barras

A CLI do Docker usa `npipe:////./pipe/docker_engine`. A biblioteca usada pelo
Testcontainers só reconhece a forma com duas barras e falha com
`The endpoint is not a npipe URI`.

`scripts/testar.ps1` normaliza o valor. Rodando a suíte por outro caminho, ajuste antes:

```bash
DOCKER_HOST="npipe://./pipe/docker_engine"
```

### 3. Duas instalações do .NET

`dotnet` no PATH resolve `C:\Program Files\dotnet`, que tem SDK 9 e **não** tem o
runtime 10. O SDK 10 está em `C:\Users\<usuário>\.dotnet`.

Os executáveis de teste resolvem o runtime por `DOTNET_ROOT`, não pelo `dotnet` que
compilou. `scripts/testar.ps1` escolhe a instalação com SDK 10 e ajusta `DOTNET_ROOT`.
Fora do script, use o caminho completo:

```bash
"C:/Users/<usuário>/.dotnet/dotnet.exe" build TorreLogistica.slnx
```

`TORRE_DOTNET` sobrepõe a escolha do script.

### 4. Node 22.17.0 abaixo do exigido por Vite 8

`npm install` avisa `EBADENGINE`: Vite 8 e `undici` pedem Node ≥ 22.22.0. Com 22.17.0
o build, o lint, os testes e a verificação de tipos passam, mas é um aviso real — vale
atualizar o Node antes de depender disso em CI local.

## Migrations

```bash
# Criar
dotnet dotnet-ef migrations add <Nome> \
  --project src/TorreLogistica.Infrastructure \
  --startup-project src/TorreLogistica.Infrastructure \
  --output-dir Persistencia/Migrations

# Aplicar
dotnet dotnet-ef database update \
  --project src/TorreLogistica.Infrastructure \
  --startup-project src/TorreLogistica.Infrastructure

# Desfazer a última ainda não aplicada
dotnet dotnet-ef migrations remove --project src/TorreLogistica.Infrastructure
```

Gerar migration não abre conexão. Para **aplicar** em um banco real, defina
`TORRE_BANCO_CADEIA_DE_CONEXAO`.

Migrations são código gerado: o `.editorconfig` as isenta das regras de estilo escritas
para código humano, porque corrigi-las à mão a cada `migrations add` tornaria o código
gerado não confiável. O conteúdo continua sendo revisado; o formato, não.

## Encerrando

```bash
docker compose down          # para o banco, mantém os dados
docker compose down -v       # apaga o volume também
```
