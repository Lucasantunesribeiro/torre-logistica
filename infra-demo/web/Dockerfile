# Borda da demonstração: as três aplicações web compiladas e o Caddy que as serve.
#
# Construída a partir da raiz do repositório:
#   docker build -f infra-demo/web/Dockerfile -t torre-logistica-web .
#
# Uma imagem só para as três aplicações e o proxy, e não quatro, porque as três são arquivo estático:
# separá-las em contêineres próprios traria três processos de servidor de arquivo para servir o que um
# já serve, e três vezes mais memória num orçamento de 4 GB.
#
# O que NÃO está aqui é tão deliberado quanto o que está: a API e os workers têm imagem própria. Esta
# imagem nunca executa código do domínio.

# ---------------------------------------------------------------------------
# Compilação das três aplicações
# ---------------------------------------------------------------------------
# `--platform=$BUILDPLATFORM`: este estágio roda na arquitetura de quem constrói, sempre. O que ele
# produz é JavaScript, CSS e HTML — que não têm arquitetura. Emular o Node em arm64 para gerar um
# arquivo idêntico seria pagar meia hora de QEMU por byte nenhum de diferença.
FROM --platform=$BUILDPLATFORM node:22-alpine@sha256:b6f26b36c8ff49624cfdac716b8ea1138d606df02586a77d364bb5536a634f85 AS compilacao
WORKDIR /origem

# Endereço público da API. Chega como argumento porque o Vite embute a configuração no pacote no
# momento da build — não há como decidir isso quando o contêiner sobe.
#
# Não há valor padrão de propósito. Um padrão aqui seria um endereço inventado embutido no JavaScript
# que vai para o navegador do visitante, e o sintoma seria a tela carregar e nada responder.
ARG TORRE_URL_DA_API
ARG TORRE_URL_DO_ESTILO_DO_MAPA=""

# Só os manifestos primeiro: a instalação das dependências só é refeita quando elas mudam, e não a
# cada linha de componente alterada.
COPY package.json package-lock.json ./
COPY apps/operacao/package.json apps/operacao/
COPY apps/motorista/package.json apps/motorista/
COPY apps/rastreamento/package.json apps/rastreamento/

# `npm ci` e não `npm install`: instala exatamente o que o lockfile diz. `install` pode resolver
# versão nova e fazer a imagem de hoje diferir da de ontem sem nenhuma mudança no repositório.
RUN npm ci

COPY apps/ apps/
# O tsconfig da raiz, do qual os das três aplicações herdam. Sem ele, o compilador de tipos não
# acha a configuração base e a build falha antes de gerar um arquivo sequer.
COPY tsconfig.base.json ./

ENV VITE_URL_DA_API=$TORRE_URL_DA_API
ENV VITE_URL_DO_ESTILO_DO_MAPA=$TORRE_URL_DO_ESTILO_DO_MAPA

# A build falha sozinha se `VITE_URL_DA_API` vier vazia — `apps/*/src/infra/ambiente.ts` valida a
# configuração na importação do módulo. A verificação abaixo existe para a mensagem ser sobre o que
# realmente falta, e não um erro de Zod no meio de um log de bundler.
RUN test -n "$TORRE_URL_DA_API" \
    || (echo "ERRO: informe --build-arg TORRE_URL_DA_API=https://api.torre.lucasafvr.com.br" >&2 && exit 1)

RUN npm run build --workspaces

# ---------------------------------------------------------------------------
# Serviço
# ---------------------------------------------------------------------------
FROM caddy:2.10-alpine@sha256:4c6e91c6ed0e2fa03efd5b44747b625fec79bc9cd06ac5235a779726618e530d

# Os três pacotes publicados. Cada um na sua raiz: são aplicações distintas, com sessão e público
# distintos, e compartilhar diretório é como um caminho relativo errado vira vazamento de tela.
COPY --from=compilacao /origem/apps/operacao/dist      /srv/operacao
COPY --from=compilacao /origem/apps/motorista/dist     /srv/motorista
COPY --from=compilacao /origem/apps/rastreamento/dist  /srv/rastreamento

COPY infra-demo/Caddyfile /etc/caddy/Caddyfile

# Sem HEALTHCHECK aqui: quem define a sonda é o compose, junto com a política de reinício. Duas
# definições de saúde para o mesmo contêiner divergem na primeira vez que alguém mexe em uma.
