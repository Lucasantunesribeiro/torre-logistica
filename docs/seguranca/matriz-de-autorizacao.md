# Matriz de autorização

> Estado: Fase 2. Esta tabela é espelhada em `tests/TorreLogistica.IntegrationTests/AutorizacaoTestes.cs`,
> que falha se a matriz e o roteamento real divergirem — em qualquer direção.

## Endpoints e respostas por perfil

| Método e rota | Administrador | Supervisor | Operador | Motorista | Sem sessão | Política |
|---|:---:|:---:|:---:|:---:|:---:|---|
| `GET /health/live` | 200 | 200 | 200 | 200 | 200 | anônimo |
| `GET /health/ready` | 200 | 200 | 200 | 200 | 200 | anônimo |
| `POST /api/autenticacao/login` | — | — | — | — | — | anônimo + origem confiável + limite |
| `POST /api/autenticacao/renovar` | — | — | — | — | — | anônimo + cookie + origem confiável + limite |
| `POST /api/autenticacao/sair` | — | — | — | — | — | anônimo + cookie + origem confiável |
| `GET /api/autenticacao/eu` | 200 | 200 | 200 | **401** | 401 | `console` |
| `POST /api/motorista/autenticacao/login` | — | — | — | — | — | anônimo + origem confiável + limite |
| `POST /api/motorista/autenticacao/renovar` | — | — | — | — | — | anônimo + cookie + origem confiável + limite |
| `POST /api/motorista/autenticacao/sair` | — | — | — | — | — | anônimo + cookie + origem confiável |
| `GET /api/motorista/autenticacao/eu` | **401** | **401** | **401** | 200 | 401 | `motorista` |
| `GET /api/organizacao` | 200 | 200 | 200 | **401** | 401 | `console` |
| `GET /api/usuarios` | 200 | 200 | 403 | **401** | 401 | `usuarios:leitura` |
| `GET /api/usuarios/{id}` | 200 | 200 | 403 | **401** | 401 | `usuarios:leitura` |
| `POST /api/usuarios` | 201 | 403 | 403 | **401** | 401 | `usuarios:gestao` |
| `PUT /api/usuarios/{id}/perfil` | 200 | 403 | 403 | **401** | 401 | `usuarios:gestao` |
| `POST /api/usuarios/{id}/desativacao` | 200 | 403 | 403 | **401** | 401 | `usuarios:gestao` |
| `GET /api/motoristas` e `GET /api/motoristas/{id}` | 200 | 200 | 200 | **401** | 401 | `operacao:leitura` |
| `POST /api/motoristas` | 201 | 201 | 403 | **401** | 401 | `operacao:gestao` |
| `PUT /api/motoristas/{id}` e `POST .../ativacao`, `.../inativacao` | 200 | 200 | 403 | **401** | 401 | `operacao:gestao` |
| `GET /api/veiculos` e `GET /api/veiculos/{id}` | 200 | 200 | 200 | **401** | 401 | `operacao:leitura` |
| `POST /api/veiculos` | 201 | 201 | 403 | **401** | 401 | `operacao:gestao` |
| `PUT /api/veiculos/{id}` e `POST .../ativacao`, `.../inativacao` | 200 | 200 | 403 | **401** | 401 | `operacao:gestao` |
| `GET /api/hubs` e `GET /api/hubs/{id}` | 200 | 200 | 200 | **401** | 401 | `operacao:leitura` |
| `POST /api/hubs` | 201 | 201 | 403 | **401** | 401 | `operacao:gestao` |
| `PUT /api/hubs/{id}` e `POST .../ativacao`, `.../inativacao` | 200 | 200 | 403 | **401** | 401 | `operacao:gestao` |
| `GET /api/clientes` e `GET /api/clientes/{id}` | 200 | 200 | 200 | **401** | 401 | `operacao:leitura` |
| `POST /api/clientes` | 201 | 201 | 403 | **401** | 401 | `operacao:gestao` |
| `PUT /api/clientes/{id}` e `POST .../ativacao`, `.../inativacao` | 200 | 200 | 403 | **401** | 401 | `operacao:gestao` |
| `GET /api/destinatarios` e `GET /api/destinatarios/{id}` | 200 | 200 | 200 | **401** | 401 | `operacao:leitura` |
| `POST /api/destinatarios` | 201 | 201 | 403 | **401** | 401 | `operacao:gestao` |
| `PUT /api/destinatarios/{id}` e `POST .../ativacao`, `.../inativacao` | 200 | 200 | 403 | **401** | 401 | `operacao:gestao` |
| `PUT` e `DELETE /api/motoristas/{id}/conta` | 200 | 200 | 403 | **401** | 401 | `operacao:gestao` |

"—" nas rotas de autenticação: a resposta depende das credenciais, não do perfil.

## Por que o motorista recebe 401 no console, e não 403

`403` significaria "você está autenticado, mas não pode". Para o console, o token do motorista não
é credencial: ele foi emitido para outra audiência (`torre-logistica:motorista`), e o esquema de
autenticação do console o rejeita antes de a autorização ser consultada. O mesmo vale no sentido
inverso.

## Regras que valem além da tabela

| Regra | Onde |
|---|---|
| Endpoint sem política declarada exige sessão do console | política de *fallback* |
| Rota inexistente sem sessão responde 401, igual a rota existente | consequência do fallback: o mapa de rotas não é levantável sem login |
| Recurso de outra organização responde 404, idêntico a identificador inexistente | filtro global de tenant, [ADR 0010](../adr/0010-multi-tenancy-e-isolamento.md) |
| Campo desconhecido no corpo JSON responde 400 | defesa contra mass assignment |
| A organização vem só da sessão | nenhuma rota aceita organização como parâmetro |
| Conta de motorista não vira perfil do console, nem o contrário | regra de domínio `perfil_incompativel` |
| A organização mantém ao menos um administrador ativo | regra `ultimo_administrador`, serializada por trava de linha |
| Ninguém desativa a própria conta | regra `nao_pode_desativar_a_propria_conta` |
| Troca de perfil e desativação encerram as sessões da conta afetada | na mesma transação |
| Supervisor prepara a estrutura operacional; operador consulta e não altera | políticas `operacao:gestao` e `operacao:leitura` |
| Alteração de cadastro exige a `versao` lida | `409 conflito_de_versao`; [ADR 0011](../adr/0011-cadastros-operacionais.md) |
| Conta de outra organização informada na associação de motorista responde 404 | a conta é procurada na consulta filtrada pelo tenant |

## Como adicionar um endpoint

1. Declare a política explicitamente (`RequireAuthorization` ou `AllowAnonymous`). Não conte com
   o fallback.
2. Acrescente a linha nesta tabela.
3. Acrescente a linha em `AutorizacaoTestes.Matriz` e em `TodoEndpointTemAutorizacaoDeclarada`.
4. Se o recurso pertence a um tenant, acrescente o teste cross-tenant (Gate de tenant do ROADMAP).
