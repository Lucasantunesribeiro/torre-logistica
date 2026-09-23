# Imagem do simulador da operação.
#
# Construída a partir da raiz do repositório:
#   docker build -f src/TorreLogistica.Simulator/Dockerfile -t torre-logistica-simulador .
#
# O simulador encena as seis histórias contra a API real, autenticado, como qualquer integrador faria.
# Ele não tem `ProjectReference` nenhuma — nem para o domínio — e isso é a garantia estrutural de que
# não existe atalho: sem referência ao domínio, não há como escrever no banco mesmo querendo. A única
# porta de entrada que ele conhece é HTTP.
#
# Não é um serviço: roda, encena e sai com código 0. No compose da demonstração ele vive atrás de um
# `profile`, para subir a pilha não disparar a encenação por acidente.

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

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/TorreLogistica.Simulator/TorreLogistica.Simulator.csproj src/TorreLogistica.Simulator/

RUN dotnet restore -a $TARGETARCH src/TorreLogistica.Simulator/TorreLogistica.Simulator.csproj

COPY src/TorreLogistica.Simulator/ src/TorreLogistica.Simulator/

RUN dotnet publish src/TorreLogistica.Simulator/TorreLogistica.Simulator.csproj \
    -a $TARGETARCH \
    --configuration Release \
    --self-contained false \
    --no-restore \
    --output /publicado

# ---------------------------------------------------------------------------
# Execução
# ---------------------------------------------------------------------------
# `runtime` e não `aspnet`: o simulador faz chamadas HTTP, não atende nenhuma.
FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS execucao
WORKDIR /aplicacao

USER $APP_UID

COPY --from=compilacao /publicado .

# Nenhum EXPOSE, nenhuma porta: este processo só fala para fora.

ENTRYPOINT ["dotnet", "TorreLogistica.Simulator.dll"]
