import { describe, expect, it } from 'vitest';

import type { Entrega, ItemDeRota, Rota } from '../infra/api';
import type { OperacaoLocal, StatusDaOperacao } from './fila';
import { projetarEntrega, projetarRota, projetarRotas } from './projecao';

const LEITURA_EM = Date.parse('2026-09-15T12:00:00Z');

function operacao(
  tipo: OperacaoLocal['tipo'],
  alvoId: string,
  extra: { status?: StatusDaOperacao; motivo?: OperacaoLocal['payload']['motivo']; sincronizadaEm?: string } = {},
): OperacaoLocal {
  return {
    id: `${tipo}-${alvoId}`,
    usuarioId: 'u1',
    tipo,
    payload: { alvoId, motivo: extra.motivo ?? null },
    descricao: tipo,
    criadaEm: '2026-09-15T12:05:00Z',
    ordem: 1,
    status: extra.status ?? 'Pending',
    tentativas: 0,
    ultimaTentativaEm: null,
    sincronizadaEm: extra.sincronizadaEm ?? null,
    codigo: null,
    mensagem: null,
    ciente: false,
  };
}

const parada = (entregaId: string, status: Rota['paradas'][number]['status']): Rota['paradas'][number] => ({
  sequencia: 1,
  entregaId,
  codigoDaEntrega: 'ENT',
  status,
  destinatarioNome: 'Carla',
  endereco: { logradouro: 'R', numero: '1', complemento: null, bairro: 'B', cidade: 'C', uf: 'SP', cep: '13000000' },
  janelaPrometida: { de: '2026-09-15T12:00:00Z', ate: '2026-09-15T15:00:00Z' },
});

const rota = (status: Rota['status'], paradas: Rota['paradas']): Rota => ({
  id: 'r1',
  codigo: 'ROT',
  data: '2026-09-15',
  status,
  saidaPlanejada: null,
  iniciadaEm: null,
  concluidaEm: null,
  hubNome: null,
  veiculo: null,
  paradas,
});

const entrega = (status: Entrega['status'], tentativas = 0): Entrega => ({
  id: 'e1',
  codigo: 'ENT',
  status,
  rotaId: 'r1',
  sequencia: 1,
  destinatario: { nome: 'Carla', telefone: null, instrucoesDeEntrega: null },
  endereco: parada('e1', 'EmRota').endereco,
  localizacao: null,
  janelaPrometida: parada('e1', 'EmRota').janelaPrometida,
  observacoes: null,
  execucao: {
    saiuParaRotaEm: null,
    chegadaRegistradaEm: null,
    entregueEm: null,
    tentativasFrustradas: tentativas,
    motivoDaUltimaTentativa: tentativas > 0 ? 'LocalFechado' : null,
    ultimaTentativaFrustradaEm: null,
  },
});

describe('projeção das ações da fila sobre a leitura', () => {
  it('início da rota guardado põe a rota em andamento e as entregas a caminho', () => {
    const projetada = projetarRota(rota('Planejada', [parada('e1', 'Atribuida'), parada('e2', 'Cancelada')]), [operacao('IniciarRota', 'r1')], LEITURA_EM);

    expect(projetada.status).toBe('EmAndamento');
    expect(projetada.paradas.map((item) => item.status)).toEqual(['EmRota', 'Cancelada']);
  });

  it('chegada, conclusão e tentativa aplicam as mesmas transições da API, em ordem', () => {
    const chegadaEConclusao = projetarEntrega(entrega('EmRota'), [operacao('RegistrarChegada', 'e1'), operacao('ConcluirEntrega', 'e1')], LEITURA_EM);
    expect(chegadaEConclusao).toMatchObject({ status: 'Entregue', execucao: { chegadaRegistradaEm: '2026-09-15T12:05:00Z', entregueEm: '2026-09-15T12:05:00Z' } });

    const tentativa = projetarEntrega(entrega('ProximaDoDestino'), [operacao('RegistrarTentativaFrustrada', 'e1', { motivo: 'AcessoImpedido' })], LEITURA_EM);
    expect(tentativa).toMatchObject({ status: 'TentativaFrustrada', execucao: { tentativasFrustradas: 1, motivoDaUltimaTentativa: 'AcessoImpedido' } });
  });

  it('ação que não vale para o estado lido não muda nada — quem decide conflito é o servidor', () => {
    const cancelada = entrega('Cancelada');

    expect(projetarEntrega(cancelada, [operacao('ConcluirEntrega', 'e1')], LEITURA_EM)).toBe(cancelada);
  });

  it('ação sincronizada depois da leitura vale; aplicada sobre leitura que já a contém, não conta duas vezes', () => {
    const sincronizada = operacao('RegistrarTentativaFrustrada', 'e1', { status: 'Synced', motivo: 'LocalFechado', sincronizadaEm: '2026-09-15T12:10:00Z' });

    expect(projetarEntrega(entrega('EmRota'), [sincronizada], LEITURA_EM).status).toBe('TentativaFrustrada');
    expect(projetarEntrega(entrega('TentativaFrustrada', 1), [sincronizada], LEITURA_EM).execucao.tentativasFrustradas).toBe(1);
    expect(projetarEntrega(entrega('EmRota'), [sincronizada], Date.parse('2026-09-15T12:20:00Z')).status).toBe('EmRota');
  });

  it('conflito e recusa não aparecem na tela como feitos', () => {
    const lida = entrega('EmRota');

    expect(projetarEntrega(lida, [operacao('ConcluirEntrega', 'e1', { status: 'Conflict' }), operacao('RegistrarChegada', 'e1', { status: 'Failed' })], LEITURA_EM)).toBe(lida);
  });

  it('lista do dia: rota iniciada vai para o topo e rota encerrada sai', () => {
    const item = (id: string, status: ItemDeRota['status']): ItemDeRota => ({
      id,
      codigo: id,
      data: '2026-09-15',
      status,
      saidaPlanejada: null,
      iniciadaEm: null,
      totalDeParadas: 1,
      paradasPendentes: 1,
    });

    expect(projetarRotas([item('r1', 'Planejada'), item('r2', 'Planejada')], [operacao('IniciarRota', 'r2')], LEITURA_EM).map((rotaDoDia) => [rotaDoDia.id, rotaDoDia.status])).toEqual([
      ['r2', 'EmAndamento'],
      ['r1', 'Planejada'],
    ]);
    expect(projetarRotas([item('r1', 'EmAndamento'), item('r2', 'Planejada')], [operacao('ConcluirRota', 'r1')], LEITURA_EM).map((rotaDoDia) => rotaDoDia.id)).toEqual(['r2']);
  });
});
