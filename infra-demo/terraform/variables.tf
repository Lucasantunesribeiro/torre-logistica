# Variáveis da infraestrutura de demonstração.
#
# Regra que atravessa este arquivo: nenhum padrão pode levar a recurso cobrado. Onde um valor maior
# sairia do nível Always Free, existe validação que recusa o valor — falha de plano é barata,
# fatura não é.

# ---------------------------------------------------------------------------
# Conta e região
# ---------------------------------------------------------------------------

variable "ocid_da_tenancy" {
  description = "OCID da tenancy. Aparece em Perfil → Tenancy no console da Oracle."
  type        = string

  validation {
    condition     = startswith(var.ocid_da_tenancy, "ocid1.tenancy.")
    error_message = "O OCID da tenancy começa com 'ocid1.tenancy.'."
  }
}

variable "regiao" {
  description = <<-TEXTO
    Região onde a máquina roda. Precisa ser a região de origem (home region) da conta:
    a capacidade Always Free de Ampere A1 só existe nela.
  TEXTO
  type        = string
}

variable "perfil_da_cli" {
  description = "Perfil dentro de ~/.oci/config usado para autenticar."
  type        = string
  default     = "DEFAULT"
}

variable "metodo_de_autenticacao" {
  description = <<-TEXTO
    Como o provedor autentica.

      ApiKey        par de chaves gerado localmente, com a chave pública colada no console da
                    Oracle. Não expira.
      SecurityToken token de sessão obtido por `oci session authenticate`, que abre o navegador.
                    Nada de chave para gerenciar; em troca o token expira.

    Se o perfil foi criado por `oci session authenticate`, este valor PRECISA ser SecurityToken —
    senão o provedor procura uma `key_file` que aquele perfil não tem, e a mensagem de erro fala de
    chave ausente em vez de falar do método errado.
  TEXTO
  type        = string
  default     = "ApiKey"

  validation {
    condition     = contains(["ApiKey", "SecurityToken", "InstancePrincipal"], var.metodo_de_autenticacao)
    error_message = "Use ApiKey, SecurityToken ou InstancePrincipal."
  }
}

variable "dominio_de_disponibilidade" {
  description = <<-TEXTO
    Nome do domínio de disponibilidade onde criar a máquina, por exemplo "abcd:SA-SAOPAULO-1-AD-1".
    Vazio deixa o Terraform escolher o primeiro da região.

    Vale conhecer o nome: a capacidade de Ampere A1 no nível gratuito é disputada, e a recusa
    ("Out of host capacity") costuma ser de um domínio específico, não da região inteira.
  TEXTO
  type        = string
  default     = ""
}

# ---------------------------------------------------------------------------
# Dimensionamento — os limites do Always Free em forma de validação
# ---------------------------------------------------------------------------

variable "ocpus" {
  description = <<-TEXTO
    OCPUs da máquina. O nível Always Free cobre 1.500 OCPU-hora por mês:
    2 OCPUs × 730 h = 1.460 OCPU-hora, dentro do limite com 40 h de folga.
    3 OCPUs × 730 h = 2.190 OCPU-hora — passa do limite e vira cobrança.
  TEXTO
  type        = number
  default     = 2

  validation {
    condition     = var.ocpus >= 1 && var.ocpus <= 2
    error_message = "Acima de 2 OCPUs o consumo mensal ultrapassa as 1.500 OCPU-hora gratuitas."
  }
}

variable "memoria_em_gb" {
  description = <<-TEXTO
    Memória da máquina. O nível Always Free cobre 9.000 GB-hora por mês, o que comportaria 12 GB.
    O padrão é 4 GB por duas razões, e nenhuma é economia de cota:

      1. A pilha medida cabe em 4 GB com folga (ver `infra-demo/README.md`). Memória alocada e não
         usada não acelera nada.
      2. Máquina Always Free ociosa pode ser recuperada pela Oracle, e um dos critérios é uso de
         memória abaixo de 20%%. Com 12 GB alocados e ~1,7 GB em uso, a pilha ficaria em 14%% —
         dentro da faixa de ociosa. Com 4 GB, fica em torno de 43%%.

    Pedir menos memória é, aqui, o que protege a máquina.
  TEXTO
  type        = number
  default     = 4

  validation {
    condition     = var.memoria_em_gb >= 2 && var.memoria_em_gb <= 12
    error_message = "Acima de 12 GB o consumo mensal ultrapassa as 9.000 GB-hora gratuitas."
  }
}

variable "tamanho_do_disco_em_gb" {
  description = <<-TEXTO
    Tamanho do volume de inicialização. O nível Always Free cobre 200 GB de Block Volume somados;
    50 GB é o padrão de uma instância e deixa margem para um segundo experimento.
  TEXTO
  type        = number
  default     = 50

  validation {
    condition     = var.tamanho_do_disco_em_gb >= 50 && var.tamanho_do_disco_em_gb <= 200
    error_message = "O mínimo do volume de inicialização é 50 GB e o total gratuito é 200 GB."
  }
}

