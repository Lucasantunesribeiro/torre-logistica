import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';

type Rota = (opcoes?: RequestInit) => Response;

const SESSAO = {
  tokenDeAcesso: 'TOKEN',
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

function json(corpo: unknown, status = 200): Response {
  return new Response(JSON.stringify(corpo), { status, headers: { 'Content-Type': 'application/json' } });
}

function urlDe(entrada: RequestInfo | URL): string {
  return typeof entrada === 'string' ? entrada : entrada instanceof URL ? entrada.href : entrada.url;
}

function servidor(rotas: Record<string, Rota>) {
  const chamadas = vi.fn((entrada: RequestInfo | URL, opcoes?: RequestInit) => {
    const rota = rotas[new URL(urlDe(entrada)).pathname];
    return Promise.resolve(rota ? rota(opcoes) : new Response(null, { status: 404 }));
  });
  vi.stubGlobal('fetch', chamadas);
  return chamadas;
}

const prontidao: Rota = () => json({ status: 'Healthy', duracaoEmMs: 3 });
const semSessao: Rota = () => json({ codigo: 'sessao_invalida' }, 401);

// A sessão vive em memória de módulo: cada teste carrega a aplicação do zero.
async function montar(rotaInicial = '/') {
  vi.resetModules();
  const { App } = await import('./App');
  const { ProvedorDeSessao } = await import('./sessao/ProvedorDeSessao');
  const cliente = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return render(
    <QueryClientProvider client={cliente}>
      <MemoryRouter initialEntries={[rotaInicial]}>
        <ProvedorDeSessao>
          <App />
        </ProvedorDeSessao>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function preencherLogin(senha = 'senha-bem-comprida') {
  fireEvent.change(screen.getByLabelText('Organização'), { target: { value: 'transportadora-aurora' } });
  fireEvent.change(screen.getByLabelText('E-mail'), { target: { value: 'paula@aurora.test' } });
  fireEvent.change(screen.getByLabelText('Senha'), { target: { value: senha } });
  fireEvent.click(screen.getByRole('button', { name: 'Entrar' }));
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('Console Operacional', () => {
  it('sem sessão válida leva ao login', async () => {
    servidor({ '/health/ready': prontidao, '/api/autenticacao/renovar': semSessao });

    await montar('/');

    expect(await screen.findByRole('heading', { level: 2, name: 'Entrar no console' })).toBeVisible();
  });

  it('recupera a sessão pelo cookie ao abrir, sem pedir senha', async () => {
    servidor({ '/health/ready': prontidao, '/api/autenticacao/renovar': () => json(SESSAO) });

    await montar('/');

    expect(await screen.findByRole('heading', { level: 2, name: 'Paula Siqueira' })).toBeVisible();
    expect(screen.getByText('Operador · Transportadora Aurora')).toBeVisible();
  });

  it('login bem-sucedido abre o painel', async () => {
    const chamadas = servidor({
      '/health/ready': prontidao,
      '/api/autenticacao/renovar': semSessao,
      '/api/autenticacao/login': () => json(SESSAO),
    });
    await montar('/');
    await screen.findByLabelText('Organização');

    preencherLogin();

    expect(await screen.findByRole('heading', { level: 2, name: 'Paula Siqueira' })).toBeVisible();
    const login = chamadas.mock.calls.find(([entrada]) => urlDe(entrada).endsWith('/api/autenticacao/login'));
    expect(JSON.parse(login?.[1]?.body as string)).toEqual({
      organizacao: 'transportadora-aurora',
      email: 'paula@aurora.test',
      senha: 'senha-bem-comprida',
    });
  });

  it('login recusado mostra mensagem neutra e limpa a senha', async () => {
    servidor({
      '/health/ready': prontidao,
      '/api/autenticacao/renovar': semSessao,
      '/api/autenticacao/login': () => json({ codigo: 'credenciais_invalidas' }, 401),
    });
    await montar('/entrar');
    await screen.findByLabelText('Organização');

    preencherLogin('senha-errada-comprida');

    expect(await screen.findByRole('alert')).toHaveTextContent('Organização, e-mail ou senha incorretos.');
    expect(screen.getByLabelText('Senha')).toHaveValue('');
  });

  it('limite de tentativas mostra orientação de espera', async () => {
    servidor({
      '/health/ready': prontidao,
      '/api/autenticacao/renovar': semSessao,
      '/api/autenticacao/login': () => json({ codigo: 'limite_de_requisicoes' }, 429),
    });
    await montar('/entrar');
    await screen.findByLabelText('Organização');

    preencherLogin();

    expect(await screen.findByRole('alert')).toHaveTextContent('Aguarde um minuto');
  });

  it('sair encerra a sessão e volta ao login', async () => {
    const chamadas = servidor({
      '/health/ready': prontidao,
      '/api/autenticacao/renovar': () => json(SESSAO),
      '/api/autenticacao/sair': () => new Response(null, { status: 204 }),
    });
    await montar('/');

    fireEvent.click(await screen.findByRole('button', { name: 'Sair' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Entrar no console' })).toBeVisible();
    expect(chamadas.mock.calls.some(([entrada]) => urlDe(entrada).endsWith('/api/autenticacao/sair'))).toBe(true);
  });

  it('mostra tela não encontrada em rota inexistente', async () => {
    servidor({ '/health/ready': prontidao, '/api/autenticacao/renovar': semSessao });

    await montar('/rota/inexistente');

    expect(await screen.findByRole('heading', { level: 2, name: 'Tela não encontrada' })).toBeVisible();
  });

  it('anuncia a operação conectada quando a API responde pronta', async () => {
    servidor({ '/health/ready': prontidao, '/api/autenticacao/renovar': semSessao });

    await montar('/entrar');

    expect(await screen.findByText('Operação conectada')).toBeVisible();
  });
});
