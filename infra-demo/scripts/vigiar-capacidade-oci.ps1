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
    # O perfil PRECISA estar em ~/.oci/config: o provedor do Terraform nao honra
    # OCI_CLI_CONFIG_FILE, entao um arquivo proprio seria invisivel para ele.
    [string] $ArquivoDeConfiguracao = "$env:USERPROFILE\.oci\config",

    # Onde o JSON do shape e escrito. Fora do repositorio, junto da chave.
    [string] $DiretorioDeTrabalho = "$env:USERPROFILE\.oci\torre-watch",
    [string] $CaminhoDaCli = "$env:USERPROFILE\.oci-cli-venv\Scripts\oci.exe",

    [string] $Regiao = 'sa-saopaulo-1',
    [string] $DominioDeDisponibilidade = 'RFMs:SA-SAOPAULO-1-AD-1',

    # Sizing. Precisa bater com o que o Terraform vai pedir — perguntar por um tamanho e criar
    # outro faria o relatório responder sobre uma máquina que não é a nossa.
    [string] $Shape = 'VM.Standard.A1.Flex',
    [double] $Ocpus = 1,
    [double] $MemoriaGb = 4,

    # 5 minutos: nao sabemos quanto tempo uma janela de capacidade A1 dura, e 15 minutos pode
    # perder uma janela curta. Uma consulta a cada 5 minutos continua sendo cadencia leve.
    [int] $IntervaloEmMinutos = 5,
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
if (-not (Test-Path $DiretorioDeTrabalho)) { New-Item -ItemType Directory -Path $DiretorioDeTrabalho | Out-Null }
$arquivoDoShape = Join-Path $DiretorioDeTrabalho 'shape-consultado.json'
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
        $texto = ($saida | Out-String)
        # Throttling NAO e ausencia de capacidade, e nao e erro de configuracao: e a Oracle pedindo
        # para diminuir o ritmo. Tratar como "sem capacidade" esconderia uma janela aberta; tratar
        # como erro fatal encerraria a vigilia por um pedido de paciencia.
        if ($texto -match 'TooManyRequests|429|Too many requests') {
            return [pscustomobject]@{ Estado = 'THROTTLING'; Quantidade = $null; Detalhe = $texto.Trim() }
        }
        return [pscustomobject]@{ Estado = 'ERRO_NA_CONSULTA'; Quantidade = $null; Detalhe = $texto.Trim() }
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
        # `-var` e nao `TF_VAR_`: em Terraform, `terraform.tfvars` tem precedencia SOBRE as
        # variaveis de ambiente. O tfvars fixa perfil DEFAULT e SecurityToken, entao o env era
        # ignorado — e o plano rodava com a sessao humana sem ninguem perceber. Passou a falhar
        # com 401 no minuto em que aquela sessao expirou, revelando o engano.
        $varPerfil = "perfil_da_cli=$Perfil"
        $varAutenticacao = 'metodo_de_autenticacao=ApiKey'

        # `-refresh=false` e deliberado, e e consequencia do privilegio minimo.
        #
        # Este usuario nao le o compartimento, o orcamento nem a cota — e nao deve mesmo. Num plano
        # com refresh, o Terraform interpreta "nao consigo ler" como "nao existe" e propoe RECRIAR:
        # medido, o plano vinha com 2 create e 5 update, incluindo um compartimento DUPLICADO.
        #
        # Sem refresh, ele planeja contra o estado ja gravado, que o administrador validou. O unico
        # recurso ausente do estado e a instancia, e e so ela que aparece.
        Escrever "  gerando plano direcionado a $NomeDoRecurso (sem refresh)" 'Cyan'
        & terraform plan -input=false -no-color -refresh=false "-var" $varPerfil "-var" $varAutenticacao "-target=$NomeDoRecurso" "-out=$planoDoTerraform" | Out-Null
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

function Resumir {
    $duracao = (Get-Date) - $inicio
    return ("resumo: {0:hh\:mm\:ss} de vigilia | {1} consultas | {2} sem capacidade | " +
            "{3} disponivel | {4} throttling | {5} tentativas reais de criacao | {6} erros inesperados") -f
           $duracao, $contagem.Consultas, $contagem.SemCapacidade, $contagem.Disponivel,
           $contagem.Throttling, $contagem.TentativasReais, $contagem.ErrosInesperados
}

