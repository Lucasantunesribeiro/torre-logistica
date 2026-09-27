<#
.SINOPSE
    Vigia a capacidade de Ampere A1 na Oracle Cloud e cria a VM da demonstração quando ela abrir.

.DESCRICAO
    Por que esta ferramenta existe: 38 tentativas de `terraform apply` em cinco janelas foram todas
    recusadas com "Out of host capacity". Tentar criar para descobrir se dá é caro e cego — cada
    ciclo custa uma chamada de criação e só responde sim ou não.

    A Oracle oferece a resposta direta: o **Compute Capacity Report**, que diz se existe capacidade
    para um shape específico num domínio de disponibilidade, sem criar nada. Esta vigília pergunta
    isso a cada 15 minutos e só chama o Terraform quando a resposta é AVAILABLE.

    A capacidade pode sumir entre o relatório e a criação. Isso é esperado e não é erro: o script
    volta a vigiar.

.SEGURANCA
    Nenhuma credencial neste arquivo. A autenticação vem de um arquivo de configuração da CLI da
    Oracle, apontado por -ArquivoDeConfiguracao, que por sua vez aponta para a chave privada. Nem o
    arquivo nem a chave são versionados.

    O usuário técnico usado aqui tem privilégio mínimo: lê capacidade, lê a rede e as imagens, e
    cria instância — apenas dentro do compartimento da demonstração. Ele não administra IAM, não
    enxerga a raiz da tenancy e não apaga a própria rede.

.EXEMPLO
    # Um ciclo só, sem criar nada — para conferir o mecanismo
    .\vigiar-capacidade-oci.ps1 -CompartimentoOcid ocid1.compartment... -CiclosMaximos 1 -Simular

.EXEMPLO
    # Vigília de 12 horas, checando a cada 15 minutos
    .\vigiar-capacidade-oci.ps1 -CompartimentoOcid ocid1.compartment...
#>

