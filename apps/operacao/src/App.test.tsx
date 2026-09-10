import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { App } from './App';

function montar(filho: ReactNode, rotaInicial = '/') {
  // `retry: false` no teste: a política de nova tentativa da aplicação existe para
  // o cold start da infraestrutura e aqui só faria o teste esperar por nada.
  const cliente = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return render(
    <QueryClientProvider client={cliente}>
      <MemoryRouter initialEntries={[rotaInicial]}>{filho}</MemoryRouter>
    </QueryClientProvider>,
  );
}

function responderProntidao(status: number, corpo: unknown) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () =>
      Promise.resolve(
        new Response(JSON.stringify(corpo), {
          status,
          headers: {
            'Content-Type': 'application/json',
            'X-Correlation-Id': '01a0892305427f2484dd965213b580e9',
          },
        }),
      ),
    ),
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('Console Operacional', () => {
  it('mostra o painel na rota inicial', () => {
    responderProntidao(200, { status: 'Healthy', duracaoEmMs: 3 });

    montar(<App />);

    expect(screen.getByRole('heading', { level: 1, name: 'Console Operacional' })).toBeVisible();
    expect(screen.getByRole('heading', { level: 2, name: 'Fundação técnica' })).toBeVisible();
  });

  it('anuncia a operação conectada quando a API responde pronta', async () => {
    responderProntidao(200, { status: 'Healthy', duracaoEmMs: 3 });

    montar(<App />);

    expect(await screen.findByText('Operação conectada')).toBeVisible();
  });

  /**
   * A distinção importa: a API respondeu, logo ela está no ar. Dizer "fora do ar"
   * aqui faria a demonstração parecer quebrada durante o cold start do banco.
   */
  it('separa dependência subindo de operação fora do ar', async () => {
    responderProntidao(503, { status: 'Unhealthy', duracaoEmMs: 21 });

    montar(<App />);

    expect(
      await screen.findByText('Operação respondendo, dependências ainda subindo'),
    ).toBeVisible();
  });

  it('avisa quando não consegue falar com a operação', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Promise.reject(new Error('rede caiu'))));

    montar(<App />);

    expect(await screen.findByText('Não foi possível falar com a operação')).toBeVisible();
  });

  it('mostra tela não encontrada em rota inexistente', () => {
    responderProntidao(200, { status: 'Healthy', duracaoEmMs: 3 });

    montar(<App />, '/rota/inexistente');

    expect(screen.getByRole('heading', { level: 2, name: 'Tela não encontrada' })).toBeVisible();
  });
});
