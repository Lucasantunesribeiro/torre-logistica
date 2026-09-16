import type { Endereco, MotivoDeTentativa, StatusDaEntrega, StatusDaRota } from './api';

const hora = new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit' });
const diaEHora = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });

/** Hora no fuso do aparelho do motorista: apresentação, nunca regra (CLAUDE.md, seção 49). */
export function formatarHora(instante: string): string {
  return hora.format(new Date(instante));
}

/** Janela prometida legível; mostra o dia só quando ela atravessa dias. */
export function formatarJanela(de: string, ate: string): string {
  const inicio = new Date(de);
  const fim = new Date(ate);
  const mesmoDia = inicio.toDateString() === fim.toDateString();

  return mesmoDia
    ? `${hora.format(inicio)} às ${hora.format(fim)}`
    : `${diaEHora.format(inicio)} às ${diaEHora.format(fim)}`;
}

export function enderecoEmLinha(endereco: Endereco): string {
  const complemento = endereco.complemento ? `, ${endereco.complemento}` : '';
  return `${endereco.logradouro}, ${endereco.numero}${complemento} — ${endereco.bairro}, ${endereco.cidade}/${endereco.uf}`;
}

export const ROTULOS_DA_ENTREGA: Record<StatusDaEntrega, string> = {
  Criada: 'Criada',
  Planejada: 'Planejada',
  Atribuida: 'Aguardando saída',
  EmRota: 'A caminho',
  ProximaDoDestino: 'No destino',
  Entregue: 'Entregue',
  TentativaFrustrada: 'Não entregue',
  Reagendada: 'Reagendada',
  Cancelada: 'Cancelada',
};

export const ROTULOS_DA_ROTA: Record<StatusDaRota, string> = {
  EmMontagem: 'Em montagem',
  Planejada: 'Pronta para sair',
  EmAndamento: 'Em andamento',
  Concluida: 'Concluída',
  Cancelada: 'Cancelada',
};

export const ROTULOS_DO_MOTIVO: Record<MotivoDeTentativa, string> = {
  DestinatarioAusente: 'Destinatário ausente',
  EnderecoNaoLocalizado: 'Endereço não encontrado',
  RecusadaPeloDestinatario: 'Recusada pelo destinatário',
  LocalFechado: 'Local fechado',
  AcessoImpedido: 'Acesso impedido (portaria, área restrita)',
  ProblemaComVeiculo: 'Problema com o veículo',
  ProblemaComMercadoria: 'Problema com a mercadoria',
  Outro: 'Outro motivo (descreva)',
};

export function estaPendente(status: StatusDaEntrega): boolean {
  return status === 'EmRota' || status === 'ProximaDoDestino' || status === 'Atribuida';
}

/** Navegação entregue ao aplicativo de mapas do aparelho, só quando o motorista toca. */
export function linkDeNavegacao(entrega: { readonly localizacao: { latitude: number; longitude: number } | null; readonly endereco: Endereco }): string {
  const destino = entrega.localizacao
    ? `${entrega.localizacao.latitude},${entrega.localizacao.longitude}`
    : enderecoEmLinha(entrega.endereco);

  return `https://www.google.com/maps/dir/?api=1&destination=${encodeURIComponent(destino)}`;
}
