import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type * as ModuloDeSessao from './sessao';

const URL_DA_API = 'http://api.teste.local';

function sessaoFalsa(token: string) {
  return {
    tokenDeAcesso: token,
    expiraEm: '2026-09-14T12:15:00Z',
    usuario: {
      id: 'u1',
      nome: 'Paula Siqueira',
      email: 'paula@aurora.test',
      perfil: 'Operador',
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

// O módulo guarda a sessão em memória; cada teste recebe uma instância limpa.
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

describe('sessão do console', () => {
  it('guarda a sessão em memória e pede o cookie ao navegador no login', async () => {
    const chamadas = vi.fn<typeof fetch>(() => Promise.resolve(json(sessaoFalsa('A1'))));
    vi.stubGlobal('fetch', chamadas);
    const sessao = await carregar();

    await sessao.entrar({ organizacao: 'transportadora-aurora', email: 'paula@aurora.test', senha: 'senha-comprida' });

    expect(sessao.obterSessao()?.tokenDeAcesso).toBe('A1');
    const [, opcoes] = chamadas.mock.calls[0]!;
    expect(opcoes?.credentials).toBe('include');
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it('transforma a falha de login em erro com status e código', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(json({ codigo: 'credenciais_invalidas', detail: 'x' }, 401))));
    const sessao = await carregar();

    await expect(sessao.entrar({ organizacao: 'a', email: 'b', senha: 'c' })).rejects.toMatchObject({
      status: 401,
      codigo: 'credenciais_invalidas',
    });
    expect(sessao.obterSessao()).toBeNull();
  });

  /**
   * A API trata reapresentação de token de renovação como possível roubo. Duas renovações
   * simultâneas com o mesmo cookie não podem sair do console.
   */
  it('faz uma única renovação mesmo com várias pedidas ao mesmo tempo', async () => {
    let liberar: (resposta: Response) => void = () => undefined;
    const chamadas = vi.fn(() => new Promise<Response>((resolver) => { liberar = resolver; }));
    vi.stubGlobal('fetch', chamadas);
    const sessao = await carregar();

    const pedidos = [sessao.renovarSessao(), sessao.renovarSessao(), sessao.renovarSessao()];
    liberar(json(sessaoFalsa('A2')));
    const resultados = await Promise.all(pedidos);

    expect(chamadas).toHaveBeenCalledTimes(1);
    expect(resultados.map((resultado) => resultado?.tokenDeAcesso)).toEqual(['A2', 'A2', 'A2']);
  });

  it('encerra a sessão local quando a renovação é recusada', async () => {
    const respostas = [json(sessaoFalsa('A1')), json({ codigo: 'sessao_invalida' }, 401)];
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostas.shift()!)));
    const sessao = await carregar();
    await sessao.entrar({ organizacao: 'a', email: 'b', senha: 'c' });

    expect(await sessao.renovarSessao()).toBeNull();
    expect(sessao.obterSessao()).toBeNull();
  });

  it('diante de 401 renova uma vez e repete a requisição com o token novo', async () => {
    const tokensUsados: string[] = [];
    let renovacoes = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn((entrada: RequestInfo | URL, opcoes?: RequestInit) => {
        const caminho = caminhoDe(entrada);
        if (caminho === '/api/autenticacao/login') return Promise.resolve(json(sessaoFalsa('VENCIDO')));
        if (caminho === '/api/autenticacao/renovar') {
          renovacoes += 1;
          return Promise.resolve(json(sessaoFalsa('NOVO')));
        }

        const autorizacao = new Headers(opcoes?.headers).get('Authorization') ?? '';
        tokensUsados.push(autorizacao);
        return Promise.resolve(new Response(null, { status: autorizacao === 'Bearer NOVO' ? 200 : 401 }));
      }),
    );
    const sessao = await carregar();
    await sessao.entrar({ organizacao: 'a', email: 'b', senha: 'c' });

    const respostas = await Promise.all([
      sessao.requisicaoAutenticada('/api/organizacao'),
      sessao.requisicaoAutenticada('/api/organizacao'),
    ]);

    expect(respostas.map((resposta) => resposta.status)).toEqual([200, 200]);
    expect(renovacoes).toBe(1);
    expect(tokensUsados.filter((token) => token === 'Bearer NOVO')).toHaveLength(2);
  });

  it('sair encerra a sessão local mesmo se a API não responder', async () => {
    const respostas: (() => Promise<Response>)[] = [
      () => Promise.resolve(json(sessaoFalsa('A1'))),
      () => Promise.reject(new Error('rede caiu')),
    ];
    vi.stubGlobal('fetch', vi.fn(() => respostas.shift()!()));
    const sessao = await carregar();
    await sessao.entrar({ organizacao: 'a', email: 'b', senha: 'c' });

    await expect(sessao.sair()).rejects.toThrow('rede caiu');
    expect(sessao.obterSessao()).toBeNull();
  });
});
