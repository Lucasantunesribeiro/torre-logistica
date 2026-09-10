import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { describe, expect, it } from 'vitest';

import { App } from './App';

function montar(rotaInicial = '/') {
  return render(
    <MemoryRouter initialEntries={[rotaInicial]}>
      <App />
    </MemoryRouter>,
  );
}

describe('Rastreamento público', () => {
  it('abre na página de acompanhamento', () => {
    montar();

    expect(
      screen.getByRole('heading', { level: 2, name: 'Acompanhamento de entrega' }),
    ).toBeVisible();
  });

  it('mostra página não encontrada em rota inexistente', () => {
    montar('/token/invalido');

    expect(screen.getByRole('heading', { level: 2, name: 'Página não encontrada' })).toBeVisible();
  });

  /**
   * A página é para o destinatário da entrega: ela não deve expor estado interno do
   * sistema, como saúde de dependências da operação.
   */
  it('não expõe estado interno da operação', () => {
    montar();

    expect(screen.queryByRole('status')).toBeNull();
    expect(screen.queryByText(/Operação/)).toBeNull();
  });
});
