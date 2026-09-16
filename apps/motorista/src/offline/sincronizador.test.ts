import { describe, expect, it, vi } from 'vitest';

import type { OperacaoParaEnvio, ResultadoDaSincronizacao, ResultadoDeOperacao } from '../infra/api';
import { ErroDeApi } from '../infra/sessao';
import { enfileirar, operacoesDoUsuario, salvarOperacoes } from './fila';
import { Sincronizador, TAMANHO_MAXIMO_DO_LOTE } from './sincronizador';

type Enviar = (operacoes: readonly OperacaoParaEnvio[]) => Promise<ResultadoDaSincronizacao>;

const aplicada = (operacao: OperacaoParaEnvio): ResultadoDeOperacao => ({
  operacaoDoClienteId: operacao.operacaoDoClienteId,
  desfecho: 'Aplicada',
  repetida: false,
  codigo: null,
  mensagem: null,
});

const todasAplicadas: Enviar = (operacoes) => Promise.resolve({ resultados: operacoes.map(aplicada) });

function criar(enviar: Enviar = todasAplicadas, online = () => true) {
  const envio = vi.fn(enviar);
  const sincronizador = new Sincronizador({ usuarioId: 'u1', enviar: envio, online });
  return { envio, sincronizador };
}

const statusDaFila = async () => (await operacoesDoUsuario('u1')).map((operacao) => operacao.status);

