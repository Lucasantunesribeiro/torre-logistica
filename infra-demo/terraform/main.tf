# Infraestrutura da demonstração pública da Torre Logística, na Oracle Cloud.
#
# ---------------------------------------------------------------------------
# O que este arquivo cria — e por que cada item é gratuito
# ---------------------------------------------------------------------------
# A tabela completa, com a citação da documentação da Oracle para cada linha, está em
# `infra-demo/README.md`. O resumo:
#
#   compartimento         sem custo — é organização, não recurso
#   política de cotas     sem custo — e é o que FAZ o provisionamento pago falhar
#   orçamento + alerta    sem custo — AVISA, não bloqueia
#   VCN + sub-rede        sem custo — a conta pode ter até 2 VCNs
#   gateway de internet   sem custo
#   tabela de rotas       sem custo
#   lista de segurança    sem custo
#   IP público efêmero    sem custo na OCI, inclusive reservado
#   instância A1.Flex     2 OCPU × 730 h = 1.460 de 1.500 OCPU-hora gratuitas
#                         4 GB × 730 h = 2.920 de 9.000 GB-hora gratuitas
#   volume de boot 50 GB  de 200 GB gratuitos
#   tráfego de saída      de 10 TB/mês gratuitos
#
# O que este arquivo DELIBERADAMENTE não cria:
#
#   Load Balancer     o Caddy na própria máquina faz TLS, redirecionamento e proxy. O nível
#                     gratuito inclui um balanceador de 10 Mbps, mas um recurso que não é
#                     necessário é um recurso a mais para monitorar, e 10 Mbps seria o teto de
#                     toda a demonstração.
#   Object Storage    os comprovantes ficam no disco da máquina. O adaptador de objeto existe e
#                     está exercitado contra o Azure (`infra/`); aqui ele custaria um recurso a
#                     mais para guardar alguns megabytes.
#   NAT Gateway       a sub-rede é pública. Um NAT existiria para dar saída a uma sub-rede
#                     privada — e NAT Gateway NÃO está no nível gratuito.
#   Monitoring/Alarms além do que já vem ligado. Notificações têm franquia mensal, e alarme que
#                     ninguém lê é ruído com risco de ultrapassar franquia.

# ---------------------------------------------------------------------------
# Compartimento dedicado
# ---------------------------------------------------------------------------
# Tudo da Torre Logística vive aqui dentro, separado de qualquer outro projeto da conta. É o que
# torna possível dizer "apague este compartimento" sem qualificar a frase — e é o alvo das cotas.
resource "oci_identity_compartment" "torre" {
  compartment_id = var.ocid_da_tenancy
  name           = var.prefixo
  description    = "Demonstração pública da Torre Logística. Somente recursos Always Free."

  # O Terraform não apaga compartimento por padrão. Ligar isto é deliberado: um compartimento
  # órfão continua aparecendo em toda listagem e em todo relatório de custo da conta.
  enable_delete = true

  freeform_tags = {
    projeto    = "torre-logistica"
    ambiente   = "demonstracao"
    custo      = "always-free"
    gerenciado = "terraform"
  }
}

# ---------------------------------------------------------------------------
# Cotas — o guarda-corpo que BLOQUEIA
# ---------------------------------------------------------------------------
# Distinção que importa e costuma ser confundida:
#
#   COTA      é limite rígido. Pedir além dela faz o provisionamento FALHAR. É o que garante que
#             um erro de digitação em `ocpus` não vire fatura.
#   ORÇAMENTO é aviso. Ele manda e-mail quando o gasto passa do valor; não impede nada.
#
# Preferimos falha de provisionamento a cobrança — então a cota é a peça principal, e o orçamento
# é a rede embaixo dela.
#
# As declarações ficam TODAS numa política só, de propósito. A documentação da Oracle é explícita:
# "Within a policy, quota statements are evaluated in order. A later statement supersedes an
# earlier statement that targets the same resource" — é isso que faz o `zero` geral valer para
# tudo e os `set` seguintes abrirem exceção apenas para o que a demonstração usa. Espalhadas em
# políticas diferentes a regra seria outra: "If several policies target the same resource, the
# most restrictive policy applies", e os `set` nunca abririam a exceção.
resource "oci_limits_quota" "somente_always_free" {
  compartment_id = var.ocid_da_tenancy
  name           = "${var.prefixo}-somente-always-free"
  description    = "Impede que o compartimento da demonstração crie qualquer recurso fora do nível Always Free."

  statements = [
    # 1. Fecha tudo em computação, depois reabre exatamente o shape gratuito.
    "zero compute-core quota /*/ in compartment ${oci_identity_compartment.torre.name}",
    "set compute-core quota standard-a1-core-count to ${var.ocpus} in compartment ${oci_identity_compartment.torre.name}",
    "set compute-core quota standard-a1-memory-count to ${var.memoria_em_gb} in compartment ${oci_identity_compartment.torre.name}",
    # A cota regional é separada da cota por domínio de disponibilidade. Definir só uma das duas
    # deixa a outra no limite de serviço padrão — ou seja, deixa a porta que se quis fechar aberta.
    "set compute-core quota standard-a1-core-regional-count to ${var.ocpus} in compartment ${oci_identity_compartment.torre.name}",
    "set compute-core quota standard-a1-memory-regional-count to ${var.memoria_em_gb} in compartment ${oci_identity_compartment.torre.name}",

    # 2. Disco: só o volume de inicialização da única máquina.
    "set block-storage quota total-storage-gb to ${var.tamanho_do_disco_em_gb} in compartment ${oci_identity_compartment.torre.name}",

    # 3. Famílias inteiras que esta demonstração não usa e que cobram. Zeradas para que um comando
    #    digitado no console por engano falhe em vez de criar.
    "zero database quota /*/ in compartment ${oci_identity_compartment.torre.name}",
    "zero load-balancer quota /*/ in compartment ${oci_identity_compartment.torre.name}",
    "zero filesystem quota /*/ in compartment ${oci_identity_compartment.torre.name}",
    "zero analytics quota /*/ in compartment ${oci_identity_compartment.torre.name}",
  ]

  freeform_tags = {
    projeto = "torre-logistica"
  }
}

