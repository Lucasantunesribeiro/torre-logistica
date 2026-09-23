# Imagem do processo de trabalho da Torre Logística.
#
# Construída a partir da raiz do repositório:
#   docker build -f src/TorreLogistica.Workers/Dockerfile -t torre-logistica-workers .
#
# É o par da imagem da API, e a diferença entre as duas é deliberada: esta não abre porta nenhuma. Ela
# não atende requisição — ela despacha outbox, reavalia previsão, abre alerta, apaga rastro vencido e
# mede o estado da operação. Nada disso depende de alguém acessar o sistema.

# ---------------------------------------------------------------------------
# Compilação
# ---------------------------------------------------------------------------
# `--platform=$BUILDPLATFORM` prende o estágio de compilação à arquitetura de QUEM constrói, e
# `-a $TARGETARCH` diz ao .NET para gerar código para a arquitetura de DESTINO.
#
# Sem as duas linhas, construir para arm64 numa máquina x86 faz o Docker emular o SDK inteiro por
# QEMU. Funciona, e é impraticável: a restauração e a publicação de um único projeto passaram de
# uma hora e meia sem terminar, medido nesta máquina. Com compilação cruzada o SDK roda nativo e só
# o resultado é arm64 — que é o caminho documentado pela Microsoft para imagem multiarquitetura.
#
# `ARG TARGETARCH` é preenchido pelo próprio buildx: "amd64" ou "arm64", conforme `--platform`.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS compilacao
ARG TARGETARCH
WORKDIR /origem

# O .editorconfig vem junto porque carrega as regras dos analisadores, e o projeto compila com aviso
# tratado como erro. Sem ele, a compilação reprova por regras que a solução configurou para não valerem.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/TorreLogistica.Domain/TorreLogistica.Domain.csproj src/TorreLogistica.Domain/
COPY src/TorreLogistica.Application/TorreLogistica.Application.csproj src/TorreLogistica.Application/
COPY src/TorreLogistica.Infrastructure/TorreLogistica.Infrastructure.csproj src/TorreLogistica.Infrastructure/
COPY src/TorreLogistica.Workers/TorreLogistica.Workers.csproj src/TorreLogistica.Workers/

RUN dotnet restore -a $TARGETARCH src/TorreLogistica.Workers/TorreLogistica.Workers.csproj

COPY src/TorreLogistica.Domain/ src/TorreLogistica.Domain/
COPY src/TorreLogistica.Application/ src/TorreLogistica.Application/
COPY src/TorreLogistica.Infrastructure/ src/TorreLogistica.Infrastructure/
COPY src/TorreLogistica.Workers/ src/TorreLogistica.Workers/

RUN dotnet publish src/TorreLogistica.Workers/TorreLogistica.Workers.csproj \
    -a $TARGETARCH \
    --configuration Release \
    --self-contained false \
    --no-restore \
    --output /publicado

# ---------------------------------------------------------------------------
# Execução
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS execucao
WORKDIR /aplicacao

# Mesmo diretório de storage da API: este processo também registra a camada de infraestrutura, então
# a validação de subida cobra um caminho gravável dele também. Criado como root, entregue ao usuário.
ENV Torre__Armazenamento__Diretorio=/var/tmp/torre-logistica/armazenamento
RUN mkdir -p /var/tmp/torre-logistica/armazenamento \
    && chown -R "$APP_UID" /var/tmp/torre-logistica

# Runtime, e não ASP.NET Core: este processo não sobe servidor HTTP, e carregar o que não se usa é
# superfície a mais para nada.
USER $APP_UID

COPY --from=compilacao /publicado .

# Nenhum EXPOSE: sem porta aberta, não há como alguém alcançar este processo de fora — nem por engano
# de configuração de rede.

ENTRYPOINT ["dotnet", "TorreLogistica.Workers.dll"]