[CmdletBinding()]
param(
    # OCID do compartimento da demonstração. Não tem padrão: é o único valor que identifica a conta.
    [Parameter(Mandatory = $true)]
    [string] $CompartimentoOcid,

    [string] $Perfil = 'TORRE_WATCH',
    [string] $ArquivoDeConfiguracao = "$env:USERPROFILE\.oci\torre-watch\config",
    [string] $CaminhoDaCli = "$env:USERPROFILE\.oci-cli-venv\Scripts\oci.exe",

    [string] $Regiao = 'sa-saopaulo-1',
    [string] $DominioDeDisponibilidade = 'RFMs:SA-SAOPAULO-1-AD-1',

    # Sizing. Precisa bater com o que o Terraform vai pedir — perguntar por um tamanho e criar
    # outro faria o relatório responder sobre uma máquina que não é a nossa.
    [string] $Shape = 'VM.Standard.A1.Flex',
    [double] $Ocpus = 1,
    [double] $MemoriaGb = 4,

    [int] $IntervaloEmMinutos = 15,
    [int] $DuracaoMaximaEmHoras = 12,

    # Teto de ciclos, útil para conferir o mecanismo sem esperar 12 horas.
    [int] $CiclosMaximos = 0,

    [string] $DiretorioTerraform,
    [string] $NomeDoRecurso = 'oci_core_instance.torre',

    # Em simulação, o ramo AVAILABLE gera e inspeciona o plano, mas NÃO aplica.
    [switch] $Simular,

    # Força o estado que o relatório teria devolvido, para exercitar o ramo AVAILABLE quando a
    # capacidade real está esgotada. Só tem efeito junto com -Simular: sem essa trava, um valor
    # errado aqui faria a vigília tentar criar uma VM acreditando numa capacidade que não existe.
    [ValidateSet('', 'AVAILABLE', 'OUT_OF_HOST_CAPACITY')]
    [string] $EstadoSimulado = '',

    [string] $ArquivoDeRegistro
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Registro
# ---------------------------------------------------------------------------
if (-not $DiretorioTerraform) {
    $DiretorioTerraform = Join-Path (Split-Path -Parent $PSScriptRoot) 'terraform'
}
if (-not $ArquivoDeRegistro) {
    $ArquivoDeRegistro = Join-Path $PSScriptRoot 'vigilia-capacidade.log'
}

function Escrever {
    param([string] $Texto, [string] $Cor = 'Gray')
    $carimbo = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    $linha = "$carimbo  $Texto"
    Write-Host $linha -ForegroundColor $Cor
    Add-Content -Path $ArquivoDeRegistro -Value $linha -Encoding utf8
}

# ---------------------------------------------------------------------------
# Verificações de partida — falhar aqui é barato, falhar no meio da vigília não
# ---------------------------------------------------------------------------
if (-not (Test-Path $CaminhoDaCli)) { throw "CLI da Oracle nao encontrada em $CaminhoDaCli" }
if (-not (Test-Path $ArquivoDeConfiguracao)) { throw "Arquivo de configuracao nao encontrado em $ArquivoDeConfiguracao" }
if (-not (Test-Path $DiretorioTerraform)) { throw "Diretorio do Terraform nao encontrado em $DiretorioTerraform" }
if ($EstadoSimulado -and -not $Simular) { throw "-EstadoSimulado so e aceito junto com -Simular." }

# O JSON do shape é escrito ao lado do arquivo de configuração, fora do repositório: ele carrega o
# sizing, não segredo, mas vive junto do resto da automação para não sujar a árvore versionada.
$arquivoDoShape = Join-Path (Split-Path -Parent $ArquivoDeConfiguracao) 'shape-consultado.json'
$corpoDoShape = [pscustomobject]@{
    instanceShape       = $Shape
    instanceShapeConfig = [pscustomobject]@{ ocpus = $Ocpus; memoryInGBs = $MemoriaGb }
}
$semBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($arquivoDoShape, (ConvertTo-Json -InputObject @($corpoDoShape) -Depth 5), $semBom)

$env:SUPPRESS_LABEL_WARNING = 'True'

# ---------------------------------------------------------------------------
# Consulta de capacidade
# ---------------------------------------------------------------------------
function Consultar-Capacidade {
    $argumentos = @(
        'compute', 'compute-capacity-report', 'create',
        '--compartment-id', $CompartimentoOcid,
        '--availability-domain', $DominioDeDisponibilidade,
        '--shape-availabilities', ("file://" + ($arquivoDoShape -replace '\\', '/')),
        '--config-file', $ArquivoDeConfiguracao,
        '--profile', $Perfil,
        '--region', $Regiao
    )

    $saida = & $CaminhoDaCli @argumentos 2>&1
    if ($LASTEXITCODE -ne 0) {
        return [pscustomobject]@{ Estado = 'ERRO_NA_CONSULTA'; Quantidade = $null; Detalhe = ($saida | Out-String).Trim() }
    }

    try {
        $objeto = ($saida | Out-String) | ConvertFrom-Json
    } catch {
        return [pscustomobject]@{ Estado = 'RESPOSTA_ILEGIVEL'; Quantidade = $null; Detalhe = ($saida | Out-String).Trim() }
    }

    $disponibilidade = $objeto.data.'shape-availabilities'[0]
    return [pscustomobject]@{
        Estado     = $disponibilidade.'availability-status'
        Quantidade = $disponibilidade.'available-count'
        Detalhe    = $null
    }
}

# ---------------------------------------------------------------------------
# Gate de criação
# ---------------------------------------------------------------------------
# Só é chamado quando o relatório diz AVAILABLE. Gera um plano direcionado, confere que ele contém
# EXATAMENTE a criação da instância, e só então aplica aquele mesmo plano — sem gerar outro entre a
# conferência e a aplicação, que é onde uma diferença poderia entrar sem ninguém ver.
function Invocar-GateDeCriacao {
    $planoDoTerraform = Join-Path $DiretorioTerraform 'torre-capacidade.tfplan'

    Push-Location $DiretorioTerraform
    try {
        $env:OCI_CLI_CONFIG_FILE = $ArquivoDeConfiguracao
        $env:TF_VAR_perfil_da_cli = $Perfil
        $env:TF_VAR_metodo_de_autenticacao = 'ApiKey'

        Escrever "  gerando plano direcionado a $NomeDoRecurso" 'Cyan'
        & terraform plan -input=false -no-color "-target=$NomeDoRecurso" "-out=$planoDoTerraform" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Escrever "  PLANO FALHOU — vigilancia interrompida" 'Red'
            return 'PARAR'
        }

        $plano = (& terraform show -json $planoDoTerraform | Out-String) | ConvertFrom-Json
        $mudancas = @($plano.resource_changes | Where-Object { $_.change.actions -notcontains 'no-op' })

        $criacoes = @($mudancas | Where-Object { ($_.change.actions -join ',') -eq 'create' -and $_.type -eq 'oci_core_instance' })
        if ($mudancas.Count -ne 1 -or $criacoes.Count -ne 1) {
            Escrever "  PLANO INESPERADO: $($mudancas.Count) mudanca(s), $($criacoes.Count) criacao(oes) de instancia" 'Red'
            foreach ($m in $mudancas) { Escrever "    $($m.address): $($m.change.actions -join ',')" 'Red' }
            Escrever "  VIGILANCIA INTERROMPIDA — nada foi aplicado" 'Red'
            return 'PARAR'
        }

        Escrever "  plano conferido: apenas CREATE $($criacoes[0].address)" 'Green'

        if ($Simular) {
            Escrever "  SIMULACAO: o apply NAO sera executado" 'Yellow'
            return 'SIMULADO'
        }

        Escrever "  aplicando o mesmo plano conferido" 'Cyan'
        $saidaDoApply = & terraform apply -no-color $planoDoTerraform 2>&1
        if ($LASTEXITCODE -eq 0) {
            Escrever "  VM CRIADA" 'Green'
            return 'CRIADA'
        }

        $texto = ($saidaDoApply | Out-String)
        if ($texto -match 'Out of host capacity') {
            # A capacidade sumiu entre o relatorio e a criacao. Esperado; volta a vigiar.
            Escrever "  capacidade sumiu entre o relatorio e a criacao — voltando a vigiar" 'Yellow'
            return 'CONTINUAR'
        }

        Escrever "  APPLY FALHOU por motivo diferente de capacidade:" 'Red'
        ($texto -split "`n" | Where-Object { $_ -match '^Error' } | Select-Object -First 3) |
            ForEach-Object { Escrever "    $_" 'Red' }
        return 'PARAR'
    } finally {
        Pop-Location
    }
}

