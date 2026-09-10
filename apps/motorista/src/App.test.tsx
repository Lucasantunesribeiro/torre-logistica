import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { App } from './App';

function montar(rotaInicial = '/') {
  const cliente = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return render(
    <QueryClientProvider client={cliente}>
      <MemoryRouter initialEntries={[rotaInicial]}>
        <App />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('PWA do motorista', () => {
  it('abre na rota do dia', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        Promise.resolve(
          new Response(JSON.stringify({ status: 'Healthy', duracaoEmMs: 2 }), { status: 200 }),
        ),
      ),
    );

    montar();

    expect(screen.getByRole('heading', { level: 2, name: 'Rota do dia' })).toBeVisible();
  });

  it('mostra tela não encontrada em rota inexistente', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        Promise.resolve(
          new Response(JSON.stringify({ status: 'Healthy', duracaoEmMs: 2 }), { status: 200 }),
        ),
      ),
    );

    montar('/nao/existe');

    expect(screen.getByRole('heading', { level: 2, name: 'Tela não encontrada' })).toBeVisible();
  });
});