# ---------------------------------------------------------------------------
# Orçamento — o guarda-corpo que AVISA
# ---------------------------------------------------------------------------
resource "oci_budget_budget" "torre" {
  # Orçamento vive na raiz da conta e aponta para o compartimento alvo.
  compartment_id = var.ocid_da_tenancy
  target_type    = "COMPARTMENT"
  targets        = [oci_identity_compartment.torre.id]

  amount       = var.limite_do_orcamento_em_dolares
  reset_period = "MONTHLY"

  display_name = "${var.prefixo}-orcamento"
  description  = "A demonstração deve custar US$ 0,00. Qualquer gasto é defeito, não crescimento."
}

resource "oci_budget_alert_rule" "qualquer_gasto" {
  budget_id      = oci_budget_budget.torre.id
  display_name   = "${var.prefixo}-qualquer-gasto"
  type           = "ACTUAL"
  threshold      = 1
  threshold_type = "PERCENTAGE"

  recipients = var.email_do_alerta_de_custo
  message    = "A demonstração da Torre Logística registrou gasto. O esperado é zero — investigue."
}

# ---------------------------------------------------------------------------
# Rede
# ---------------------------------------------------------------------------
data "oci_identity_availability_domains" "disponiveis" {
  compartment_id = var.ocid_da_tenancy
}

locals {
  dominio_escolhido = coalesce(
    var.dominio_de_disponibilidade != "" ? var.dominio_de_disponibilidade : null,
    data.oci_identity_availability_domains.disponiveis.availability_domains[0].name,
  )
}

resource "oci_core_vcn" "torre" {
  compartment_id = oci_identity_compartment.torre.id
  display_name   = "${var.prefixo}-vcn"
  cidr_blocks    = ["10.20.0.0/16"]
  dns_label      = replace(var.prefixo, "-", "")
}

resource "oci_core_internet_gateway" "torre" {
  compartment_id = oci_identity_compartment.torre.id
  vcn_id         = oci_core_vcn.torre.id
  display_name   = "${var.prefixo}-gateway"
  enabled        = true
}

resource "oci_core_route_table" "torre" {
  compartment_id = oci_identity_compartment.torre.id
  vcn_id         = oci_core_vcn.torre.id
  display_name   = "${var.prefixo}-rotas"

  route_rules {
    destination       = "0.0.0.0/0"
    destination_type  = "CIDR_BLOCK"
    network_entity_id = oci_core_internet_gateway.torre.id
  }
}

