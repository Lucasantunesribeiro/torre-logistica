import type { Entrega, ItemDeRota, MotivoDeTentativa, Rota, StatusDaEntrega } from '../infra/api';
import { aguardandoEnvio } from './fila';
import type { OperacaoLocal } from './fila';

/*
 * A tela mostra o que o motorista fez, mesmo antes de o servidor saber: a leitura do servidor (ou a cópia
 * guardada) com as ações da fila aplicadas por cima.
 *
 * Cada ação só se aplica a partir do estado em que ela vale — as mesmas transições da API. Isso torna a
 * projeção segura de aplicar sobre uma leitura que já contém o efeito: a conclusão não conclui de novo, a
 * tentativa não conta duas vezes. A projeção nunca decide conflito; quem decide é o servidor.
 */

const EM_EXECUCAO: readonly StatusDaEntrega[] = ['EmRota', 'ProximaDoDestino'];

interface EstadoDaEntrega {
  readonly status: StatusDaEntrega;
  readonly tentativas: number;
  readonly motivo: MotivoDeTentativa | null;
  readonly instante: string | null;
}

/**
 * Ações que ainda não estão refletidas na leitura: as que esperam envio e as aplicadas depois de a leitura
 * ter sido obtida.
 */
function naoRefletidas(operacoes: readonly OperacaoLocal[], obtidaEm: number): readonly OperacaoLocal[] {
  return operacoes.filter(
    (operacao) =>
      aguardandoEnvio(operacao) ||
      (operacao.status === 'Synced' && operacao.sincronizadaEm !== null && Date.parse(operacao.sincronizadaEm) > obtidaEm),
  );
}

function aplicarNaEntrega(estado: EstadoDaEntrega, entregaId: string, rotaId: string | null, operacao: OperacaoLocal): EstadoDaEntrega {
  const { alvoId, motivo } = operacao.payload;

  switch (operacao.tipo) {
    case 'IniciarRota':
      return alvoId === rotaId && estado.status === 'Atribuida' ? { ...estado, status: 'EmRota' } : estado;
    case 'RegistrarChegada':
      return alvoId === entregaId && estado.status === 'EmRota' ? { ...estado, status: 'ProximaDoDestino', instante: operacao.criadaEm } : estado;
    case 'ConcluirEntrega':
      return alvoId === entregaId && EM_EXECUCAO.includes(estado.status) ? { ...estado, status: 'Entregue', instante: operacao.criadaEm } : estado;
    case 'RegistrarTentativaFrustrada':
      return alvoId === entregaId && motivo && EM_EXECUCAO.includes(estado.status)
        ? { status: 'TentativaFrustrada', tentativas: estado.tentativas + 1, motivo, instante: operacao.criadaEm }
        : estado;
    case 'ConcluirRota':
      return estado;
  }
}

function statusProjetado(status: StatusDaEntrega, entregaId: string, rotaId: string | null, operacoes: readonly OperacaoLocal[]): StatusDaEntrega {
  return operacoes.reduce<EstadoDaEntrega>(
    (estado, operacao) => aplicarNaEntrega(estado, entregaId, rotaId, operacao),
    { status, tentativas: 0, motivo: null, instante: null },
  ).status;
}

export function projetarRota(rota: Rota, operacoes: readonly OperacaoLocal[], obtidaEm: number): Rota {
  const pendentes = naoRefletidas(operacoes, obtidaEm);
  if (pendentes.length === 0) {
    return rota;
  }

  let status = rota.status;
  for (const operacao of pendentes) {
    if (operacao.payload.alvoId !== rota.id) continue;
    if (operacao.tipo === 'IniciarRota' && status === 'Planejada') status = 'EmAndamento';
    if (operacao.tipo === 'ConcluirRota' && status === 'EmAndamento') status = 'Concluida';
  }

  return {
    ...rota,
    status,
    paradas: rota.paradas.map((parada) => ({
      ...parada,
      status: statusProjetado(parada.status, parada.entregaId, rota.id, pendentes),
    })),
  };
}

export function projetarRotas(rotas: readonly ItemDeRota[], operacoes: readonly OperacaoLocal[], obtidaEm: number): ItemDeRota[] {
  const pendentes = naoRefletidas(operacoes, obtidaEm);
  if (pendentes.length === 0) {
    return [...rotas];
  }

  const encerradas = new Set(pendentes.filter((operacao) => operacao.tipo === 'ConcluirRota').map((operacao) => operacao.payload.alvoId));
  const iniciadas = new Set(pendentes.filter((operacao) => operacao.tipo === 'IniciarRota').map((operacao) => operacao.payload.alvoId));

  // Mesma ordem da API: a rota em andamento primeiro. Rota encerrada sai da lista do dia.
  return rotas
    .map((rota) => (iniciadas.has(rota.id) && rota.status === 'Planejada' ? { ...rota, status: 'EmAndamento' as const } : rota))
    .filter((rota) => !(encerradas.has(rota.id) && rota.status === 'EmAndamento'))
    .sort((a, b) => Number(b.status === 'EmAndamento') - Number(a.status === 'EmAndamento'));
}

export function projetarEntrega(entrega: Entrega, operacoes: readonly OperacaoLocal[], obtidaEm: number): Entrega {
  const pendentes = naoRefletidas(operacoes, obtidaEm);
  if (pendentes.length === 0) {
    return entrega;
  }

  let execucao = entrega.execucao;
  let mudou = false;
  let estado: EstadoDaEntrega = {
    status: entrega.status,
    tentativas: execucao.tentativasFrustradas,
    motivo: execucao.motivoDaUltimaTentativa,
    instante: null,
  };

  for (const operacao of pendentes) {
    const depois = aplicarNaEntrega(estado, entrega.id, entrega.rotaId, operacao);
    if (depois === estado) continue;
    mudou = true;

    if (depois.status === 'ProximaDoDestino') execucao = { ...execucao, chegadaRegistradaEm: depois.instante };
    if (depois.status === 'Entregue') execucao = { ...execucao, entregueEm: depois.instante };
    if (depois.status === 'TentativaFrustrada') {
      execucao = {
        ...execucao,
        tentativasFrustradas: depois.tentativas,
        motivoDaUltimaTentativa: depois.motivo,
        ultimaTentativaFrustradaEm: depois.instante,
      };
    }
    estado = depois;
  }

  return mudou ? { ...entrega, status: estado.status, execucao } : entrega;
}
