# Versões do Terraform e do provedor.
#
# O provedor da Oracle muda com frequência e já quebrou compatibilidade entre versões maiores.
# A restrição abaixo prende a versão maior: `terraform init -upgrade` traz correção, não surpresa.

terraform {
  required_version = ">= 1.6.0"

  required_providers {
    oci = {
      source  = "oracle/oci"
      version = "~> 7.0"
    }
  }
}

# Autenticação por arquivo de configuração da CLI da Oracle (~/.oci/config), e não por chave em
# variável do Terraform. Chave privada em variável acaba no estado, e o estado é um arquivo comum
# que alguém eventualmente abre, copia ou versiona por engano.
provider "oci" {
  region              = var.regiao
  config_file_profile = var.perfil_da_cli
}
