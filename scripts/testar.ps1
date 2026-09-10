<#
.SYNOPSIS
    Executa as suítes de teste da Torre Logística.

.DESCRIPTION
    Os projetos de teste usam xunit.v3 sobre o Microsoft.Testing.Platform, em que
    cada suíte é um executável próprio. Este script compila uma vez e roda cada
    executável, somando os resultados e devolvendo código de saída diferente de zero
    se qualquer suíte falhar.

    Por que não `dotnet test`: no SDK 10.0.400 com xunit.v3 4.0.0 o wrapper
    `dotnet test` encerra com "Zero testes executados" (código 5) embora o mesmo
    executável encontre e rode todos os testes quando invocado direto. Rodar o
    executável é caminho suportado pela plataforma de teste e não esconde falha
    nenhuma: o código de saída continua reprovando a execução.

.PARAMETER Suite
    unit | integration | architecture | all (padrão)

.PARAMETER SemCompilar
    Reaproveita a saída de compilação existente.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/testar.ps1
    powershell -ExecutionPolicy Bypass -File scripts/testar.ps1 -Suite unit
#>
[CmdletBinding()]
param(
    [ValidateSet('unit', 'integration', 'architecture', 'all')]
    [string]$Suite = 'all',

    [switch]$SemCompilar
)

$ErrorActionPreference = 'Stop'

$raiz = Split-Path -Parent $PSScriptRoot

$projetos = [ordered]@{
    unit         = 'tests/TorreLogistica.UnitTests'
    architecture = 'tests/TorreLogistica.ArchitectureTests'
    integration  = 'tests/TorreLogistica.IntegrationTests'
}

if ($Suite -eq 'all') {
    $selecionados = @($projetos.Keys)
} else {
    $selecionados = @($Suite)
}

# ---------------------------------------------------------------------------
# Qual `dotnet` usar
#
# Uma máquina pode ter mais de uma instalação do .NET, e a que está no PATH não é
# necessariamente a que atende ao global.json. Escolher aqui a instalação que tem
# SDK 10 evita o erro "SDK not found" e, pior, o executável de teste subindo contra
# um runtime antigo.
# ---------------------------------------------------------------------------
function Resolver-Dotnet {
    $candidatos = New-Object System.Collections.Generic.List[string]

    if ($env:TORRE_DOTNET) { $candidatos.Add($env:TORRE_DOTNET) }

    $comando = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($comando) { $candidatos.Add($comando.Source) }

    if ($env:USERPROFILE) {
        $candidatos.Add((Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'))
    }
    if ($env:HOME) {
        $candidatos.Add((Join-Path $env:HOME '.dotnet/dotnet'))
    }

    foreach ($candidato in $candidatos) {
        if (-not (Test-Path $candidato)) { continue }

        $sdks = & $candidato --list-sdks
        if ($LASTEXITCODE -ne 0) { continue }

        if ($sdks | Where-Object { $_ -match '^10\.' }) {
            return $candidato
        }
    }

    throw ("Nenhuma instalação do .NET com SDK 10 foi encontrada. " +
           "Defina TORRE_DOTNET apontando para o dotnet correto.")
}

$dotnet = Resolver-Dotnet
Write-Host "==> dotnet: $dotnet" -ForegroundColor DarkGray

# O executável de teste é um apphost: ele resolve o runtime pelo DOTNET_ROOT, não
# pelo `dotnet` que compilou. Sem isto, a suíte pode tentar subir num runtime antigo.
$env:DOTNET_ROOT = Split-Path -Parent $dotnet

# A CLI do Docker aceita `npipe:////./pipe/docker_engine` (quatro barras), mas a
# biblioteca usada pelo Testcontainers só reconhece a forma com duas barras e falha
# com "The endpoint is not a npipe URI". Normalizar aqui evita que a suíte de
# integração pareça quebrada por causa do formato da variável de ambiente.
if ($env:DOCKER_HOST -and $env:DOCKER_HOST.StartsWith('npipe:////')) {
    $env:DOCKER_HOST = $env:DOCKER_HOST.Replace('npipe:////', 'npipe://')
    Write-Host "==> DOCKER_HOST normalizado para $($env:DOCKER_HOST)" -ForegroundColor DarkGray
}

if (-not $SemCompilar) {
    Write-Host '==> Compilando a solução' -ForegroundColor Cyan
    & $dotnet build (Join-Path $raiz 'TorreLogistica.slnx') --nologo -v minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Compilação falhou.' -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

$falhas = New-Object System.Collections.Generic.List[string]

foreach ($nome in $selecionados) {
    $projeto = Join-Path $raiz $projetos[$nome]

    Write-Host ''
    Write-Host "==> Suíte: $nome" -ForegroundColor Cyan

    & $dotnet run --project $projeto --no-build
    if ($LASTEXITCODE -ne 0) {
        $falhas.Add("$nome (código $LASTEXITCODE)")
    }
}

Write-Host ''
if ($falhas.Count -gt 0) {
    Write-Host ("Suítes com falha: " + ($falhas -join ', ')) -ForegroundColor Red
    exit 1
}

Write-Host 'Todas as suítes passaram.' -ForegroundColor Green
exit 0
