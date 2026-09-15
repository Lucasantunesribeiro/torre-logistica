# ADR 0017 — Tempo real da operação: aviso só depois do commit, grupo pela sessão e conexão que cai com a sessão

- **Data:** 2026-09-15
- **Status:** aceita
- **Fase:** 8
- **Complementa:** [ADR 0005](./0005-signalr-como-direcao-de-realtime.md)

## Contexto

A Fase 8 atualiza o console operacional sem recarregar. O ADR 0005 já fixou a direção: SignalR atrás
de abstração, domínio sem SignalR, tempo real como aviso e nunca como fonte de verdade, e canais que
respeitam tenant — motorista fora do canal administrativo e rastreamento público fora do canal interno.

Faltava decidir quatro coisas:

1. Onde o aviso nasce, sem que cada caso de uso precise lembrar de publicar.
2. Como garantir que nenhum aviso sai de uma mudança que não foi confirmada.
3. Como o cliente entra no grupo certo sem poder escolher grupo.
4. O que acontece com uma conexão aberta quando a sessão é encerrada.

## Decisão

### Implementação na `Api`, não na `Infrastructure`

O ADR 0005 previa a implementação em `Infrastructure`. O teste de arquitetura, porém, proíbe
ASP.NET Core ali — com razão: `Infrastructure` também serve ao `Workers`, que não hospeda hub. O hub
e o publicador ficam na `Api`, o composition root que já depende de ASP.NET Core. A abstração
`IPublicadorDeTempoReal` fica na `Application`, como previsto; o domínio continua sem saber que
tempo real existe.

### O aviso nasce num ponto só, e só depois do commit

O contexto de persistência é o único lugar que sabe, com certeza, que uma mudança foi confirmada:

- ao gravar, recolhe os eventos de entrega que mudam status (todos, exceto alteração de dados e
  reatribuição) e as sessões cujo `RevogadaEm` passou de vazio a preenchido;
- a gravação da posição registra o aviso quando a posição atual avançou — um por motorista no lote,
  o mais recente;
- sem transação aberta, despacha logo após gravar; dentro de `ExecutarEmTransacaoAsync`, despacha
  depois do commit, e descarta tudo se a tentativa for desfeita.

Nenhum caso de uso publica diretamente. Uma operação nova que mude status avisa sem ninguém lembrar,
e uma operação que falhe não avisa nada.

A publicação é de melhor esforço: falha ao enviar é registrada e não volta para a operação — o banco
já confirmou, e o cliente recupera o estado pela API.

### Avisos

| No fio | Conteúdo | Origem |
|---|---|---|
| `DriverPositionUpdated` | motorista, rota, latitude, longitude, precisão, velocidade, direção, sequência, captura | posição atual que avançou |
| `DeliveryStatusChanged` | entrega, status, evento que causou, sequência na timeline, instante | evento de entrega que muda status |
| `DeliveryRiskChanged` | — | nome reservado; produzido na Fase 9 (ETA e SLA) |
| `AlertCreated` | — | nome reservado; produzido na Fase 10 |
| `IncidentCreated` | — | nome reservado; produzido na fase de ocorrências |

Os três últimos têm nome definido agora, para o console assinar desde já, e ganham conteúdo junto com
quem os produz — não existe contrato sem produtor. `DeliveryStatusChanged` leva a sequência da
timeline: o cliente que perceber salto sabe que perdeu aviso e recarrega.

O aviso não leva endereço, nome ou telefone de destinatário. Quem precisar do detalhe consulta a API,
com a própria autorização.

### Grupo pela sessão; o cliente não escolhe nada

`/tempo-real/operacao` exige a política do console. Na conexão, o hub lê a organização **do token** e
coloca a conexão em um único grupo, `organizacao:{id}`. O hub não tem nenhum método que o cliente
possa chamar: não existe como pedir outro grupo. Sem organização ou sessão na identidade, a conexão é
encerrada — falha fechada.

Token de motorista não autentica no esquema do console (401). Rastreamento público não tem acesso a
este caminho.

### Token na query string, só no hub

O navegador não envia `Authorization` em WebSocket; o cliente SignalR manda o token como
`access_token` na query string. Isso é aceito **apenas** no caminho do hub, **apenas** no esquema do
console e **apenas** sem cabeçalho. Em qualquer rota da API, token na URL não autentica. Os logs de
requisição registram o caminho sem a query, e os componentes do ASP.NET logam a partir de Warning —
um teste procura o token no log.

### Conexão cai com a sessão

Conexão aberta não volta a passar pela validação de sessão. Duas barreiras:

- **Sessão revogada** — logout, reuso de token de renovação, troca de perfil, desativação: o contexto
  detecta a revogação, e depois do commit o registro de conexões derruba as conexões daquela sessão. A
  reconexão automática falha, porque a sessão é conferida no banco a cada autenticação.
- **Token vencido** — `CloseOnAuthenticationExpiration` fecha a conexão no vencimento do token de
  acesso (15 minutos), mesmo sem revogação.

### Limites e observabilidade

Mensagem do cliente limitada a 4 KB (o hub não espera nenhuma). Métrica
`signalr.connected_clients` (CLAUDE.md, seção 51), no medidor `TorreLogistica.TempoReal`, sem
dimensão de organização.

### Uma instância, sem backplane

O registro de conexões e os grupos vivem na memória da instância. Com mais de uma instância, o aviso
publicado numa não chega aos clientes da outra, e a sessão revogada numa não derruba conexão da outra.
Isso exige backplane ou serviço gerenciado — decisão mantida adiada, como no ADR 0005, e registrada
para a fase de infraestrutura.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Publicar em cada caso de uso | Uma dezena de pontos, cada um podendo esquecer ou publicar antes do commit |
| Interceptor do EF Core registrado como singleton | O estado pendente é por requisição; o contexto já é por requisição e já controla a transação |
| Outbox persistente para tempo real | Aviso perdido se recupera pela API; outbox entra quando houver efeito que não pode se perder (webhook, notificação) |
| Cliente escolhe o grupo por método do hub | Abre a porta para pedir o grupo de outra organização; o grupo precisa vir da sessão |
| Token na query string em qualquer rota | Token em URL vaza para log, histórico e cabeçalho `Referer` |
| Só o vencimento do token para derrubar conexão | Quem sai ou é desativado continuaria recebendo dados por até 15 minutos |
| Hub na `Infrastructure` | Levaria ASP.NET Core para uma camada que também serve ao `Workers` |

## Consequências

- ETA e SLA (Fase 9), alertas (Fase 10) e ocorrências publicam pelo mesmo ponto pós-commit.
- O mapa do console (Fase 18) consome os avisos e recarrega pela API quando perceber lacuna.
- Escalar a API horizontalmente passa a exigir backplane — requisito explícito para a Fase 25.

## Como isto é verificado

`TempoRealTestes`, com cliente SignalR real contra a API e o PostgreSQL reais:

- posição válida aparece no cliente conectado (critério de aceite); posição atrasada ou recusada não avisa;
- saída para rota e entrada na geofence avisam com o evento certo; alteração de dados e comando recusado não avisam;
- sem token, token de motorista e token adulterado: 401; token na query string conecta no hub e não
  autentica rota da API; chamada a método do hub é recusada; token fora do log;
- duas organizações conectadas: cada console só recebe a própria;
- conexão derrubada pelo servidor: o cliente reconecta e volta a receber;
- logout derruba a conexão, a reconexão é recusada e nada mais chega.

`AutorizacaoTestes` cobre o hub no mapa de rotas com a política do console.
