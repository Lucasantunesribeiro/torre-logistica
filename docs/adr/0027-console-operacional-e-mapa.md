# ADR 0027 — Console operacional: densidade sobre ornamento, e mapa que não depende de fornecedor

**Status:** aceito — Fase 18
**Decisores:** time técnico
**Relacionados:** [0003](./0003-tres-aplicacoes-web.md), [0017](./0017-tempo-real-da-operacao.md), [0020](./0020-pwa-do-motorista.md), [0024](./0024-rastreamento-publico.md), [0026](./0026-webhooks-e-backbone-assincrono.md)

## Contexto

Até aqui o console tinha login e um painel de identidade: tudo o que as dezesseis fases anteriores
construíram só era visível por chamada de API. Esta fase transforma isso em produto — e o critério de
aceite é temporal: **entender o estado da operação em poucos segundos**.

Duas decisões carregam risco e precisam ficar registradas: o mapa exige um renderizador e um provedor de
tiles, e o tempo real exige um cliente de transporte.

## Decisão

### O mapa funciona sem fornecedor contratado

MapLibre GL é o renderizador (direção que o ROADMAP fixou; é OSS, sem chave e sem servidor próprio). O
**estilo** vem de `VITE_URL_DO_ESTILO_DO_MAPA`, e **sem ele o mapa desenha os pontos sobre fundo neutro**.

Isso não é degradação aceita a contragosto: é o que mantém o console utilizável — e o projeto sem custo —
enquanto a decisão de infraestrutura não chega (Fase 25). O que a operação precisa ver são os pontos e a
relação entre eles; o mapa de fundo é contexto, não informação. Quem contratar um provedor aponta a
variável e ganha o fundo, sem tocar em código.

Nenhuma chave foi inventada, nenhum serviço pago foi contratado e nenhum endereço de provedor foi
embutido como padrão — padrão embutido é conta que alguém paga sem saber.

### Duas dependências novas, ambas justificadas

| Pacote | Por que não dá para evitar |
|---|---|
| `maplibre-gl` | renderizar mapa vetorial com zoom, camadas e projeção é trabalho de anos; a alternativa real seria não ter mapa |
| `@microsoft/signalr` | é o cliente do protocolo que o servidor já fala desde a Fase 8 (ADR 0017); reimplementar negociação, reconexão e fallback seria reescrever a biblioteca |

Ambas são gratuitas, mantidas e sem serviço pago atrás. Versões fixadas exatas, como o resto do projeto.

### Aviso invalida consulta; posição entra direto

O tempo real **não** remenda o cache: ao receber `DeliveryStatusChanged`, `AlertCreated` ou
`IncidentCreated`, o console invalida a consulta correspondente e relê da API. A fonte da verdade continua
sendo o servidor, e a tela não constrói uma segunda versão dos fatos a partir de fragmentos de evento.

`DriverPositionUpdated` é a exceção deliberada: chega muitas vezes por minuto e alimenta o mapa
diretamente. Reler a lista de entregas a cada posição seria desperdício puro, e posição é o único dado em
que o próprio aviso já é a informação completa.

Sem tempo real a tela continua correta — apenas mais lenta para perceber mudanças. A falha de conexão é
anunciada no mapa em vez de fingir dados ao vivo.

### Densidade é a regra de UX

Tabela em vez de cartão, linha fina em vez de sombra, cor só onde carrega significado (bom, atenção,
ruim). Sem vidro fosco, sem gradiente, sem espaço decorativo. Quem opera compara muitas linhas por minuto:
cartão espaçado obriga a rolar para ver o que cabe numa tela.

O painel de abertura não tem gráfico. Ele responde a uma pergunta só — "há algo exigindo ação agora?" — e
para isso contagem e fila bastam. Série histórica e indicadores são da Fase 19.

### Estado de tela em um lugar só

Carregando, erro e vazio moram num componente compartilhado. Repetir os três em onze telas faria cada uma
errar de um jeito diferente — e a que errasse mostraria tabela vazia como se fosse resposta.

As consultas de uma mesma tela são independentes: no detalhe da entrega, comprovante e previsão podem não
existir ainda, e a falta de um não pode apagar o resto da tela de quem está com o cliente ao telefone.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Embutir um provedor de tiles como padrão | conta que alguém paga sem saber, e chave no pacote publicado |
| Google Maps ou Mapbox | custo por carregamento e chave obrigatória para funcionar |
| Sem mapa, só listas | o ROADMAP define o Mapa da Operação como a tela símbolo do produto |
| Remendar o cache com os avisos | a tela passaria a manter uma segunda versão dos fatos, que diverge na primeira mensagem perdida |
| Polling em vez de SignalR | o servidor já publica; trocar por consulta periódica desperdiçaria o que a Fase 8 construiu |
| Gráficos no painel | indicadores com série são da Fase 19; aqui gráfico seria enfeite sem ação associada |

## Como a decisão é verificada

| Afirmação | Prova |
|---|---|
| O console abre sem provedor de mapa | teste do mapa: a tela monta, lista as entregas a caminho e avisa que não há provedor |
| Navegação e sessão | testes de sessão: recuperação por cookie, login, saída e rota inexistente |
| Painel responde "há algo agora?" | teste dos contadores e da fila; e o caso vazio, que diz que a operação está limpa |
| Detalhe reúne o que as fases construíram | teste que abre a entrega e confere previsão explicada, ocorrência e ausência de comprovante |
| Link de rastreamento (pendência da Fase 15) | teste que emite e confere que o valor aparece |
| Reenvio de webhook (pendência da Fase 17) | teste que dispara o reenvio da entrega falhada |
| Resolver alerta (pendência da Fase 10) | teste que resolve com observação e confere o corpo enviado |
| Mapa e tempo real não quebram o teste | duplos de MapLibre e SignalR: jsdom não tem WebGL nem WebSocket, e o que se prova aqui é a tela |
