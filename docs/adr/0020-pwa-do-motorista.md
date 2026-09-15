# ADR 0020 — PWA do motorista: leitura própria, ações grandes e GPS só em primeiro plano

- **Data:** 2026-09-15
- **Status:** aceita
- **Fase:** 11
- **Complementa:** [ADR 0003](./0003-tres-aplicacoes-web.md), [ADR 0009](./0009-autenticacao-e-sessao.md), [ADR 0015](./0015-ingestao-de-localizacao.md)

## Contexto

A Fase 11 cria a experiência móvel do motorista, separada do console. O ROADMAP pede as telas de login,
rota do dia, lista de paradas, próxima entrega, detalhe, iniciar rota, chegada, tentativa frustrada,
concluir, ocorrências e estado de conexão. As ações críticas devem ser simples e grandes, sem painel
administrativo comprimido. A coleta de GPS deve usar as APIs web possíveis, com as limitações de segundo
plano documentadas e sem fingir capacidade.

O critério de aceite é que o motorista complete o fluxo básico usando somente a PWA.

O que existia:

- **sessão** do canal do motorista, com token curto e renovação por cookie próprio (Fase 1);
- **comandos** de execução: iniciar e concluir rota, chegada, conclusão e tentativa sem sucesso (Fase 5);
- **ingestão** de posições em lote com identificador do aparelho (Fase 6);
- **casca** mobile da PWA (Fase 0).

**Faltava ao motorista qualquer leitura:** a API do canal só tinha comandos, e as rotas de leitura de rota
e entrega eram do console, com a política do console.

## Decisão

### Leitura própria do motorista

| Rota | Devolve |
|---|---|
| `GET /api/motorista/rotas` | rotas **planejadas ou em andamento** do motorista da sessão, a em andamento primeiro, com total e pendentes |
| `GET /api/motorista/rotas/{id}` | a rota com as paradas ativas em ordem: destinatário, endereço, janela e status — mais veículo e hub |
| `GET /api/motorista/entregas/{id}` | a entrega: destinatário com **telefone e instruções**, endereço, coordenada, janela, observações, rota e sequência, execução e tentativas |

- **Modelo próprio, e não o do console.** Sem cliente, versão, auditoria, criação ou dados de outras rotas.
  Contato e instruções só aparecem no detalhe da entrega, que é o que o motorista abre para fazer aquela
  entrega.
- **Só o que é dele.** O motorista é resolvido pela conta da sessão, e a rota ou entrega de outro responde
  404, igual a inexistente. A entrega que **já foi dele** e passou a outro motorista responde
  `409 entrega_reatribuida`, como os comandos (ADR 0014), para o aplicativo explicar o que aconteceu.
- Rota em montagem, cancelada ou concluída não aparece: o motorista não vê estrutura mudando por baixo
  dele, e rota encerrada sai da lista do dia.

### Sessão no canal do motorista

O mesmo desenho do console (ADR 0009), no canal `/api/motorista/autenticacao`:

- token de acesso só em memória, nunca em `localStorage`;
- renovação por cookie HttpOnly, uma por vez, com um único 401 renovado e repetido;
- ao sair, o cache de consultas é limpo, para nada da rota ficar para quem pegar o aparelho depois.

O código da sessão é paralelo ao do console, de propósito. As aplicações são implantadas separadamente
(ADR 0003), e um pacote compartilhado só se paga com um terceiro consumidor com sessão; o rastreamento
público não tem login.

### Telas e UX

| Tela | Caminho | Ação principal |
|---|---|---|
| Entrar | `/entrar` | entrar |
| Rota do dia e próxima entrega | `/` | iniciar rota · abrir entrega · encerrar rota quando tudo tem resultado |
| Lista de paradas | `/rotas/{id}/paradas` | abrir qualquer parada |
| Detalhe da entrega | `/entregas/{id}` | cheguei ao destino · entrega concluída (com confirmação) · não foi possível entregar · abrir no mapa · ligar |
| Registrar ocorrência | `/entregas/{id}/ocorrencia` | motivo tipado obrigatório → registrar tentativa sem sucesso |
| Estado de conexão | cabeçalho | sem internet no aparelho · operação conectada · operação acordando · inalcançável |

- **Uma coluna e ações que ocupam a largura**, com alvo de toque de pelo menos 3,5rem (os campos, 3rem).
  Fonte de 16px, para o navegador não dar zoom no campo, e contraste alto.
- **Concluir pede confirmação**, porque é a ação que não se desfaz. A tentativa sem sucesso exige escolher
  o motivo, e é essa escolha que serve de confirmação.
- **Ocorrência com motivo tipado**, nunca texto livre como única informação. Nesta fase, a ocorrência que o
  domínio registra é a tentativa sem sucesso com os cinco motivos existentes. O detalhe mostra o histórico
  de tentativas. Os tipos de ocorrência além da tentativa (veículo, mercadoria, severidade) são da Fase 13.
- **Erro com instrução.** Cada falha vira uma frase curta pela situação: reatribuída, sessão encerrada, sem
  conexão, operação com problema, ação que não vale mais. Comando recusado atualiza a tela na hora.
- **Navegação.** "Abrir no mapa" entrega o destino ao aplicativo de mapas do aparelho por link. Nenhuma
  chave, nenhum custo, e o destino só sai do aparelho quando o motorista toca.

### GPS: o que o navegador garante, e só isso

- **Quando coleta.** `navigator.geolocation.watchPosition` com alta precisão, **só com rota em andamento e o
  aplicativo aberto**. Fora da execução da rota, o aplicativo nem pede a permissão (coleta mínima,
  ADR 0015).
