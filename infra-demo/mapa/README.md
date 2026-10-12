# Basemap self-host (Protomaps / PMTiles)

O Mapa da Operação usa um basemap **self-host**: um único arquivo `.pmtiles` servido pela própria borda
(Caddy), sob `/tiles`, lido pelo navegador por requisições Range. Sem servidor de tiles, sem conta, sem
chave, sem DNS novo — e custo recorrente de **US$ 0,00**. Ver [ADR 0035](../../docs/adr/0035-demonstracao-de-custo-zero.md).

> **Os binários do mapa NÃO ficam no repositório.** Este diretório versiona só o `style.json` (gerado, abaixo)
> e este guia. O `.pmtiles`, os glyphs e o sprite são provisionados na máquina, fora do git e fora da imagem.

## O que a borda serve

O `handle_path /tiles/*` do [`Caddyfile`](../Caddyfile) mapeia `/tiles/...` para o diretório
`/srv/mapa` do contêiner `web`, montado (somente leitura) de `${TORRE_DIRETORIO_DADOS}/mapa` no hospedeiro
(ver [`docker-compose.demo.yml`](../docker-compose.demo.yml)). O `file_server` do Caddy responde a Range por
padrão — condição do protocolo PMTiles.

Estrutura esperada em `${TORRE_DIRETORIO_DADOS}/mapa` (padrão `/srv/torre-logistica/mapa`):

```text
mapa/
  style.json                      # versionado neste diretório; copiar para a máquina
  campinas.pmtiles                # o basemap (recorte regional)
  sprite/
    light.json  light.png  light@2x.json  light@2x.png
  glyphs/
    <fontstack>/<range>.pbf       # p.ex. "Noto Sans Regular/0-255.pbf"
```

O `style.json` referencia tudo por caminho **relativo à raiz** (`/tiles/...`), então o mesmo arquivo serve
em qualquer ambiente, sem domínio embutido.

## Provisionar (na sua máquina, depois enviar)

A VM Always Free tem 1 GB de RAM e 50 GB de disco; **gere o recorte na sua máquina** e envie só o resultado
(dezenas de MB), em vez de baixar o planeta inteiro na VM.

### 1. O `.pmtiles` (recorte de Campinas)

Com o [CLI `pmtiles`](https://github.com/protomaps/go-pmtiles/releases) (binário único, sem instalar nada):

```bash
# Recorta a regiao de Campinas de uma build diaria publica do basemap Protomaps (derivado do OSM).
# bbox: oeste,sul,leste,norte
pmtiles extract https://build.protomaps.com/20251001.pmtiles campinas.pmtiles \
  --bbox=-47.20,-23.05,-46.85,-22.72 --maxzoom=14
```

O recorte de Campinas em z0–14 fica na casa de algumas dezenas de MB — confortável no disco da VM. Confira a
retenção e o espaço livre junto da medição do TL-08 antes de enviar algo muito maior.

### 2. Glyphs e sprite

Do repositório de assets do Protomaps ([`protomaps/basemaps-assets`](https://github.com/protomaps/basemaps-assets)),
que casa com o tema da versão usada para gerar o `style.json` (ver abaixo):

```bash
# fontes (glyphs) -> mapa/glyphs/<fontstack>/<range>.pbf
cp -r basemaps-assets/fonts/*        mapa/glyphs/
# sprite do tema claro -> mapa/sprite/light.{json,png,@2x.json,@2x.png}
mkdir -p mapa/sprite && cp basemaps-assets/sprites/v4/light* mapa/sprite/
```

### 3. Enviar e montar

```bash
# style.json vem deste repositorio; o resto vem do passo acima.
cp infra-demo/mapa/style.json mapa/
scp -r mapa/*  usuario@maquina:/srv/torre-logistica/mapa/
```

A montagem já está no compose. Depois de enviar, recriar o `web` basta (`up -d --no-build web`); como os
arquivos são estáticos e montados, **não** é preciso reconstruir imagem para trocá-los — só para mudar
`TORRE_URL_DO_ESTILO_DO_MAPA`, que é build-time.

### 4. Ligar no app

No `.env.demo`:

```bash
TORRE_URL_DO_ESTILO_DO_MAPA=https://operacao.torre.lucasafvr.com.br/tiles/style.json
```

Reconstruir a imagem `web` (a URL é embutida na build), enviar e recriar.

## Regerar o `style.json`

O `style.json` é a saída do tema oficial do Protomaps, com fonte, glyphs e sprite apontados para `/tiles`:

```bash
npm install --no-save @protomaps/basemaps@5.7.2
node -e '
  const { layers, namedFlavor } = require("@protomaps/basemaps");
  const estilo = {
    version: 8,
    glyphs: "/tiles/glyphs/{fontstack}/{range}.pbf",
    sprite: "/tiles/sprite/light",
    sources: { protomaps: {
      type: "vector",
      url: "pmtiles:///tiles/campinas.pmtiles",
      attribution: "© OpenStreetMap · Protomaps",
    } },
    // namedFlavor("light"), NAO a string "light": a string faz as cores saírem null e o MapLibre recusa o estilo.
    layers: layers("protomaps", namedFlavor("light"), { lang: "pt" }),
  };
  require("fs").writeFileSync("infra-demo/mapa/style.json", JSON.stringify(estilo, null, 2) + "\n");
'
```

## Robustez e atribuição

- **Ausência não quebra.** Sem os arquivos (ou com a variável vazia), o Mapa da Operação cai para um fundo
  neutro e desenha os pontos por cima; a lista, o painel e a navegação seguem funcionando. O
  `MapaDaOperacao.tsx` escuta o erro de carregamento do estilo e troca para o fundo neutro uma vez,
  avisando na lateral.
- **Atribuição visível.** O `style.json` carrega o crédito a OpenStreetMap e Protomaps na fonte do mapa, e o
  controle de atribuição do MapLibre o exibe — exigência das duas licenças.
- **Privacidade.** O rastreamento público continua com posição aproximada; o basemap não muda isso.
