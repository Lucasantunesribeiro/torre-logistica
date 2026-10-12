# Evidências — Portfólio 95

Registro do trabalho feito a partir da auditoria `AUDITORIA_PORTFOLIO_95.md` (nota 78/100, 07–08/10/2026),
que apontou nove frentes (TL-01 a TL-09) e mandou **começar pelo SSRF de webhooks** e **não** por telas ou
módulos novos. A entrega foi em duas ondas, cada uma subindo no GitHub e sendo implantada na VM da demonstração.

Este documento é a trilha de provas: o que mudou, por qual commit, e como cada afirmação é verificada — por
teste automatizado, pela CI contra stack real, por auditoria de dependências ou por medição.

## Como verificar

```bash
# Backend (SDK 10 + Docker): unidade, arquitetura e integração (PostGIS real via Testcontainers)
powershell -ExecutionPolicy Bypass -File scripts/testar.ps1

# Frontend (Node 24): lint, tipos, testes e build dos três apps
npm ci && npm run lint && npm run typecheck && npm run test && npm run build

# Segurança
npm audit --audit-level=high && npm audit --omit=dev --audit-level=high
dotnet list src/TorreLogistica.Api/TorreLogistica.Api.csproj package --vulnerable --include-transitive

# Benchmark de ingestão (sob demanda)
TORRE_CARGA=1 TORRE_CARGA_RELATORIO=carga.json <exe de integração> -class "*CargaDeIngestaoTestes"
```

A CI (`.github/workflows/ci.yml`) roda em todo push para `main`: `backend`, `frontend`, `seguranca` e
`contrato` (sobe a stack de demonstração e valida os cinco contratos consumidor contra a API real).

## Onda 1 — segurança e ganhos rápidos

### TL-01 — Webhooks não alcançam a rede interna (P0) · `e9cb7b0`

O cliente de webhook validava o DNS, mas deixava escapar IPv4 mapeado em IPv6 (`::ffff:10.0.0.5`,
`::ffff:169.254.169.254`) e resolvia o nome de novo ao conectar (TOCTOU). Correções:

- `DestinoDeWebhook.EhEnderecoPublico` normaliza IPv4-mapeado antes da política e aplica política de IPv6
  explícita (loopback, link/site/unique-local, multicast, não-especificado, IPv4-compatível).
- `ConectorDeWebhook` fixa a conexão: resolve, revalida **cada** endereço no instante de conectar e conecta
  **só** ao aprovado — fecha o DNS rebinding. Proxy desligado no handler (`UseProxy=false`, `Proxy=null`)
  para nenhum proxy do processo/ambiente desviar a conexão da política.

Prova: `DestinoDeWebhookTestes` (classificação de ~28 endereços), `ConectorDeWebhookTestes` (destino
**efetivamente** conectado, incl. rebinding barrado), `WebhooksTestes` (integração, HMAC, política, retries).

### TL-04 — Dependências e ambiente · `bb1aa76`

Duas altas de desenvolvimento (brace-expansion, source-map-js) resolvidas pelos pais, sem `--force`.
`npm audit --audit-level=high` e `--omit=dev` limpos; Node 24; gate de engine mantido.

### TL-05 — Mapa fora do carregamento inicial · `4a1f9cc`

`MapaDaOperacao` passou a `React.lazy` + `<Suspense>` com limite de erro de chunk. Bundle inicial do console
caiu de **1.497 kB → 425 kB**; o MapLibre vive agora no chunk da rota `/mapa`. (Isso é _divisão de código por
rota_.)

### TL-07 — Documentação factual · `3936d4c`, `17be58c`

