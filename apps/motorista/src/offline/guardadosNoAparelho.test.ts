import { describe, expect, it } from 'vitest';

import { RespostaInesperada } from '../infra/api';
import { ErroDeApi } from '../infra/sessao';
import type { MotoristaAutenticado } from '../infra/sessao';
import { enfileirar, operacoesDoUsuario } from './fila';
import {
  armazenamentoDePosicoes,
  esquecerDoAparelho,
  guardarIdentidade,
  haSaidaPendente,
  identidadeGuardada,
  lerComCopia,
} from './guardadosNoAparelho';

const MOTORISTA: MotoristaAutenticado = {
  id: 'u1',
  nome: 'Rafael Moura',
  email: 'rafael@aurora.test',
  perfil: 'Motorista',
  organizacaoId: 'o1',
  organizacaoNome: 'Transportadora Aurora',
  organizacaoSlug: 'transportadora-aurora',
};

const semRede = () => Promise.reject(new TypeError('Failed to fetch'));

describe('cópias e identidade guardadas no aparelho', () => {
  it('sem rede ou com o servidor fora devolve a última cópia do mesmo motorista, com o instante dela', async () => {
    await lerComCopia('u1', 'rotas', () => Promise.resolve(['r1']), () => 1_000);

    await expect(lerComCopia('u1', 'rotas', semRede)).resolves.toEqual({ dados: ['r1'], obtidaEm: 1_000, doAparelho: true });
    await expect(lerComCopia('u1', 'rotas', () => Promise.reject(new ErroDeApi(503, null, 'fora')))).resolves.toMatchObject({ doAparelho: true });
    await expect(lerComCopia('u2', 'rotas', semRede)).rejects.toThrow(TypeError);
  });

  it('resposta que diz que o item não é mais do motorista não cai para a cópia, e a cópia é apagada', async () => {
    await lerComCopia('u1', 'entrega:e1', () => Promise.resolve({ id: 'e1' }));

    await expect(lerComCopia('u1', 'entrega:e1', () => Promise.reject(new ErroDeApi(409, 'entrega_reatribuida', 'x')))).rejects.toMatchObject({ status: 409 });
    await expect(lerComCopia('u1', 'entrega:e1', semRede)).rejects.toThrow(TypeError);
    await expect(lerComCopia('u1', 'entrega:e1', () => Promise.reject(new RespostaInesperada()))).rejects.toThrow(RespostaInesperada);
  });

  it('identidade vale por 7 dias desde a última confirmação do servidor', async () => {
    const confirmadaEm = Date.parse('2026-09-15T08:00:00Z');
    await guardarIdentidade(MOTORISTA, confirmadaEm);

    expect(await identidadeGuardada(confirmadaEm + 6 * 24 * 60 * 60 * 1000)).toEqual(MOTORISTA);
    expect(await identidadeGuardada(confirmadaEm + 7 * 24 * 60 * 60 * 1000 + 1)).toBeNull();
    expect(await identidadeGuardada(confirmadaEm)).toBeNull();
  });

  it('sair apaga cópias, posições e identidade, mas mantém as ações não enviadas e anota a saída pendente', async () => {
    await lerComCopia('u1', 'rotas', () => Promise.resolve(['r1']));
    await guardarIdentidade(MOTORISTA);
    const posicoes = armazenamentoDePosicoes('u1');
    await posicoes.guardar([
      { eventoDeLocalizacaoId: 'p1', latitude: -22.9, longitude: -47.06, precisaoEmMetros: 5, capturadaEm: '2026-09-15T12:00:00Z', sequencia: 1, velocidadeEmMetrosPorSegundo: null, direcaoEmGraus: null },
    ]);
    await enfileirar('u1', { tipo: 'ConcluirEntrega', alvoId: 'e1', descricao: 'Conclusão' });

    await esquecerDoAparelho(true);

    await expect(lerComCopia('u1', 'rotas', semRede)).rejects.toThrow(TypeError);
    expect(await identidadeGuardada()).toBeNull();
    expect(await posicoes.carregar()).toEqual([]);
    expect(await operacoesDoUsuario('u1')).toHaveLength(1);
    expect(await haSaidaPendente()).toBe(true);
  });
});