resource "oci_core_security_list" "torre" {
  compartment_id = oci_identity_compartment.torre.id
  vcn_id         = oci_core_vcn.torre.id
  display_name   = "${var.prefixo}-seguranca"

  # Saída liberada: a máquina precisa buscar pacote do sistema, imagem do Docker e certificado da
  # autoridade certificadora. Nada disso é alcançável por quem está de fora.
  egress_security_rules {
    destination      = "0.0.0.0/0"
    destination_type = "CIDR_BLOCK"
    protocol         = "all"
    description      = "Atualização de sistema, imagens e ACME"
  }

  # SSH, e só de onde foi declarado. É o único acesso administrativo da máquina.
  ingress_security_rules {
    source      = var.cidr_de_administracao
    source_type = "CIDR_BLOCK"
    protocol    = "6" # TCP
    description = "SSH administrativo"

    tcp_options {
      min = 22
      max = 22
    }
  }

  # As duas únicas portas públicas da demonstração.
  ingress_security_rules {
    source      = "0.0.0.0/0"
    source_type = "CIDR_BLOCK"
    protocol    = "6"
    description = "HTTP — redireciona para HTTPS e responde ao desafio da autoridade certificadora"

    tcp_options {
      min = 80
      max = 80
    }
  }

  ingress_security_rules {
    source      = "0.0.0.0/0"
    source_type = "CIDR_BLOCK"
    protocol    = "6"
    description = "HTTPS"

    tcp_options {
      min = 443
      max = 443
    }
  }

  ingress_security_rules {
    source      = "0.0.0.0/0"
    source_type = "CIDR_BLOCK"
    protocol    = "17" # UDP
    description = "HTTP/3 (QUIC)"

    udp_options {
      min = 443
      max = 443
    }
  }

  # Sem esta regra, uma resposta grande para um cliente com MTU menor some sem erro nenhum: o
  # roteador do caminho precisa poder avisar "fragmente", e o aviso é esta mensagem ICMP.
  ingress_security_rules {
    source      = "0.0.0.0/0"
    source_type = "CIDR_BLOCK"
    protocol    = "1" # ICMP
    description = "Descoberta de MTU do caminho"

    icmp_options {
      type = 3
      code = 4
    }
  }
}

resource "oci_core_subnet" "torre" {
  compartment_id = oci_identity_compartment.torre.id
  vcn_id         = oci_core_vcn.torre.id
  display_name   = "${var.prefixo}-sub-rede"
  cidr_block     = "10.20.1.0/24"
  dns_label      = "publica"

  route_table_id    = oci_core_route_table.torre.id
  security_list_ids = [oci_core_security_list.torre.id]

  # Sub-rede regional (sem `availability_domain`): sobrevive à indisponibilidade de um domínio
  # específico, e é o padrão recomendado pela Oracle.
  prohibit_public_ip_on_vnic = false
}

# ---------------------------------------------------------------------------
# Máquina
# ---------------------------------------------------------------------------
# A imagem é resolvida por consulta, não fixada por OCID: OCID de imagem muda a cada região, e
# fixar um tornaria este arquivo utilizável numa região só.
data "oci_core_images" "ubuntu_arm" {
  compartment_id           = var.ocid_da_tenancy
  operating_system         = "Canonical Ubuntu"
  operating_system_version = "24.04"
  shape                    = "VM.Standard.A1.Flex"
  sort_by                  = "TIMECREATED"
  sort_order               = "DESC"
}

resource "oci_core_instance" "torre" {
  compartment_id      = oci_identity_compartment.torre.id
  availability_domain = local.dominio_escolhido
  display_name        = "${var.prefixo}-maquina"

  # O único shape Ampere do nível Always Free.
  shape = "VM.Standard.A1.Flex"

  shape_config {
    ocpus         = var.ocpus
    memory_in_gbs = var.memoria_em_gb
  }

  source_details {
    source_type             = "image"
    source_id               = data.oci_core_images.ubuntu_arm.images[0].id
    boot_volume_size_in_gbs = var.tamanho_do_disco_em_gb
  }

  create_vnic_details {
    subnet_id = oci_core_subnet.torre.id

    # IP público efêmero. Não há cobrança por endereço IPv4 na OCI — nem efêmero, nem reservado,
    # nem reservado sem uso. Efêmero basta porque o endereço só muda se a máquina for destruída,
    # e nesse caso o registro de DNS precisaria ser refeito de qualquer forma.
    assign_public_ip = true

    # A máquina não precisa que outra máquina a encontre por nome: não há outra máquina.
    assign_private_dns_record = false
    hostname_label            = null
  }

  metadata = {
    ssh_authorized_keys = var.chave_publica_ssh
    user_data           = base64encode(file("${path.module}/cloud-init.yaml"))
  }

  agent_config {
    # O agente de monitoramento é o que reporta uso de CPU, rede e memória — exatamente as três
    # medidas pelas quais a Oracle decide se uma máquina Always Free está ociosa. Desligá-lo não
    # protegeria a máquina; deixaria a Oracle sem o dado que mostra que ela não está ociosa.
    is_monitoring_disabled = false
    is_management_disabled = false
  }

  lifecycle {
    # Sem isto, uma imagem nova do Ubuntu publicada pela Canonical faria o próximo `plan` propor
    # DESTRUIR e recriar a máquina — levando junto o disco, o banco e os comprovantes.
    ignore_changes = [source_details[0].source_id]
  }

  freeform_tags = {
    projeto  = "torre-logistica"
    ambiente = "demonstracao"
    custo    = "always-free"
  }
}