Corrigidos "nada provisionado" (a demo está no ar), a descrição do simulador (sob demanda, não "opera
sozinha"), o escopo do pentest (não prova completa de SSRF) e as contagens de teste com data e execução de CI.

## Onda 2 — demo viva, mapa e provas

### TL-02 — Conexão × idade do dado × encenação · `b475708`

A demo é sob demanda e reexecutável; a encenação termina e os dados envelhecem com a API no ar. O console
mostrava só a luz da conexão, e "conectado" era lido como "ao vivo". Correção:

- `GET /api/operacao/pulso` (leitura, recortado por tenant): idade da posição atual mais recente, medida
  contra o relógio do servidor.
- `IndicadorDaOperacao`, ao lado do `IndicadorDeConexao`: _ao vivo_ / _recente_ / _encenação encerrada_ /
  _sem operação_.
- Simulador registra a etiqueta da execução (`demo-SEMENTE-CARIMBO`) no início e no fim.

Prova: `OperacaoTestes` (PostGIS real — pulso vazio, pulso com posição, isolamento de tenant, exige sessão de
leitura), `App.test.tsx` → _Pulso da operação_ (conexão verde convive com "encerrada há 2 dias"). Decisão na
ADR 0032 e no guia do simulador.

### TL-03 — Basemap Protomaps/PMTiles self-host · `3271a18`

Mapa de fundo próprio, custo recorrente US$ 0,00: um `.pmtiles` servido pela borda sob `/tiles`, lido por
requisições Range, sem servidor de tiles, sem chave, sem DNS novo.

- App registra o protocolo `pmtiles://` no MapLibre, mostra atribuição (OpenStreetMap/Protomaps) e, se o
  estilo falhar, cai uma vez para o fundo neutro avisando — lista, painel e navegação seguem de pé. O
  `pmtiles` entra no chunk preguiçoso do mapa; bundle inicial inalterado (425 kB).
- `style.json` gerado do tema oficial (`@protomaps/basemaps`), caminhos relativos a `/tiles`.
- Caddy serve `/tiles/*` com Range (`file_server`), config validada; compose monta `…/mapa:ro`. Binários do
  basemap provisionados na máquina — ver `infra-demo/mapa/README.md`.

### TL-06 — Offline, tempo real e privacidade (provas)

Verificação dirigida reusando a suíte existente, toda contra PostGIS real:

- **Offline / idempotência:** `SincronizacaoTestes` — conclusão/comprovante/posição offline, sincronização
  exatamente uma vez, conflito durante o período offline.
- **Tempo real:** `TempoRealTestes` — publicação de posição/estado/alerta pelo canal, efeito pós-commit.
- **Privacidade:** `RastreamentoPublicoTestes` (token forte, posição aproximada, nada administrativo) e
  `RedacaoDeLogTestes` (nenhuma coordenada/segredo em log).

Execução local destes quatro conjuntos: **25 testes, 0 falhas**. Passagem em navegador pelas três interfaces:
ver seção _Verificação na demonstração_.

### TL-08 — Desempenho

Benchmark `CargaDeIngestaoTestes` (sob demanda, `TORRE_CARGA=1`, **não roda na CI nem bloqueia a entrega** —
é alvo de referência, não promessa comercial; CLAUDE.md §70). Medido em 12/10/2026, máquina de
desenvolvimento Windows + Docker Desktop, com 1,6 M posições semeadas no histórico:

```text
Carga: 990 posições em 35,3 s → 28,1/s (meta 33/s).
Latência da ingestão: p50 30,1 ms · p95 53,6 ms · p99 6114 ms · máx 6121 ms.
Recusadas pela política: 0.
Lista do mapa (entregas em rota): p50 4,0 ms · p95 4,7 ms.
Posição atual de um motorista: p50 2,9 ms · p95 3,5 ms.
Banco: 1,6 M posições, 60 projeções, tabela de 652 MB.
Planos (volume de um dia):
  posição atual por motorista → Index Only Scan (pk_motoristas) · 0,03 ms
  histórico por período       → Bitmap Index Scan (ix_posicoes_motorista_id_capturada_em) · 6,69 ms
  limpeza por retenção        → Index Scan (ix_posicoes_recebida_em) · 2,60 ms
```

**Leitura honesta:** o caminho de **leitura** — o que o console e o mapa tocam o tempo todo — está saudável e
indexado (listas e posição atual com p95 abaixo de 5 ms; planos usando os índices certos). A **escrita sob
estresse** nesta máquina ficou abaixo do piso: 28,1/s contra os 31,35/s exigidos, com pico de cauda no p99
(~6 s). Isso é artefato do ambiente — Docker Desktop virtualiza o IO e a máquina semeava 1,6 M linhas em
paralelo — e **não** regressão de código (nada em ingestão ou índices mudou nesta entrega); por isso as
asserções do benchmark foram mantidas como estão, sem afrouxamento. A carga real da demo é de poucos
motoristas, muito abaixo desse estresse; o pior caso da própria VM Always Free está medido à parte em
`infra-demo/` (shape E2).

### TL-09 — Roteiro, interface e evidências

Este documento. Capturas em `docs/assets/screenshots`; checagem responsiva (390/768/1280) e de
teclado/foco na passagem em navegador.

## Verificação na demonstração

Deploy da Onda 2 na VM (12/10/2026), pelo caminho `docker save | ssh docker load` + `up -d --no-build`
(a VM não constrói imagem), com as imagens anteriores guardadas em `:anterior` para reversão:

- Imagens no ar: `api 218275`, `workers c03666`, `web 882888` (digests novos; as `:anterior` seguem na VM).
- `GET /health/ready` e `/health/live`: **200**. Três superfícies (`operacao`, `motorista`, `rastrear`): **200**.
- Contratos: o job `contrato` da CI valida os cinco esquemas — incluindo o novo `/api/operacao/pulso` — contra
  a API real levantada na pipeline.

Passagem em navegador (console, como operador da demonstração):

- **TL-02 ao vivo.** Com a encenação encerrada, o rodapé mostrou *Operação conectada* (verde) **e** *Encenação
  encerrada · último dado há 13 dias* lado a lado — a correção em ação. Ao rodar a encenação na VM, o mesmo
  indicador virou *Operação ao vivo · dado de há 5 s*. É a distinção que a auditoria pedia, provada na tela.
- **TL-03 ao vivo.** O Mapa da Operação desenha o basemap vetorial de Campinas (cidades, ruas, água, áreas
  verdes, rótulos) servido do próprio domínio sob `/tiles` por PMTiles, com o motorista e o destino por cima e
  a atribuição *© OpenStreetMap · Protomaps* visível. Captura em
  [`docs/assets/screenshots/console-mapa-basemap.jpg`](assets/screenshots/console-mapa-basemap.jpg).

### Dois defeitos que só a verificação no navegador pegou (TL-03)

O teste automatizado não renderiza WebGL; a passagem no navegador encontrou e corrigiu:

1. **Estilo com a flavor errada.** O `style.json` fora gerado passando a flavor como string (`"light"`) em vez
   do objeto `namedFlavor("light")`, e as cores saíam `null` — o MapLibre recusava o estilo inteiro. Corrigido;
   o estilo é a saída do tema oficial, sem nenhum `null`. URLs do estilo passaram a **absolutas** (o MapLibre v6
   exige sprite/glyphs absolutos).
2. **Worker do MapLibre v6 não subia sob o Vite.** A causa de o mapa pintar só o fundo, sem erro no console: o
   MapLibre v6 é ESM e não descobre sozinho a URL do próprio Web Worker, que é quem busca e decodifica os tiles.
   O bug ficava escondido enquanto o mapa usava só o fundo neutro (sem fonte). Corrigido empacotando o worker
   (`?worker&url` + `worker.format: 'es'`) e registrando-o com `setWorkerUrl` — os tiles passaram a ser pedidos
   (header + 4 tiles na primeira vista) e o basemap renderizou.

O fallback endurecido provou seu valor durante tudo isso: com o estilo quebrado, o console nunca travou — lista,
pontos e navegação seguiram, com aviso honesto. A reação ao erro foi trocada por **espera com prazo** (cai ao
fundo neutro só se o estilo não carregar a tempo), para um tile lento não neutralizar um mapa bom.

## Segurança

- `npm audit --audit-level=high`: **0 vulnerabilidades**; `--omit=dev` idem.
- Varredura de segredos no commit (`varredura-segredos.py`) + gitleaks/GitGuardian na CI.
- Nenhuma credencial em código, commit ou documento.

## Limitações nomeadas

- O basemap só renderiza depois de os arquivos `.pmtiles`/glyphs/sprite serem provisionados na máquina; sem
  eles o console usa o fundo neutro — por desenho, nunca quebra.
- As histórias B e C do simulador dependem de temporizadores do servidor e não aparecem num teste rápido
  (ADR 0032); são encenadas e verificadas pela saída, não afirmadas além disso.
- Medição de desempenho é o benchmark local de ingestão; a VM Always Free tem seu pior caso medido à parte
  em `infra-demo/` (shape E2).