# ---------------------------------------------------------------------------
# Acesso
# ---------------------------------------------------------------------------

variable "chave_publica_ssh" {
  description = <<-TEXTO
    Conteúdo da chave pública SSH que poderá entrar na máquina (o arquivo .pub inteiro).
    Nunca a chave privada.
  TEXTO
  type        = string

  validation {
    condition     = can(regex("^(ssh-ed25519|ssh-rsa|ecdsa-sha2-) ", var.chave_publica_ssh))
    error_message = "Informe o conteúdo do arquivo .pub, que começa com ssh-ed25519, ssh-rsa ou ecdsa-sha2-."
  }
}

variable "cidr_de_administracao" {
  description = <<-TEXTO
    De quais endereços a porta 22 aceita conexão. É o único acesso administrativo da máquina.

    Não existe padrão. `0.0.0.0/0` funcionaria e deixaria o SSH exposto ao mundo inteiro — com
    chave, sim, mas também com todo varredor automático da internet batendo nele o dia todo.
    Se o seu endereço muda, use a faixa do seu provedor, não o mundo.
  TEXTO
  type        = string

  validation {
    condition     = can(cidrnetmask(var.cidr_de_administracao))
    error_message = "Informe uma faixa CIDR válida, por exemplo 203.0.113.10/32."
  }
}

# ---------------------------------------------------------------------------
# Guarda-corpos financeiros
# ---------------------------------------------------------------------------

variable "email_do_alerta_de_custo" {
  description = "Para onde vai o aviso caso a conta registre qualquer gasto."
  type        = string

  validation {
    condition     = can(regex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", var.email_do_alerta_de_custo))
    error_message = "Informe um endereço de e-mail válido."
  }
}

variable "limite_do_orcamento_em_dolares" {
  description = <<-TEXTO
    Valor do orçamento, em dólares. O mínimo aceito pelo serviço é 1.

    Atenção ao que isto é e ao que não é: orçamento na OCI AVISA, não BLOQUEIA. O bloqueio de
    verdade vem das cotas de compartimento, definidas em `main.tf` — elas fazem o provisionamento
    falhar, que é o comportamento que queremos.
  TEXTO
  type        = number
  default     = 1

  validation {
    condition     = var.limite_do_orcamento_em_dolares >= 1
    error_message = "O serviço de orçamento da Oracle não aceita valor abaixo de 1."
  }
}

# ---------------------------------------------------------------------------
# Nomes
# ---------------------------------------------------------------------------

variable "prefixo" {
  description = "Prefixo dos nomes dos recursos."
  type        = string
  default     = "torre-demo"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{2,20}$", var.prefixo))
    error_message = "Use minúsculas, números e hífen, começando por letra, com 3 a 21 caracteres."
  }
}

variable "shape_da_demo" {
  description = <<-TEXTO
    Shape da máquina da demonstração.

    `VM.Standard.A1.Flex` é o preferido: 1 OCPU inteira e 4 GB. Ficou indisponível — 143 consultas
    ao Compute Capacity Report em 12 horas seguidas, todas `OUT_OF_HOST_CAPACITY`.

    `VM.Standard.E2.1.Micro` é o contorno: x86-64, 1 GB, 1/8 de OCPU com burst não garantido. A
    pilha foi medida e cabe, mas é menor em toda dimensão. Não é arquitetura recomendada para
    cliente real — é o que mantém a demonstração pública no ar por US$ 0,00.
  TEXTO
  type        = string
  default     = "VM.Standard.E2.1.Micro"

  validation {
    condition     = contains(["VM.Standard.A1.Flex", "VM.Standard.E2.1.Micro"], var.shape_da_demo)
    error_message = "Só estes dois shapes são Always Free e foram validados: VM.Standard.A1.Flex e VM.Standard.E2.1.Micro."
  }
}

variable "cota_de_nucleos_a1" {
  description = <<-TEXTO
    Núcleos de `standard-a1-core-count` que a cota do compartimento libera.

    É um conceito SEPARADO de `ocpus`. `ocpus` dimensiona a máquina quando o shape ativo é A1;
    esta variável diz quanto a cota autoriza. Elas coincidem hoje, mas confundi-las faria a cota
    do A1 desaparecer no dia em que a demo passasse a usar outro shape — e a permissão de criar
    A1 sumiria junto, silenciosamente.
  TEXTO
  type        = number
  default     = 1
}

variable "cota_de_nucleos_e2_micro" {
  description = <<-TEXTO
    Núcleos de `standard-e2-micro-core-count` que a cota do compartimento libera.

    O limite da tenancy é 2 e o LinkGuardião já usa 1, medido em 28/09/2026: `used = 1`,
    `available = 1`. Um é tudo o que existe, e é tudo o que esta demonstração pede.

    Escopo do limite é Availability Domain. NÃO existe variante `-regional-count` documentada
    para este shape, e inventá-la por analogia com o A1 seria repetir o erro já cometido com
    `standard-a1-memory-count`, que a OCI recusou por não ser nome de cota.
  TEXTO
  type        = number
  default     = 1
}
