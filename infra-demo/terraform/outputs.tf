# O que sai do provisionamento, e para que serve cada coisa.

output "ip_publico" {
  description = <<-TEXTO
    Endereço IPv4 público da máquina.

    Os quatro subdomínios da demonstração precisam de um registro A apontando para ele ANTES da
    primeira subida do compose: o Caddy pede o certificado ao subir, e a autoridade certificadora
    confere o DNS naquele momento.
  TEXTO
  value       = oci_core_instance.torre.public_ip
}

output "comando_de_acesso" {
  description = <<-TEXTO
    Linha pronta para entrar na máquina, com a chave correspondente à que foi declarada.

    O usuário é `ubuntu`, que é a conta administrativa sem privilégio da imagem da Canonical —
    criar uma conta a mais não acrescentaria segurança e exigiria passar a chave pública para
    dentro do cloud-init, onde ela viraria mais uma cópia para manter em dia.
  TEXTO
  value       = "ssh -i <sua-chave-privada> ubuntu@${oci_core_instance.torre.public_ip}"
}

output "ocid_do_compartimento" {
  description = <<-TEXTO
    Compartimento onde tudo isto vive.

    Para apagar a demonstração inteira, `terraform destroy` basta. Este OCID serve para conferir,
    pelo console ou pela CLI, que não sobrou nada:

      oci search resource structured-search --query-text \
        "query all resources where compartmentId = '<este-ocid>'"
  TEXTO
  value       = oci_identity_compartment.torre.id
}

output "custo_mensal_esperado" {
  description = "Registrado como saída para aparecer em todo `apply`: o valor que se espera na fatura."
  value       = "US$ 0,00 — todo recurso deste plano está dentro do nível Always Free."
}

output "conferencia_de_gratuidade" {
  description = <<-TEXTO
    As três contas que decidem se a fatura continua zerada. Conferir a cada mudança de
    dimensionamento, e não só na primeira vez.
  TEXTO
  value = {
    ocpu_hora_por_mes = "${var.ocpus} OCPU × 730 h = ${var.ocpus * 730} de 1.500 gratuitas"
    gb_hora_por_mes   = "${var.memoria_em_gb} GB × 730 h = ${var.memoria_em_gb * 730} de 9.000 gratuitas"
    disco_em_gb       = "${var.tamanho_do_disco_em_gb} de 200 gratuitos"
  }
}