# ---------------------------------------------------------------------------
# Laço
# ---------------------------------------------------------------------------
Escrever "=== vigilia de capacidade iniciada ===" 'White'
Escrever "shape $Shape  $Ocpus OCPU  $MemoriaGb GB  |  $Regiao  $DominioDeDisponibilidade"
Escrever "intervalo $IntervaloEmMinutos min  |  duracao maxima $DuracaoMaximaEmHoras h  |  simulacao: $($Simular.IsPresent)"
Escrever "registro em $ArquivoDeRegistro"

$limite = (Get-Date).AddHours($DuracaoMaximaEmHoras)
$ciclo = 0

while ((Get-Date) -lt $limite) {
    $ciclo++
    if ($CiclosMaximos -gt 0 -and $ciclo -gt $CiclosMaximos) {
        Escrever "teto de $CiclosMaximos ciclo(s) atingido" 'White'
        break
    }

    $resultado = Consultar-Capacidade
    if ($EstadoSimulado) {
        Escrever "  (estado real: $($resultado.Estado) — sobreposto por -EstadoSimulado $EstadoSimulado)" 'Yellow'
        $resultado = [pscustomobject]@{ Estado = $EstadoSimulado; Quantidade = $null; Detalhe = $null }
    }
    $quantidade = if ($null -ne $resultado.Quantidade) { $resultado.Quantidade } else { '-' }

    switch ($resultado.Estado) {
        'AVAILABLE' {
            Escrever "ciclo $ciclo  AVAILABLE  (disponiveis: $quantidade)" 'Green'
            $desfecho = Invocar-GateDeCriacao
            if ($desfecho -eq 'CRIADA') {
                Escrever "=== vigilia encerrada: VM criada. PARE AQUI — nao configure nada ===" 'Green'
                exit 0
            }
            if ($desfecho -eq 'PARAR') {
                Escrever "=== vigilia encerrada por seguranca ===" 'Red'
                exit 2
            }
            if ($desfecho -eq 'SIMULADO') {
                Escrever "=== simulacao concluida: o ramo AVAILABLE funciona e nada foi criado ===" 'Yellow'
                exit 0
            }
        }
        'OUT_OF_HOST_CAPACITY' {
            # Nao chama o Terraform. E a razao de existir desta ferramenta.
            Escrever "ciclo $ciclo  OUT_OF_HOST_CAPACITY  — sem chamar o Terraform"
        }
        default {
            Escrever "ciclo $ciclo  estado inesperado: $($resultado.Estado)" 'Yellow'
            if ($resultado.Detalhe) { Escrever "    $($resultado.Detalhe.Substring(0, [Math]::Min(200, $resultado.Detalhe.Length)))" 'Yellow' }
        }
    }

    if ($CiclosMaximos -gt 0 -and $ciclo -ge $CiclosMaximos) { continue }
    Start-Sleep -Seconds ($IntervaloEmMinutos * 60)
}

Escrever "=== vigilia encerrada sem capacidade na janela de $DuracaoMaximaEmHoras h ===" 'White'
exit 1