- **Como envia.** Em lote, a cada 15 s. Cada posição nasce com UUIDv7 e sequência crescente no aparelho, de
  modo que o reenvio é seguro e o servidor descarta a duplicata. Falha de envio mantém o lote em memória,
  limitado a 200 posições, com as mais antigas saindo primeiro. Item recusado pelo servidor não é
  reenviado.
- **Estados mostrados ao motorista:** aguardando permissão, ativa (com o horário do último envio e quantas
  aguardam conexão), bloqueada, indisponível e sem sinal.

| Limitação real | Consequência | O que o aplicativo faz |
|---|---|---|
| Não existe API web de localização em segundo plano | com a tela bloqueada, o app em segundo plano ou a aba escondida, o navegador suspende ou espaça as leituras (Android e iOS) | avisa: "Mantenha o aplicativo aberto durante a rota" |
| Permissão negada não se pede de novo por código | só o motorista libera nas configurações do navegador | explica onde liberar; as entregas continuam funcionando |
| Precisão varia (GPS, rede, Wi-Fi) | posições imprecisas não movem a posição atual (ADR 0015) | envia a precisão informada; o servidor decide |
| Background Sync e Periodic Background Sync não são garantidos | não servem de base para rastreamento | não são usados |
| Fechar o navegador perde o que está em memória | posições não enviadas se perdem | fila persistente é da Fase 12 (IndexedDB) |

A consequência operacional fica dita: a operação vê o motorista enquanto o aplicativo está aberto. O alerta
de motorista offline (ADR 0019) existe exatamente para quando isso não acontece.

### Instalável, sem service worker nesta fase

Manifesto (`display: standalone`, `start_url: /`, cor de tema, ícone SVG) e metadados de aparelho. **Não há
service worker nesta fase**: cache offline, fila persistente e sincronização são o escopo da Fase 12, e um
service worker de cache sem a fila faria o aplicativo abrir sem rede e falhar em seguida em cada ação. Em
navegadores que exigem service worker para oferecer instalação, a instalação chega com a Fase 12.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Liberar as leituras do console para o token de motorista | Exporia cliente, versão e dados de outras rotas, e misturaria autoridades (CLAUDE.md, seção 14) |
| Uma leitura só, "tudo do dia" | Contato e instruções de todas as entregas numa chamada, e dados demais trafegando a cada atualização |
| Guardar o token para sobreviver ao recarregar | `localStorage` é legível por qualquer script; a renovação por cookie já recupera a sessão |
| Prometer rastreamento em segundo plano | O navegador não garante; o ROADMAP proíbe fingir capacidade |
| Coletar localização desde o login | Coleta além da execução da rota (ADR 0015) |
| Service worker de cache já nesta fase | Sem a fila da Fase 12, abriria offline e falharia em cada ação |
| Pacote compartilhado de sessão entre console e PWA | Dois consumidores; o custo de versionar e publicar não se paga ainda |
| Mapa embutido na PWA | Custo e dependência de provedor sem necessidade; o aplicativo de mapas do aparelho navega melhor |

## Consequências

- A Fase 12 acrescenta service worker, fila em IndexedDB com `ClientOperationId` e sincronização. As
  telas e o rastreador já isolam o envio (`apiDoMotorista`, `RastreadorDeLocalizacao.descarregar`) para
  trocar a memória pela fila.
- A Fase 13 amplia a tela de ocorrência com os tipos novos, sem mudar o caminho do motorista.
- Os comandos continuam devolvendo o modelo do console, e a PWA ignora essa resposta e relê pela leitura
  própria. Reduzir esse retorno é compatível e pode entrar com a Fase 12, quando os comandos ganham
  `ClientOperationId`.

## Como isto é verificado

- `LeituraDoMotoristaTestes` (API e PostgreSQL reais):
  - **fluxo básico inteiro só pelas rotas que a PWA chama:** rota do dia, paradas em ordem sem dado do
    console, iniciar, detalhe com contato e instruções, chegada, conclusão, tentativa com motivo, pendentes
    zerados e rota encerrada saindo da lista;
  - leitura de outro motorista e de outra organização responde 404, e token do console, 401;
  - entrega reatribuída responde `409 entrega_reatribuida`.
- `AutorizacaoTestes`: as três rotas no mapa e na matriz por perfil.
- PWA, com Vitest e Testing Library:
  - `App.test.tsx`:
    - **critério de aceite** com servidor falso de estado: iniciar, próxima entrega, chegada, conclusão
      com confirmação, próxima, ocorrência com motivo, encerrar rota e estado vazio, na ordem exata dos
      comandos;
    - login pelo canal do motorista;
    - lista de paradas e detalhe, com links de mapa e telefone;
    - sessão encerrada no meio da rota volta ao login;
    - reatribuída explicada;
    - comando recusado atualiza a tela;
    - falha com nova tentativa;
    - sem rota;
    - localização negada sem impedir a conclusão;
    - localização pedida só com rota em andamento;
    - sem internet;
    - sair;
    - tela inexistente.
  - `rastreador.test.ts`: identificador e sequência crescente, reenvio com os mesmos identificadores,
    permissão negada, sem sinal e volta, navegador sem geolocalização, limite de pendentes, parar.
  - `sessao.test.ts`: canal do motorista, token só em memória, renovação única, 401 renovado e sessão
    encerrada.
  - `uuidv7.test.ts` e `estilos.test.ts`, este último para responsividade: viewport, largura fluida, alvo
    de toque, sem largura fixa, manifesto.
