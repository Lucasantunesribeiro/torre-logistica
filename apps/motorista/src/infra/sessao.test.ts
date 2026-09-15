import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type * as ModuloDeSessao from './sessao';

const URL_DA_API = 'http://api.teste.local';

function sessaoFalsa(token: string) {
  return {
    tokenDeAcesso: token,
    expiraEm: '2026-09-15T12:15:00Z',
    usuario: {
      id: 'u1',
      nome: 'Rafael Moura',
      email: 'rafael@aurora.test',
      perfil: 'Motorista',
      organizacaoId: 'o1',
      organizacaoNome: 'Transportadora Aurora',
      organizacaoSlug: 'transportadora-aurora',
    },
  };
}

function json(corpo: unknown, status = 200): Response {
  return new Response(JSON.stringify(corpo), { status, headers: { 'Content-Type': 'application/json' } });
}

function caminhoDe(entrada: RequestInfo | URL): string {
  const url = typeof entrada === 'string' ? entrada : entrada instanceof URL ? entrada.href : entrada.url;
  return new URL(url).pathname;
}

async function carregar(): Promise<typeof ModuloDeSessao> {
  vi.resetModules();
  return import('./sessao');
}

beforeEach(() => {
  vi.stubEnv('VITE_URL_DA_API', URL_DA_API);
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
});

describe('sessão do motorista', () => {
  it('usa o canal do motorista, guarda o token só em memória e pede o cookie no login', async () => {
    const chamadas = vi.fn<typeof fetch>(() => Promise.resolve(json(sessaoFalsa('M1'))));
    vi.stubGlobal('fetch', chamadas);
    const sessao = await carregar();

    await sessao.entrar({ organizacao: 'transportadora-aurora', email: 'rafael@aurora.test', senha: 'senha-comprida' });

    const [entrada, opcoes] = chamadas.mock.calls[0]!;
    expect(caminhoDe(entrada)).toBe('/api/motorista/autenticacao/login');
    expect(opcoes?.credentials).toBe('include');
    expect(sessao.obterSessao()?.tokenDeAcesso).toBe('M1');
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it('faz uma única renovação mesmo com várias pedidas ao mesmo tempo', async () => {
    let liberar: (resposta: Response) => void = () => undefined;
    const chamadas = vi.fn<(entrada: RequestInfo | URL) => Promise<Response>>(() => new Promise((resolver) => { liberar = resolver; }));
    vi.stubGlobal('fetch', chamadas);
    const sessao = await carregar();

    const pedidos = [sessao.renovarSessao(), sessao.renovarSessao()];
    liberar(json(sessaoFalsa('M2')));
    const resultados = await Promise.all(pedidos);

    expect(chamadas).toHaveBeenCalledTimes(1);
    expect(caminhoDe(chamadas.mock.calls[0]![0])).toBe('/api/motorista/autenticacao/renovar');
    expect(resultados.map((resultado) => resultado?.tokenDeAcesso)).toEqual(['M2', 'M2']);
  });

  it('diante de 401 renova uma vez e repete a requisição com o token novo', async () => {
    const tokens: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn((entrada: RequestInfo | URL, opcoes?: RequestInit) => {
        const caminho = caminhoDe(entrada);
        if (caminho.endsWith('/login')) return Promise.resolve(json(sessaoFalsa('VENCIDO')));
        if (caminho.endsWith('/renovar')) return Promise.resolve(json(sessaoFalsa('NOVO')));

        const autorizacao = new Headers(opcoes?.headers).get('Authorization') ?? '';
        tokens.push(autorizacao);
        return Promise.resolve(new Response('[]', { status: autorizacao === 'Bearer NOVO' ? 200 : 401 }));
      }),
    );
    const sessao = await carregar();
    await sessao.entrar({ organizacao: 'a', email: 'b', senha: 'c' });

    const resposta = await sessao.requisicaoAutenticada('/api/motorista/rotas');

    expect(resposta.status).toBe(200);
    expect(tokens).toEqual(['Bearer VENCIDO', 'Bearer NOVO']);
  });

  it('com a renovação recusada, encerra a sessão local e falha com 401', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn((entrada: RequestInfo | URL) => {
        const caminho = caminhoDe(entrada);
        if (caminho.endsWith('/login')) return Promise.resolve(json(sessaoFalsa('VENCIDO')));
        if (caminho.endsWith('/renovar')) return Promise.resolve(json({ codigo: 'sessao_invalida' }, 401));
        return Promise.resolve(new Response(null, { status: 401 }));
      }),
    );
    const sessao = await carregar();
    await sessao.entrar({ organizacao: 'a', email: 'b', senha: 'c' });

    await expect(sessao.requisicaoAutenticada('/api/motorista/rotas')).rejects.toMatchObject({ status: 401 });
    expect(sessao.obterSessao()).toBeNull();
  });
});
