# Matriz de autorização

> Estado: Fase 14. Esta tabela é espelhada em `tests/TorreLogistica.IntegrationTests/AutorizacaoTestes.cs`,
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
| `GET /api/entregas`, `GET /api/entregas/{id}` e `GET .../eventos` | 200 | 200 | 200 | **401** | 401 | `operacao:leitura` |
| `POST /api/entregas` | 201 | 201 | 201 | **401** | 401 | `entregas:operacao` |
| `PUT /api/entregas/{id}` e `POST .../cancelamento` | 200 | 200 | 200 | **401** | 401 | `entregas:operacao` |
| `GET /api/rotas`, `GET /api/rotas/{id}` e `GET .../eventos` | 200 | 200 | 200 | **401** | 401 | `operacao:leitura` |
| `POST /api/rotas` | 201 | 201 | 403 | **401** | 401 | `operacao:gestao` |
| `POST .../paradas`, `DELETE .../paradas/{entregaId}`, `PUT .../ordem`, `.../motorista`, `.../veiculo`, `.../saida`, `POST .../planejamento`, `.../cancelamento` | 200 | 200 | 403 | **401** | 401 | `operacao:gestao` |
| `POST /api/entregas/{id}/reagendamento` | 200 | 200 | 200 | **401** | 401 | `entregas:operacao` |
| `GET /api/motorista/rotas`, `GET /api/motorista/rotas/{id}` e `GET /api/motorista/entregas/{id}` | **401** | **401** | **401** | 200 | 401 | `motorista` |
| `POST /api/motorista/rotas/{id}/inicio` e `.../conclusao` | **401** | **401** | **401** | 200 | 401 | `motorista` |
| `POST /api/motorista/entregas/{id}/chegada`, `.../conclusao` e `.../tentativa-frustrada` | **401** | **401** | **401** | 200 | 401 | `motorista` |
| `POST /api/motorista/posicoes` | **401** | **401** | **401** | 200 | 401 | `motorista` + limite por motorista |
| `POST /api/motorista/sincronizacao` | **401** | **401** | **401** | 200 | 401 | `motorista` + limite por motorista; dono de cada operação resolvido pela sessão |
| `GET /api/motoristas/{id}/posicao-atual` | 200 | 200 | 200 | **401** | 401 | `operacao:leitura` |
| `GET /api/motoristas/{id}/posicoes` | 200 | 200 | 403 | **401** | 401 | `operacao:gestao` |
| `GET /api/entregas/{id}/geofence` | 200 | 200 | 200 | **401** | 401 | `operacao:leitura` |
| `GET /api/entregas/{id}/previsao` | 200 | 200 | 200 | **401** | 401 | `operacao:leitura` |
| `GET /api/alertas` e `GET /api/alertas/{id}` | 200 | 200 | 200 | **401** | 401 | `operacao:leitura` |
| `POST /api/alertas/{id}/resolucao` | 200 | 200 | 200 | **401** | 401 | `entregas:operacao` |
| `GET /api/ocorrencias`, `GET /api/ocorrencias/{id}` e `GET /api/entregas/{id}/ocorrencias` | 200 | 200 | 200 | **401** | 401 | `operacao:leitura` |
| `GET /api/entregas/{id}/comprovante` | 200 | 200 | 200 | **401** | 401 | `operacao:leitura`; devolve URLs assinadas curtas |
| `POST /api/motorista/entregas/{id}/comprovante` e `.../comprovante/autorizacao` | **401** | **401** | **401** | 200 | 401 | `motorista`; só entrega do motorista da sessão |
| `PUT` e `GET /api/arquivos/{chave}` | — | — | — | — | — | anônimo **por desenho**: a credencial é a assinatura da URL (ADR 0023); sem assinatura válida e no prazo, 403 |
| `POST /api/entregas/{id}/link-de-rastreamento` | 200 | 200 | 200 | **401** | 401 | `entregas:operacao`; o valor emitido aparece uma única vez e revoga o anterior |
| `GET /api/publico/rastreamento/{token}` | — | — | — | — | — | anônimo **por desenho**: a credencial é o token do link (ADR 0024); token que não abre nada devolve 404 idêntico para todos, e a rota tem limite por endereço |
| `GET` e `POST /api/integracoes`, `GET /api/integracoes/{id}`, `POST /api/integracoes/{id}/revogacao` | 200/201 | 403 | 403 | **401** | 401 | `usuarios:gestao`: emitir e revogar credencial é decisão de segurança, como criar conta. A chave aparece uma única vez |
| `/api/integracoes/v1/*` (entregas e importações) | **401** | **401** | **401** | **401** | 401 | esquema **Integracao**: a credencial é a chave de API (ADR 0025). Token de pessoa não vale aqui, e a chave não vale no console; limite por credencial |
| `POST /api/entregas/{id}/ocorrencias` | 201 | 201 | 201 | **401** | 401 | `entregas:operacao`; recusa tipo `TentativaDeEntrega` |
| `POST /api/motorista/entregas/{id}/ocorrencia` | **401** | **401** | **401** | 201 | 401 | `motorista`; só entrega do motorista da sessão |
| Hub `/tempo-real/operacao` (conexão e `negotiate`) | conecta | conecta | conecta | **401** | 401 | `console` |

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
| Operador cria, altera e cancela entregas; estrutura operacional continua com administrador e supervisor | política `entregas:operacao`; [ADR 0012](../adr/0012-entrega-como-agregado-central.md) |
| Cliente ou destinatário de outra organização informado na entrega responde 404, idêntico a inexistente | procurados nas consultas filtradas pelo tenant |
| Status, código e organização não entram no corpo da entrega | campo desconhecido responde 400; status muda só por operação |
| Supervisor e administrador montam e atribuem rotas; operador consulta | política `operacao:gestao`; [ADR 0013](../adr/0013-rotas-e-paradas.md) |
| Entrega, motorista, veículo e hub de outra organização informados na rota respondem 404, idêntico a inexistente | procurados nas consultas filtradas pelo tenant |
| Motorista comanda só a própria rota e as próprias entregas; as demais respondem 404 idêntico a inexistente | motorista resolvido pela conta da sessão; [ADR 0014](../adr/0014-maquina-de-estados-e-concorrencia.md) |
| Entrega que foi do motorista e passou a outro responde 409 `entrega_reatribuida`, não 404 | conferido na timeline; não revela entregas que nunca foram dele |
| Conta de motorista sem cadastro associado responde 404 `motorista_nao_associado` | nada é executado |
| Não existe endpoint genérico de status | teste lê o roteamento real: sem `PATCH`, sem "status" no caminho, lista exata de comandos |
| Posição é sempre do motorista da sessão; `motoristaId` no corpo responde 400 | [ADR 0015](../adr/0015-ingestao-de-localizacao.md) |
| Posição fora da execução de uma rota do próprio motorista é recusada | coleta mínima, conferida no servidor |
| Histórico de localização só para gestão, por período de até 24 h | operador vê só a posição atual |
| Limite de envio de posições por motorista, não por endereço | operadoras móveis compartilham IP (CGNAT) |
| Geofence só avalia entregas da organização e do motorista responsável; destino de outra organização no mesmo ponto não é tocado | consulta PostGIS com organização explícita; [ADR 0016](../adr/0016-geofence-de-destino.md) |
| Eventos de geofence guardam distância e raio, nunca a coordenada | a localização exata fica só no histórico de posições |
| Conexão de tempo real entra só no grupo da organização da sessão; o hub não tem método para escolher grupo | [ADR 0017](../adr/0017-tempo-real-da-operacao.md) |
| Token na query string autentica só o hub do console; em rota da API, não | o navegador não envia cabeçalho em WebSocket |
| Sessão revogada derruba as conexões de tempo real dela; token vencido fecha a conexão | detectado no commit da revogação |
| Rastreamento público não usa o canal interno | o hub exige sessão do console |
| Previsão e situação do SLA não são informadas por ninguém: não há rota de escrita | calculadas pelo sistema a partir da execução; [ADR 0018](../adr/0018-previsao-de-chegada-e-sla.md) |
| Recálculo em segundo plano roda no tenant da organização do pedido, nunca sem tenant | contexto de persistência criado com o tenant do pedido; a reavaliação periódica lê sem tenant só identificadores |
| Explicação da previsão não leva coordenada nem dado do destinatário | só durações, distâncias e regras |
| Alerta não é criado nem alterado pela API; o operador só consulta e resolve | o motor abre e resolve pelas regras; [ADR 0019](../adr/0019-motor-de-alertas-operacionais.md) |
| Alerta de outra organização responde 404 idêntico a inexistente, inclusive na resolução | filtro global de tenant nas duas tabelas |
| Evidência e aviso de alerta sem coordenada nem dado do destinatário | a localização exata fica no histórico de posições, só para gestão |
| Resolução registra quem resolveu e a observação, em ciclo de vida somente-inserção | trigger no banco |
| Motorista lê só as próprias rotas e entregas, com modelo próprio sem dado do console; o resto responde 404 | [ADR 0020](../adr/0020-pwa-do-motorista.md) |
| Contato e instruções do destinatário só no detalhe da entrega do próprio motorista | não aparecem na lista da rota |
| PWA guarda o token de acesso só em memória e limpa o cache de consultas ao sair | ninguém que pegar o aparelho depois vê a rota anterior |
| Localização só é pedida e coletada com rota em andamento e o aplicativo aberto | coleta mínima; sem rastreamento em segundo plano |

## Como adicionar um endpoint

1. Declare a política explicitamente (`RequireAuthorization` ou `AllowAnonymous`). Não conte com
   o fallback.
2. Acrescente a linha nesta tabela.
3. Acrescente a linha em `AutorizacaoTestes.Matriz` e em `TodoEndpointTemAutorizacaoDeclarada`.
4. Se o recurso pertence a um tenant, acrescente o teste cross-tenant (Gate de tenant do ROADMAP).
