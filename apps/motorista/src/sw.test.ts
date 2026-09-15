import { describe, expect, it, vi } from 'vitest';

import codigoDoServiceWorker from '../public/sw.js?raw';

const ORIGEM = 'https://motorista.teste';
const HTML = '<html><head><script type="module" src="/assets/index-abc123.js"></script><link rel="stylesheet" href="/assets/index-def456.css"></head></html>';

type Ouvinte = (evento: Record<string, unknown>) => void;

function caminho(pedido: string | { url: string }): string {
  return typeof pedido === 'string' ? pedido : new URL(pedido.url).pathname;
}

/** Executa o service worker real com `self`, `caches` e `fetch` de mentira. */
function carregar(rede: (endereco: string) => Promise<Response>) {
  const ouvintes: Record<string, Ouvinte> = {};
  const armazens = new Map<string, Map<string, Response>>();

  const abrir = (nome: string) => {
    const armazem = armazens.get(nome) ?? new Map<string, Response>();
    armazens.set(nome, armazem);
    return {
      addAll: async (enderecos: string[]) => {
        for (const endereco of enderecos) armazem.set(endereco, await rede(endereco));
      },
      put: (pedido: string | { url: string }, resposta: Response) => {
        armazem.set(caminho(pedido), resposta);
        return Promise.resolve();
      },
      match: (pedido: string | { url: string }) => Promise.resolve(armazem.get(caminho(pedido))?.clone()),
    };
  };

  const caches = {
    open: (nome: string) => Promise.resolve(abrir(nome)),
    match: (pedido: string | { url: string }) =>
      Promise.resolve([...armazens.values()].map((armazem) => armazem.get(caminho(pedido))).find(Boolean)?.clone()),
    keys: () => Promise.resolve([...armazens.keys()]),
    delete: (nome: string) => Promise.resolve(armazens.delete(nome)),
  };

  const self = {
    addEventListener: (tipo: string, ouvinte: Ouvinte) => {
      ouvintes[tipo] = ouvinte;
    },
    skipWaiting: vi.fn(() => Promise.resolve()),
    clients: { claim: vi.fn(() => Promise.resolve()) },
    location: { origin: ORIGEM },
  };

  const buscar = vi.fn((pedido: string | { url: string }) => rede(caminho(pedido)));

  // O arquivo é servido como está ao navegador; aqui ele roda com as dependências de ambiente injetadas.
  // eslint-disable-next-line @typescript-eslint/no-implied-eval
  const executar = new Function('self', 'caches', 'fetch', 'Response', codigoDoServiceWorker) as (...dependencias: unknown[]) => void;
  executar(self, caches, buscar, Response);

  async function disparar(tipo: string, dados: Record<string, unknown> = {}) {
    let espera: Promise<unknown> = Promise.resolve();
    let resposta: Promise<Response> | undefined;
    ouvintes[tipo]!({
      ...dados,
      waitUntil: (promessa: Promise<unknown>) => {
        espera = promessa;
      },
      respondWith: (promessa: Promise<Response>) => {
        resposta = promessa;
      },
    });
    await espera;
    return resposta;
  }

  return { disparar, armazens, buscar };
}

const redeOnline = (endereco: string) =>
  Promise.resolve(new Response(endereco === '/' ? HTML : `conteudo de ${endereco}`, { status: 200 }));

const pedido = (endereco: string, extra: { method?: string; mode?: string } = {}) => ({
  request: { url: new URL(endereco, ORIGEM).href, method: extra.method ?? 'GET', mode: extra.mode ?? 'cors' },
});

describe('service worker da PWA', () => {
  it('na instalação guarda a casca e os recursos referenciados no HTML', async () => {
    const sw = carregar(redeOnline);

    await sw.disparar('install');

    const guardados = [...sw.armazens.values()].flatMap((armazem) => [...armazem.keys()]);
    expect(guardados).toEqual(expect.arrayContaining(['/', '/manifest.webmanifest', '/icone.svg', '/assets/index-abc123.js', '/assets/index-def456.css']));
  });

  it('nunca intercepta a API, outra origem ou envio', async () => {
    const sw = carregar(redeOnline);

    expect(await sw.disparar('fetch', pedido('/api/motorista/rotas'))).toBeUndefined();
    expect(await sw.disparar('fetch', pedido('https://api.torre.teste/api/motorista/sincronizacao'))).toBeUndefined();
    expect(await sw.disparar('fetch', pedido('/assets/index-abc123.js', { method: 'POST' }))).toBeUndefined();
  });

  it('sem internet, qualquer tela do aplicativo abre pela casca guardada', async () => {
    let online = true;
    const sw = carregar((endereco) => (online ? redeOnline(endereco) : Promise.reject(new TypeError('Failed to fetch'))));
    await sw.disparar('install');

    online = false;
    const resposta = await sw.disparar('fetch', pedido('/entregas/e1', { mode: 'navigate' }));

    expect(await resposta!.text()).toBe(HTML);
  });

  it('recurso com hash vem do cache sem ir à rede', async () => {
    const sw = carregar(redeOnline);
    await sw.disparar('install');
    sw.buscar.mockClear();

    const resposta = await sw.disparar('fetch', pedido('/assets/index-abc123.js'));

    expect(await resposta!.text()).toBe('conteudo de /assets/index-abc123.js');
    expect(sw.buscar).not.toHaveBeenCalled();
  });

  it('ao ativar apaga a casca de versões antigas', async () => {
    const sw = carregar(redeOnline);
    sw.armazens.set('torre-motorista-v0', new Map());
    await sw.disparar('install');

    await sw.disparar('activate');

    expect([...sw.armazens.keys()]).toEqual(['torre-motorista-v1']);
  });
});
