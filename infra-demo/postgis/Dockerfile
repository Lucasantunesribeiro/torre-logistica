# PostgreSQL 17 + PostGIS para arm64 (e amd64).
#
# ---------------------------------------------------------------------------
# Por que este arquivo precisa existir
# ---------------------------------------------------------------------------
# A imagem que o projeto usa em desenvolvimento e nos testes — `postgis/postgis:17-3.5` — não roda
# na máquina da demonstração. O README do projeto PostGIS é explícito:
#
#     Supported architecture: amd64 (x86-64)
#
# e a inspeção do manifesto confirma, sem margem para dúvida:
#
#     docker manifest inspect postgis/postgis:17-3.5  →  linux/amd64  (e nada mais)
#
# A máquina da demonstração é Ampere A1, que é aarch64. Sem este Dockerfile, não há PostGIS lá —
# e sem PostGIS não há geofence, não há `ST_DWithin`, não há metade do produto.
#
# ---------------------------------------------------------------------------
# Por que não uma imagem de terceiro
# ---------------------------------------------------------------------------
# Existem imagens PostGIS multiarquitetura publicadas por terceiros. A mais citada, `imresamu/postgis`,
# é um fork de quem contribui com o projeto oficial — e se descreve, na própria página, como
# "Status: Experimental, under active development" e "(test) Docker image".
#
# Banco de uma demonstração pública não roda sobre imagem que o próprio autor marca como teste.
#
# O que este arquivo faz é exatamente o que o Dockerfile oficial do PostGIS faz — instalar os pacotes
# PGDG sobre a imagem oficial do PostgreSQL — com a única diferença de ser construído também para
# arm64. A receita é a de cima; a arquitetura é a que precisamos.
#
# ---------------------------------------------------------------------------
# Por que bookworm e não bullseye
# ---------------------------------------------------------------------------
# O Dockerfile oficial do PostGIS 17-3.5 parte de `postgres:17-bullseye`. Bullseye é Debian 11, cujo
# suporte LTS terminou em agosto de 2026 — antes desta fase. Partir de uma base que não recebe mais
# correção de segurança para economizar uma diferença de versão menor é troca ruim.
#
# Bookworm custa uma diferença de versão do PostGIS: o repositório PGDG para bookworm oferece 3.6.x,
# não 3.5.x. Essa diferença **não fica no escuro** — ver `infra-demo/README.md`, seção "Equivalência
# geoespacial", onde a suíte geoespacial de integração é executada contra esta imagem.

# A imagem oficial do PostgreSQL, fixada por digest: tag pode ser reapontada para outro conteúdo,
# digest não. `17-bookworm` publica linux/amd64 e linux/arm64/v8.
FROM postgres:17-bookworm@sha256:639ab7ceb90e13123085b741fb31ef493fba25463002f6da665352e7b534b652

LABEL org.opencontainers.image.title="torre-postgis" \
      org.opencontainers.image.description="PostgreSQL 17 + PostGIS para a demonstração da Torre Logística, com suporte a arm64" \
      org.opencontainers.image.source="https://github.com/postgis/docker-postgis"

# Versão congelada. Sem o `=`, uma reconstrução meses depois traria outro PostGIS sem ninguém pedir —
# e diferença de versão de banco geoespacial aparece como comportamento estranho de geofence, não
# como erro de build.
ENV POSTGIS_MAJOR=3
ENV POSTGIS_VERSION=3.6.4+dfsg-2.pgdg12+1

# `apt-cache showpkg` antes do install é herança do Dockerfile oficial e tem serventia real: quando a
# versão fixada sai do repositório, o log da build mostra quais existem, em vez de só dizer que não
# achou. É a diferença entre "atualize o pino para 3.6.5" e uma hora de investigação.
RUN apt-get update \
      && apt-cache showpkg "postgresql-$PG_MAJOR-postgis-$POSTGIS_MAJOR" \
      && apt-get install -y --no-install-recommends \
           ca-certificates \
           "postgresql-$PG_MAJOR-postgis-$POSTGIS_MAJOR=$POSTGIS_VERSION" \
           "postgresql-$PG_MAJOR-postgis-$POSTGIS_MAJOR-scripts=$POSTGIS_VERSION" \
      && rm -rf /var/lib/apt/lists/*

# Nenhum script de inicialização criando a extensão, e isso é deliberado: quem cria `postgis` é a
# migration `20260909221821_Fundacao`, pelo mesmo caminho em todos os ambientes. Criar a extensão
# aqui faria a demonstração ser o único lugar onde ela já existe antes da migration rodar — ou seja,
# o único lugar onde esse passo da migration nunca é exercitado.