describe('sincronizador da fila do aparelho', () => {
  it('envia as ações em ordem num lote, com o identificador de cada uma, e marca como sincronizadas', async () => {
    const saida = await enfileirar('u1', { tipo: 'IniciarRota', alvoId: 'r1', descricao: 'Início' });
    const chegada = await enfileirar('u1', { tipo: 'RegistrarChegada', alvoId: 'e1', descricao: 'Chegada' });
    const tentativa = await enfileirar('u1', { tipo: 'RegistrarTentativaFrustrada', alvoId: 'e1', motivo: 'LocalFechado', descricao: 'Tentativa' });
    await enfileirar('u2', { tipo: 'ConcluirEntrega', alvoId: 'e9', descricao: 'De outro motorista' });
    const { envio, sincronizador } = criar();

    await sincronizador.sincronizar();

    expect(envio).toHaveBeenCalledTimes(1);
    expect(envio.mock.calls[0]![0]).toEqual([
      { operacaoDoClienteId: saida.id, tipo: 'IniciarRota', alvoId: 'r1', motivo: null, observacao: null, criadaEm: saida.criadaEm },
      { operacaoDoClienteId: chegada.id, tipo: 'RegistrarChegada', alvoId: 'e1', motivo: null, observacao: null, criadaEm: chegada.criadaEm },
      {
        operacaoDoClienteId: tentativa.id,
        tipo: 'RegistrarTentativaFrustrada',
        alvoId: 'e1',
        motivo: 'LocalFechado',
        observacao: null,
        criadaEm: tentativa.criadaEm,
      },
    ]);
    expect(saida.id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    expect(await operacoesDoUsuario('u1')).toEqual(
      [saida, chegada, tentativa].map((operacao) => expect.objectContaining({ id: operacao.id, status: 'Synced', tentativas: 1 }) as unknown),
    );
    expect((await operacoesDoUsuario('u2'))[0]!.status).toBe('Pending');
  });

  it('sem internet no aparelho não tenta enviar', async () => {
    await enfileirar('u1', { tipo: 'ConcluirEntrega', alvoId: 'e1', descricao: 'Conclusão' });
    const { envio, sincronizador } = criar(todasAplicadas, () => false);

    await sincronizador.sincronizar();

    expect(envio).not.toHaveBeenCalled();
    expect(await statusDaFila()).toEqual(['Pending']);
  });

  it('falha de rede mantém a ação na fila, e o reenvio usa o mesmo identificador', async () => {
    const conclusao = await enfileirar('u1', { tipo: 'ConcluirEntrega', alvoId: 'e1', descricao: 'Conclusão' });
    let falhar = true;
    const { envio, sincronizador } = criar((operacoes) => (falhar ? Promise.reject(new TypeError('Failed to fetch')) : todasAplicadas(operacoes)));

    await sincronizador.sincronizar();
    expect(await operacoesDoUsuario('u1')).toEqual([expect.objectContaining({ status: 'Pending', tentativas: 1 })]);

    falhar = false;
    await sincronizador.sincronizar();

    expect(envio.mock.calls.map(([lote]) => lote[0]!.operacaoDoClienteId)).toEqual([conclusao.id, conclusao.id]);
    expect(await operacoesDoUsuario('u1')).toEqual([expect.objectContaining({ status: 'Synced', tentativas: 2 })]);
  });

  it.each([
    [new ErroDeApi(500, 'erro_inesperado', 'x'), 'Pending'],
    [new ErroDeApi(429, 'limite_excedido', 'x'), 'Pending'],
    [new ErroDeApi(401, 'nao_autenticado', 'x'), 'Pending'],
    [new ErroDeApi(400, 'validacao', 'Lote recusado.'), 'Failed'],
  ])('erro %o no envio deixa a ação %s', async (erro, status) => {
    await enfileirar('u1', { tipo: 'ConcluirEntrega', alvoId: 'e1', descricao: 'Conclusão' });
    const { sincronizador } = criar(() => Promise.reject(erro));

    await sincronizador.sincronizar();

    expect(await statusDaFila()).toEqual([status]);
  });

  it('cada desfecho do servidor vira um estado local; conflito e recusa não voltam a ser enviados', async () => {
    const aplicadaNoServidor = await enfileirar('u1', { tipo: 'RegistrarChegada', alvoId: 'e1', descricao: 'A' });
    const emConflito = await enfileirar('u1', { tipo: 'ConcluirEntrega', alvoId: 'e2', descricao: 'B' });
    const recusada = await enfileirar('u1', { tipo: 'ConcluirEntrega', alvoId: 'e3', descricao: 'C' });
    const concorrente = await enfileirar('u1', { tipo: 'ConcluirEntrega', alvoId: 'e4', descricao: 'D' });
    const semResposta = await enfileirar('u1', { tipo: 'ConcluirEntrega', alvoId: 'e5', descricao: 'E' });

    const { envio, sincronizador } = criar(() =>
      Promise.resolve({
        resultados: [
          { operacaoDoClienteId: aplicadaNoServidor.id.toUpperCase(), desfecho: 'Aplicada', repetida: true, codigo: null, mensagem: null },
          { operacaoDoClienteId: emConflito.id, desfecho: 'Conflito', repetida: false, codigo: 'transicao_invalida', mensagem: 'Transição inválida.' },
          { operacaoDoClienteId: recusada.id, desfecho: 'Recusada', repetida: false, codigo: 'entrega_nao_encontrada', mensagem: 'Não encontrada.' },
          { operacaoDoClienteId: concorrente.id, desfecho: 'TentarDeNovo', repetida: false, codigo: 'conflito_de_versao', mensagem: 'x' },
        ],
      }),
    );

    await sincronizador.sincronizar();

    const fila = await operacoesDoUsuario('u1');
    expect(fila.map((operacao) => [operacao.payload.alvoId, operacao.status, operacao.codigo])).toEqual([
      ['e1', 'Synced', null],
      ['e2', 'Conflict', 'transicao_invalida'],
      ['e3', 'Failed', 'entrega_nao_encontrada'],
      ['e4', 'Pending', null],
      ['e5', 'Pending', null],
    ]);
    expect(semResposta.status).toBe('Pending');

    await sincronizador.sincronizar();
    expect(envio.mock.calls[1]![0].map((operacao) => operacao.alvoId)).toEqual(['e4', 'e5']);
  });

  it('envio interrompido por fechar o navegador é retomado com o mesmo identificador', async () => {
    const conclusao = await enfileirar('u1', { tipo: 'ConcluirEntrega', alvoId: 'e1', descricao: 'Conclusão' });
    // O aparelho desligou com a ação "enviando": o servidor pode ou não ter recebido.
    await salvarOperacoes([{ ...conclusao, status: 'Uploading', tentativas: 1 }]);

    const { envio, sincronizador } = criar();
    await sincronizador.sincronizar();

    expect(envio.mock.calls[0]![0][0]!.operacaoDoClienteId).toBe(conclusao.id);
    expect(await operacoesDoUsuario('u1')).toEqual([expect.objectContaining({ status: 'Synced', tentativas: 2 })]);
  });

  it('fila maior que um lote sai em lotes sucessivos até esvaziar', async () => {
    for (let indice = 0; indice <= TAMANHO_MAXIMO_DO_LOTE; indice++) {
      await enfileirar('u1', { tipo: 'RegistrarChegada', alvoId: `e${indice}`, descricao: `Chegada ${indice}` });
    }
    const { envio, sincronizador } = criar();

    await sincronizador.sincronizar();

    expect(envio.mock.calls.map(([lote]) => lote.length)).toEqual([TAMANHO_MAXIMO_DO_LOTE, 1]);
    expect(new Set(await statusDaFila())).toEqual(new Set(['Synced']));
  });

  it('um envio por vez; ação guardada durante o envio sai na volta seguinte', async () => {
    await enfileirar('u1', { tipo: 'IniciarRota', alvoId: 'r1', descricao: 'Início' });
    let liberar: () => void = () => undefined;
    const { envio, sincronizador } = criar(async (operacoes) => {
      if (envio.mock.calls.length === 1) {
        await new Promise<void>((resolver) => {
          liberar = resolver;
        });
      }
      return todasAplicadas(operacoes);
    });

    const primeira = sincronizador.sincronizar();
    await vi.waitFor(() => {
      expect(envio).toHaveBeenCalledTimes(1);
    });
    await enfileirar('u1', { tipo: 'RegistrarChegada', alvoId: 'e1', descricao: 'Chegada' });
    const segunda = sincronizador.sincronizar();
    liberar();
    await Promise.all([primeira, segunda]);

    expect(envio.mock.calls.map(([lote]) => lote.map((operacao) => operacao.tipo))).toEqual([['IniciarRota'], ['RegistrarChegada']]);
  });
});