# ---------------------------------------------------------------------------
# Laço
# ---------------------------------------------------------------------------
Escrever "=== vigilia de capacidade iniciada ===" 'White'
Escrever "shape $Shape  $Ocpus OCPU  $MemoriaGb GB  |  $Regiao  $DominioDeDisponibilidade"
Escrever "intervalo $IntervaloEmMinutos min  |  duracao maxima $DuracaoMaximaEmHoras h  |  simulacao: $($Simular.IsPresent)"
Escrever "registro em $ArquivoDeRegistro"

$limite = (Get-Date).AddHours($DuracaoMaximaEmHoras)
$inicio = Get-Date
$ciclo = 0

# Contadores do relatorio final. Vigilia que roda 12 horas e nao sabe dizer o que viu nao serve
# para decidir a proxima janela.
$contagem = @{
    Consultas        = 0
    SemCapacidade    = 0
    Disponivel       = 0
    Throttling       = 0
    TentativasReais  = 0
    ErrosInesperados = 0
}
$errosSeguidos = 0
$esperaAtual = $IntervaloEmMinutos * 60

while ((Get-Date) -lt $limite) {
    $ciclo++
    if ($CiclosMaximos -gt 0 -and $ciclo -gt $CiclosMaximos) {
        Escrever "teto de $CiclosMaximos ciclo(s) atingido" 'White'
        break
    }

    $resultado = Consultar-Capacidade
    $contagem.Consultas++
    if ($EstadoSimulado) {
        Escrever "  (estado real: $($resultado.Estado) — sobreposto por -EstadoSimulado $EstadoSimulado)" 'Yellow'
        $resultado = [pscustomobject]@{ Estado = $EstadoSimulado; Quantidade = $null; Detalhe = $null }
    }
    $quantidade = if ($null -ne $resultado.Quantidade) { $resultado.Quantidade } else { '-' }

    switch ($resultado.Estado) {
        'AVAILABLE' {
            Escrever "ciclo $ciclo  AVAILABLE  (disponiveis: $quantidade)" 'Green'
            $contagem.Disponivel++
            $errosSeguidos = 0
            $esperaAtual = $IntervaloEmMinutos * 60
            $contagem.TentativasReais++
            $desfecho = Invocar-GateDeCriacao
            if ($desfecho -eq 'CRIADA') {
                Escrever "=== vigilia encerrada: VM criada. PARE AQUI — nao configure nada ===" 'Green'
                Escrever (Resumir)
                exit 0
            }
            if ($desfecho -eq 'PARAR') {
                Escrever "=== vigilia encerrada por seguranca ===" 'Red'
                Escrever (Resumir)
                exit 2
            }
            if ($desfecho -eq 'SIMULADO') {
                Escrever "=== simulacao concluida: o ramo AVAILABLE funciona e nada foi criado ===" 'Yellow'
                exit 0
            }
        }
        'OUT_OF_HOST_CAPACITY' {
            # Nao chama o Terraform. E a razao de existir desta ferramenta.
            $contagem.SemCapacidade++
            $errosSeguidos = 0
            $esperaAtual = $IntervaloEmMinutos * 60
            Escrever "ciclo $ciclo  OUT_OF_HOST_CAPACITY  — sem chamar o Terraform"
        }
        'THROTTLING' {
            # Backoff: dobra a espera, com teto de 30 minutos. Nunca acelera.
            $contagem.Throttling++
            $esperaAtual = [Math]::Min($esperaAtual * 2, 1800)
            Escrever "ciclo $ciclo  THROTTLING (429) - sem chamar o Terraform; proxima consulta em $([int]($esperaAtual/60)) min" 'Yellow'
        }
        default {
            $contagem.ErrosInesperados++
            $errosSeguidos++
            Escrever "ciclo $ciclo  estado inesperado: $($resultado.Estado)  (seguidos: $errosSeguidos)" 'Yellow'
            if ($resultado.Detalhe) { Escrever "    $($resultado.Detalhe.Substring(0, [Math]::Min(200, $resultado.Detalhe.Length)))" 'Yellow' }
            if ($errosSeguidos -ge 5) {
                Escrever "=== vigilia encerrada: 5 erros inesperados seguidos ===" 'Red'
                Escrever (Resumir)
                exit 3
            }
        }
    }

    if ($CiclosMaximos -gt 0 -and $ciclo -ge $CiclosMaximos) { continue }
    Start-Sleep -Seconds $esperaAtual
}

Escrever "=== vigilia encerrada sem capacidade na janela de $DuracaoMaximaEmHoras h ===" 'White'
Escrever (Resumir)
exit 1
