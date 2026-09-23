# Imagem da API da Torre Logística.
#
# Construída a partir da raiz do repositório:
#   docker build -f src/TorreLogistica.Api/Dockerfile -t torre-logistica-api .
#
# Neutra quanto a fornecedor de propósito: a mesma imagem sobe em qualquer serviço que rode contêiner,
# e a escolha da nuvem (Fase 25) não obriga a reescrever nada aqui.

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

# Primeiro só o que descreve as dependências. Assim a camada de restauração só é refeita quando um
# projeto ou uma versão muda — e não a cada linha de código alterada.
# O .editorconfig vem junto: ele carrega as regras dos analisadores, e o projeto compila com aviso
# tratado como erro. Sem ele, a compilação dentro do contêiner reprova por regras que a solução
# configurou para não valerem — como exigir que exceção termine em "Exception" num domínio em português.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/TorreLogistica.Domain/TorreLogistica.Domain.csproj src/TorreLogistica.Domain/
COPY src/TorreLogistica.Application/TorreLogistica.Application.csproj src/TorreLogistica.Application/
COPY src/TorreLogistica.Infrastructure/TorreLogistica.Infrastructure.csproj src/TorreLogistica.Infrastructure/
COPY src/TorreLogistica.Api/TorreLogistica.Api.csproj src/TorreLogistica.Api/

RUN dotnet restore -a $TARGETARCH src/TorreLogistica.Api/TorreLogistica.Api.csproj

COPY src/TorreLogistica.Domain/ src/TorreLogistica.Domain/
COPY src/TorreLogistica.Application/ src/TorreLogistica.Application/
COPY src/TorreLogistica.Infrastructure/ src/TorreLogistica.Infrastructure/
COPY src/TorreLogistica.Api/ src/TorreLogistica.Api/

RUN dotnet publish src/TorreLogistica.Api/TorreLogistica.Api.csproj \
    -a $TARGETARCH \
    --configuration Release \
    --self-contained false \
    --no-restore \
    --output /publicado

# ---------------------------------------------------------------------------
# Execução
# ---------------------------------------------------------------------------
# Alpine porque o projeto compila com InvariantGlobalization: sem ICU para carregar, a imagem Debian
# levaria dezenas de megabytes de biblioteca de globalização que ninguém abriria.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS execucao
WORKDIR /aplicacao

# Diretório do storage local, criado enquanto ainda somos root e entregue ao usuário da aplicação.
# Antes não existia: o padrão do adaptador era uma pasta sob /aplicacao, que pertence ao root, e o
# processo sem privilégio só descobria isso ao gravar o primeiro comprovante — em produção, horas
# depois de subir. Fica em /var/tmp por ser o lugar do padrão de diretórios do Linux para dado
# temporário que sobrevive a reinício de processo, e não em /tmp, que alguns runtimes montam em RAM.
ENV Torre__Armazenamento__Diretorio=/var/tmp/torre-logistica/armazenamento
RUN mkdir -p /var/tmp/torre-logistica/armazenamento \
    && chown -R "$APP_UID" /var/tmp/torre-logistica

# Usuário sem privilégio, fornecido pela própria imagem. Processo de borda rodando como root é o tipo
# de detalhe que só importa no dia em que alguém encontra uma falha de execução remota.
USER $APP_UID

COPY --from=compilacao /publicado .

# A porta vem do ambiente porque cada serviço gerenciado injeta a sua. 8080 é o padrão das imagens
# .NET sem privilégio — abaixo de 1024 exigiria root.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "TorreLogistica.Api.dll"]
