import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { App } from './App';

const TOKEN = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';

function montar(rotaInicial = '/') {
  return render(
    <MemoryRouter initialEntries={[rotaInicial]}>
      <App />
    </MemoryRouter>,
  );
}

function responderCom(corpo: unknown, status = 200) {
  vi.stubGlobal(
    'fetch',
    vi.fn(() =>
      Promise.resolve({
        ok: status >= 200 && status < 300,
        status,
        json: () => Promise.resolve(corpo),
      } as unknown as Response),
    ),
  );
}

function acompanhamento(ajustes: Record<string, unknown> = {}) {
  return {
    codigo: 'ENT-2026-000042',
    status: 'EmRota',
    janelaDe: '2026-09-17T13:00:00+00:00',
    janelaAte: '2026-09-17T17:00:00+00:00',
    destino: { bairro: 'Cambuí', cidade: 'Campinas', uf: 'SP' },
    chegadaPrevistaEm: '2026-09-17T14:30:00+00:00',
    situacao: 'Normal',
    posicao: {
      latitude: -22.91,
      longitude: -47.06,
      precisaoAproximadaEmMetros: 1100,
      atualizadaEm: '2026-09-17T14:05:00+00:00',
    },
    comprovante: null,
    marcos: [
      { tipo: 'Criada', ocorridoEm: '2026-09-16T09:00:00+00:00' },
      { tipo: 'SaiuParaRota', ocorridoEm: '2026-09-17T13:10:00+00:00' },
    ],
    consultadoEm: '2026-09-17T14:06:00+00:00',
    ...ajustes,
  };
}

describe('Rastreamento público', () => {
  beforeEach(() => {
    vi.unstubAllGlobals();
  });

  it('sem link, explica que o acompanhamento vem do remetente e não oferece busca', () => {
    montar();

    expect(
      screen.getByRole('heading', { level: 2, name: 'Acompanhamento de entrega' }),
    ).toBeVisible();
    expect(screen.getByText(/link que você recebeu do remetente/i)).toBeVisible();

    // Campo de busca por código transformaria a página num balcão para descobrir entregas alheias.
    expect(screen.queryByRole('textbox')).toBeNull();
    expect(screen.queryByRole('searchbox')).toBeNull();
  });

  it('mostra o andamento da entrega do link', async () => {
    responderCom(acompanhamento());

    montar(`/e/${TOKEN}`);

    expect(await screen.findByText('Pedido ENT-2026-000042')).toBeVisible();
    expect(screen.getByText('A caminho')).toBeVisible();
    expect(screen.getByText('Cambuí, Campinas — SP')).toBeVisible();
    expect(screen.getByText('Saiu para entrega')).toBeVisible();
    expect(screen.getByText(/posição é aproximada/i)).toBeVisible();
  });

  it('mostra o comprovante depois da entrega concluída', async () => {
    responderCom(
      acompanhamento({
        status: 'Entregue',
        posicao: null,
        comprovante: {
          recebidoPor: 'Carla Nunes',
          registradoEm: '2026-09-17T14:40:00+00:00',
          arquivos: [
            {
              tipo: 'Foto',
              url: 'http://api.teste.local/api/arquivos/prova.jpg?assinatura=x',
              urlExpiraEm: '2026-09-17T14:45:00+00:00',
            },
          ],
        },
      }),
    );

    montar(`/e/${TOKEN}`);

    expect(await screen.findByText('Entregue')).toBeVisible();
    expect(screen.getByText(/Recebido por Carla Nunes/)).toBeVisible();
    expect(screen.getByRole('link', { name: 'Ver foto da entrega' })).toBeVisible();
  });

  /**
   * Link inválido, expirado e revogado chegam aqui como o mesmo 404 — e a página não tenta
   * adivinhar qual foi, porque a API de propósito não conta.
   */
  it('trata link que não abre nada sem revelar o motivo', async () => {
    responderCom({}, 404);

    montar(`/e/${TOKEN}`);

    expect(await screen.findByText(/não é válido ou expirou/i)).toBeVisible();
    expect(screen.queryByText(/revogad/i)).toBeNull();
    expect(screen.queryByText(/expirou em/i)).toBeNull();
  });

  it('avisa sem repassar mensagem do servidor quando a consulta falha', async () => {
    responderCom({ detail: 'Npgsql.PostgresException: relation does not exist' }, 500);

    montar(`/e/${TOKEN}`);

    expect(await screen.findByText(/Tente de novo em alguns instantes/i)).toBeVisible();
    expect(screen.queryByText(/Npgsql/)).toBeNull();
  });

  /**
   * A página é para o destinatário da entrega: ela não deve expor estado interno do sistema,
   * nem o próprio token, que é a credencial do link.
   */
  it('não expõe estado interno da operação nem o token do link', async () => {
    responderCom(acompanhamento());

    const { container } = montar(`/e/${TOKEN}`);
    await screen.findByText('Pedido ENT-2026-000042');

    expect(container.textContent).not.toContain(TOKEN);
    expect(screen.queryByText(/Operação/)).toBeNull();
    expect(screen.queryByText(/motorista/i)).toBeNull();
    expect(screen.queryByText(/-22.91/)).toBeNull();
  });

  it('mostra página não encontrada em rota inexistente', () => {
    montar('/nao/existe');

    expect(screen.getByRole('heading', { level: 2, name: 'Página não encontrada' })).toBeVisible();
  });
});
