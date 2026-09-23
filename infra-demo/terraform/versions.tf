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
#
# `auth` existe porque os dois caminhos de login escrevem perfis diferentes no mesmo arquivo:
#
#   ApiKey        par de chaves gerado localmente, chave pública colada no console da Oracle.
#                 Não expira. É o padrão do provedor.
#   SecurityToken `oci session authenticate` abre o navegador, você entra como entraria no console,
#                 e a CLI guarda um token de sessão. Nada de chave para gerenciar — em troca, o
#                 token expira (renovável com `oci session refresh`).
#
# Sem declarar `SecurityToken`, o provedor tenta ler `key_file` de um perfil que não tem nenhum, e a
# falha fala de chave ausente em vez de falar do método errado.
provider "oci" {
  region              = var.regiao
  auth                = var.metodo_de_autenticacao
  config_file_profile = var.perfil_da_cli
}
