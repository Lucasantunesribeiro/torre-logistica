# Gestão de segredos

> Escopo: o que vale desde a Fase 0. O modelo de segurança completo
> (`docs/security-model.md`) é entregável da Fase 20.

## Regra

Nenhum segredo entra no repositório. Nem em código, nem em `appsettings`, nem em
comentário, nem em arquivo de exemplo, nem em mensagem de commit.

O que pode ser versionado é o **nome** da variável e um valor obviamente falso.

## Onde cada segredo vive

| Ambiente | Origem do valor |
|---|---|
| Desenvolvimento local | `.env` na raiz (ignorado pelo Git) ou `dotnet user-secrets` |
| Teste automatizado | gerado em tempo de execução pelo Testcontainers |
| CI | segredo do repositório no GitHub Actions |
| Produção | gerenciador de segredos da nuvem (decidido na Fase 25) |

## Variáveis em uso

| Variável | Para quê | Contém segredo |
|---|---|:---:|
| `TORRE_POSTGRES_BANCO` | nome do banco no contêiner local | não |
| `TORRE_POSTGRES_USUARIO` | usuário no contêiner local | não |
| `TORRE_POSTGRES_SENHA` | senha do contêiner local | **sim** |
| `TORRE_POSTGRES_PORTA` | porta publicada pelo contêiner (55432) | não |
| `Torre__BancoDeDados__CadeiaDeConexao` | cadeia de conexão da API e dos workers | **sim** |
| `TORRE_BANCO_CADEIA_DE_CONEXAO` | cadeia usada pelas ferramentas do EF Core | **sim** |
| `Torre__Simulador__UrlBaseDaApi` | endereço da API para o simulador | não |
| `VITE_URL_DA_API` | endereço da API para as aplicações web | não |

O separador `__` (dois sublinhados) é como a configuração do .NET representa
hierarquia: `Torre__BancoDeDados__CadeiaDeConexao` alimenta a seção
`Torre:BancoDeDados:CadeiaDeConexao`.

## Variável com prefixo `VITE_` não é lugar de segredo

Tudo com esse prefixo é **embutido no pacote JavaScript publicado** e fica legível por
qualquer visitante que abra as ferramentas do navegador. Só entra aí endereço público.

Não existe "esconder" valor no frontend: o que chega ao navegador é público.

## O que está versionado e por quê não é vazamento

| Arquivo | Conteúdo | Por que é seguro |
|---|---|---|
| `.env.example` | nomes de variável com valores marcados `troque_esta_senha_local` | valor de preenchimento, inútil em qualquer ambiente |
| `docker-compose.yml` | `${TORRE_POSTGRES_SENHA}` | interpolação; o valor vem do `.env`, não versionado |
| `appsettings.json` | `"CadeiaDeConexao": ""` | vazio de propósito; a aplicação **falha na subida** sem o valor real |
| `FabricaDeDbContextEmTempoDeDesign` | `Host=localhost;…;Username=torre` | sem credencial; gerar migration não abre conexão |
| `FabricaDaApi` (teste) | senha gerada por `Guid.CreateVersion7()` | descartável, vive o tempo do contêiner |

## Verificações automáticas

| Camada | O que faz |
|---|---|
| `.gitignore` | barra `.env`, `*.pem`, `*.key`, `*.pfx`, `appsettings.*.Local.json`, `secrets.json` |
| Hook local de `git commit` | bloqueia padrões de senha, token, chave AWS e chave privada antes do commit |
| CI — job `seguranca` | gitleaks sobre o **histórico completo** (`fetch-depth: 0`), configurado por `.gitleaks.toml` |
| CI — backend | `dotnet list package --vulnerable --include-transitive` reprova a execução |
| CI — frontend | `npm audit --audit-level=high` reprova a execução |

A varredura da CI usa o histórico inteiro de propósito: segredo removido em um commit
posterior continua recuperável no anterior.

### Limites conhecidos da varredura

| Limite | Situação |
|---|---|
| `package-lock.json` não é lido pelo gitleaks (exclusão da configuração padrão dele) | Sem impacto hoje: não há registry privado — `.npmrc` ausente e todas as resoluções apontam para `registry.npmjs.org`. Se um registry autenticado entrar no projeto, a lacuna precisa ser fechada |
| Valores de exemplo publicados em documentação oficial — a chave de acesso que aparece nos manuais da AWS, por exemplo — são ignorados | Comportamento correto do gitleaks, e verificado: com credenciais falsas realistas a varredura acusou os 3 achados plantados |

A segunda linha veio de uma checagem feita durante o Security Gate 0. A primeira
tentativa de validar o scanner usou justamente uma chave de exemplo de documentação
oficial e não acusou nada — o que pareceria "repositório limpo" sendo, na verdade,
"scanner não exercitado". Varredura que nunca achou nada precisa ser provada antes de
virar gate.

Lição prática para quem escrever sobre segurança aqui: **não reproduza no texto o
formato de uma credencial**, nem o de exemplo. Descreva em palavras. O hook local de
`git commit` acusa o padrão e bloqueia — corretamente, porque ele não tem como saber
que aquele valor específico é inofensivo.

## Se um segredo for exposto

1. **Rotacione o valor.** Apagar do arquivo não remove do histórico do Git, e quem
   clonou o repositório antes da remoção já tem a cópia.
2. Revogue a credencial antiga no provedor.
3. Só então limpe o repositório, se fizer sentido.
4. Não reproduza o valor — nem truncado — em issue, log, chat ou mensagem de commit.

A ordem importa: limpar o histórico primeiro e rotacionar depois deixa uma janela em
que a credencial válida está publicada.

## Logs

Nunca registrar token, refresh token, chave de integração, URL assinada completa,
conteúdo de comprovante ou dado pessoal desnecessário.

O que já está em vigor na Fase 0:

- o corpo dos endpoints de saúde traz apenas nome, estado e duração de cada verificação
  — descrição e exceção ficam de fora porque carregam host, banco e usuário da cadeia
  de conexão, e o endpoint é anônimo;
- resposta de erro nunca inclui mensagem de exceção; o detalhe fica no log, ligado ao
  identificador de correlação devolvido ao cliente;
- o identificador de correlação recebido do cliente é validado contra um formato
  estreito antes de entrar em qualquer linha de log, para fechar injeção de log.
