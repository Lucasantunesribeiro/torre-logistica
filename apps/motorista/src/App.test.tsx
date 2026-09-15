import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';

type Manipulador = (corpo: unknown) => Response;

interface EntregaFalsa {
  status: string;
  nome: string;
  tentativas: number;
  motivo: string | null;
}

const SESSAO = {
  tokenDeAcesso: 'TOKEN',
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

const ENDERECO = {
  logradouro: 'Rua das Palmeiras',
  numero: '120',
  complemento: null,
  bairro: 'Centro',
  cidade: 'Campinas',
  uf: 'SP',
  cep: '13010000',
};

const JANELA = { de: '2026-09-15T12:00:00Z', ate: '2026-09-15T15:00:00Z' };

function json(corpo: unknown, status = 200): Response {
  return new Response(JSON.stringify(corpo), { status, headers: { 'Content-Type': 'application/json' } });
}

function urlDe(entrada: RequestInfo | URL): string {
  return typeof entrada === 'string' ? entrada : entrada instanceof URL ? entrada.href : entrada.url;
}

/**
 * Servidor falso com estado: aplica os comandos do motorista como a máquina de estados da API, para o
 * fluxo inteiro rodar pela interface.
 */
function servidorDoMotorista(opcoes: { rota?: string; extras?: Record<string, Manipulador> } = {}) {
  const estado = {
    rota: opcoes.rota ?? 'Planejada',
    entregas: {
      e1: { status: opcoes.rota === 'EmAndamento' ? 'EmRota' : 'Atribuida', nome: 'Carla Nunes', tentativas: 0, motivo: null },
      e2: { status: opcoes.rota === 'EmAndamento' ? 'EmRota' : 'Atribuida', nome: 'Bruno Alves', tentativas: 0, motivo: null },
    } as Record<string, EntregaFalsa>,
  };
  const comandos: string[] = [];

  const entregaJson = (id: string, sequencia: number) => {
    const entrega = estado.entregas[id]!;
    return {
      id,
      codigo: `ENT-2026-00000${sequencia}`,
      status: entrega.status,
      rotaId: 'r1',
      sequencia,
      destinatario: { nome: entrega.nome, telefone: '11 98765-4321', instrucoesDeEntrega: 'Portaria 24h' },
      endereco: ENDERECO,
      localizacao: { latitude: -22.91, longitude: -47.06 },
      janelaPrometida: JANELA,
      observacoes: null,
      execucao: {
        saiuParaRotaEm: null,
        chegadaRegistradaEm: null,
        entregueEm: null,
        tentativasFrustradas: entrega.tentativas,
        motivoDaUltimaTentativa: entrega.motivo,
        ultimaTentativaFrustradaEm: entrega.motivo ? '2026-09-15T13:00:00Z' : null,
      },
    };
  };

  const resolvida = (status: string) => ['Entregue', 'TentativaFrustrada', 'Reagendada', 'Cancelada'].includes(status);

  const rotas: Record<string, Manipulador> = {
    'GET /health/ready': () => json({ status: 'Healthy', duracaoEmMs: 2 }),
    'POST /api/motorista/autenticacao/renovar': () => json(SESSAO),
    'POST /api/motorista/autenticacao/sair': () => new Response(null, { status: 204 }),
    'GET /api/motorista/rotas': () =>
      json(
        estado.rota === 'Concluida'
          ? []
          : [
              {
                id: 'r1',
                codigo: 'ROT-2026-0001',
                data: '2026-09-15',
                status: estado.rota,
                saidaPlanejada: '2026-09-15T11:00:00Z',
                iniciadaEm: null,
                totalDeParadas: 2,
                paradasPendentes: Object.values(estado.entregas).filter((entrega) => !resolvida(entrega.status)).length,
              },
            ],
      ),
    'GET /api/motorista/rotas/r1': () =>
      json({
        id: 'r1',
        codigo: 'ROT-2026-0001',
        data: '2026-09-15',
        status: estado.rota,
        saidaPlanejada: '2026-09-15T11:00:00Z',
        iniciadaEm: null,
        concluidaEm: null,
        hubNome: 'Hub Campinas',
        veiculo: { placa: 'ABC1D23', identificacao: 'Fiorino 04' },
        paradas: ['e1', 'e2'].map((id, indice) => ({
          sequencia: indice + 1,
          entregaId: id,
          codigoDaEntrega: `ENT-2026-00000${indice + 1}`,
          status: estado.entregas[id]!.status,
          destinatarioNome: estado.entregas[id]!.nome,
          endereco: ENDERECO,
          janelaPrometida: JANELA,
        })),
      }),
    'GET /api/motorista/entregas/e1': () => json(entregaJson('e1', 1)),
    'GET /api/motorista/entregas/e2': () => json(entregaJson('e2', 2)),
    'POST /api/motorista/rotas/r1/inicio': () => {
      estado.rota = 'EmAndamento';
      Object.values(estado.entregas).forEach((entrega) => {
        entrega.status = 'EmRota';
      });
      return json({});
    },
    'POST /api/motorista/rotas/r1/conclusao': () => {
      estado.rota = 'Concluida';
      return json({});
    },
    'POST /api/motorista/posicoes': () => json({ recebidas: 1, aceitas: 1, duplicadas: 0, rejeitadas: 0 }),
  };

  for (const id of ['e1', 'e2']) {
    rotas[`POST /api/motorista/entregas/${id}/chegada`] = () => {
      estado.entregas[id]!.status = 'ProximaDoDestino';
      return json({});
    };
    rotas[`POST /api/motorista/entregas/${id}/conclusao`] = () => {
      estado.entregas[id]!.status = 'Entregue';
      return json({});
    };
    rotas[`POST /api/motorista/entregas/${id}/tentativa-frustrada`] = (corpo) => {
      const entrega = estado.entregas[id]!;
      entrega.status = 'TentativaFrustrada';
      entrega.tentativas += 1;
      entrega.motivo = (corpo as { motivo: string }).motivo;
      return json({});
    };
  }

  Object.assign(rotas, opcoes.extras);

  const chamadas = vi.fn((entrada: RequestInfo | URL, init?: RequestInit) => {
    const metodo = init?.method ?? 'GET';
    const chave = `${metodo} ${new URL(urlDe(entrada)).pathname}`;
    const corpo: unknown = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined;

    if (metodo === 'POST' && chave.startsWith('POST /api/motorista/') && !chave.includes('/autenticacao/') && !chave.endsWith('/posicoes')) {
      comandos.push(chave);
    }

    const manipulador = rotas[chave];
    return Promise.resolve(manipulador ? manipulador(corpo) : json({ codigo: 'nao_encontrado' }, 404));
  });

  vi.stubGlobal('fetch', chamadas);
  return { estado, comandos, chamadas };
}

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

function simularGeolocalizacao(comportamento: 'negar' | 'liberar') {
  const geolocalizacao = {
    watchPosition: vi.fn((sucesso: PositionCallback, falha?: PositionErrorCallback | null) => {
      if (comportamento === 'negar') {
        falha?.({ code: 1, PERMISSION_DENIED: 1, POSITION_UNAVAILABLE: 2, TIMEOUT: 3, message: 'negado' });
      } else {
        sucesso({
          timestamp: Date.now(),
          coords: { latitude: -22.9, longitude: -47.06, accuracy: 8, speed: null, heading: null, altitude: null, altitudeAccuracy: null },
        } as GeolocationPosition);
      }

      return 7;
    }),
    clearWatch: vi.fn(),
    getCurrentPosition: vi.fn(),
  };

  Object.defineProperty(navigator, 'geolocation', { value: geolocalizacao, configurable: true });
  return geolocalizacao;
}

afterEach(() => {
  vi.unstubAllGlobals();
  Object.defineProperty(navigator, 'geolocation', { value: undefined, configurable: true });
  Object.defineProperty(navigator, 'onLine', { value: true, configurable: true });
});

describe('PWA do motorista', () => {
  it('sem sessão leva ao login do canal do motorista', async () => {
    servidorDoMotorista({ extras: { 'POST /api/motorista/autenticacao/renovar': () => json({ codigo: 'sessao_invalida' }, 401) } });

    await montar('/');

    expect(await screen.findByRole('heading', { level: 2, name: 'Entrar no aplicativo' })).toBeVisible();
  });

  it('login pelo canal do motorista abre a rota do dia', async () => {
    let logado = false;
    const { chamadas } = servidorDoMotorista({
      extras: {
        'POST /api/motorista/autenticacao/renovar': () => (logado ? json(SESSAO) : json({ codigo: 'sessao_invalida' }, 401)),
        'POST /api/motorista/autenticacao/login': () => {
          logado = true;
          return json(SESSAO);
        },
      },
    });
    await montar('/');
    await screen.findByLabelText('Organização');

    fireEvent.change(screen.getByLabelText('Organização'), { target: { value: 'transportadora-aurora' } });
    fireEvent.change(screen.getByLabelText('E-mail'), { target: { value: 'rafael@aurora.test' } });
    fireEvent.change(screen.getByLabelText('Senha'), { target: { value: 'senha-bem-comprida' } });
    fireEvent.click(screen.getByRole('button', { name: 'Entrar' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Rota do dia' })).toBeVisible();
    expect(chamadas.mock.calls.some(([entrada]) => urlDe(entrada).endsWith('/api/motorista/autenticacao/login'))).toBe(true);
    expect(chamadas.mock.calls.some(([entrada]) => urlDe(entrada).endsWith('/api/autenticacao/login'))).toBe(false);
  });

  /** Critério de aceite da Fase 11: o fluxo básico inteiro usando só a PWA. */
  it('motorista completa o fluxo básico só pela PWA', async () => {
    const { estado, comandos } = servidorDoMotorista();
    await montar('/');

    fireEvent.click(await screen.findByRole('button', { name: 'Iniciar rota' }));

    // Próxima entrega: a primeira da rota.
    expect(await screen.findByRole('heading', { level: 3, name: 'Próxima entrega' })).toBeVisible();
    fireEvent.click(await screen.findByRole('link', { name: 'Abrir entrega' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Carla Nunes' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Cheguei ao destino' }));
    await waitFor(() => {
      expect(estado.entregas.e1!.status).toBe('ProximaDoDestino');
    });

    // Concluir pede confirmação.
    fireEvent.click(await screen.findByRole('button', { name: 'Entrega concluída' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Confirmar entrega concluída' }));

    // De volta à rota: a próxima é a segunda.
    expect(await screen.findByText('Bruno Alves')).toBeVisible();
    fireEvent.click(screen.getByRole('link', { name: 'Abrir entrega' }));
    expect(await screen.findByRole('heading', { level: 2, name: 'Bruno Alves' })).toBeVisible();

    // Ocorrência com motivo tipado.
    fireEvent.click(screen.getByRole('link', { name: 'Não foi possível entregar' }));
    expect(await screen.findByRole('heading', { level: 2, name: 'Registrar ocorrência' })).toBeVisible();
    const registrar = screen.getByRole('button', { name: 'Registrar tentativa sem sucesso' });
    expect(registrar).toBeDisabled();
    fireEvent.click(screen.getByLabelText('Local fechado'));
    fireEvent.click(registrar);

    // Tudo resolvido: encerrar a rota.
    fireEvent.click(await screen.findByRole('button', { name: 'Encerrar rota' }));
    expect(await screen.findByText('Nenhuma rota para você agora.')).toBeVisible();

    expect(comandos).toEqual([
      'POST /api/motorista/rotas/r1/inicio',
      'POST /api/motorista/entregas/e1/chegada',
      'POST /api/motorista/entregas/e1/conclusao',
      'POST /api/motorista/entregas/e2/tentativa-frustrada',
      'POST /api/motorista/rotas/r1/conclusao',
    ]);
    expect(estado.entregas.e2).toMatchObject({ status: 'TentativaFrustrada', motivo: 'LocalFechado' });
  });

  it('lista de paradas mostra a ordem da rota e abre o detalhe', async () => {
    servidorDoMotorista({ rota: 'EmAndamento' });
    await montar('/rotas/r1/paradas');

    expect(await screen.findByRole('heading', { level: 2, name: 'Paradas da rota ROT-2026-0001' })).toBeVisible();
    const itens = screen.getAllByRole('listitem');
    expect(itens.map((item) => item.textContent)).toEqual([expect.stringContaining('Carla Nunes'), expect.stringContaining('Bruno Alves')]);

    fireEvent.click(screen.getByRole('link', { name: /Bruno Alves/ }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Bruno Alves' })).toBeVisible();
    expect(screen.getByRole('link', { name: 'Abrir no mapa' })).toHaveAttribute('href', expect.stringContaining('-22.91%2C-47.06'));
    expect(screen.getByRole('link', { name: 'Ligar para Bruno Alves' })).toHaveAttribute('href', 'tel:11987654321');
  });

  it('sessão encerrada no meio da rota volta ao login', async () => {
    let renovacoes = 0;
    servidorDoMotorista({
      extras: {
        'POST /api/motorista/autenticacao/renovar': () => {
          renovacoes += 1;
          return renovacoes === 1 ? json(SESSAO) : json({ codigo: 'sessao_invalida' }, 401);
        },
        'GET /api/motorista/rotas': () => json({ codigo: 'nao_autenticado' }, 401),
      },
    });

    await montar('/');

    expect(await screen.findByRole('heading', { level: 2, name: 'Entrar no aplicativo' })).toBeVisible();
  });

  it('entrega passada a outro motorista explica o que aconteceu', async () => {
    servidorDoMotorista({
      rota: 'EmAndamento',
      extras: { 'GET /api/motorista/entregas/e1': () => json({ codigo: 'entrega_reatribuida' }, 409) },
    });

    await montar('/entregas/e1');

    // Busca pelo texto: com rota em andamento, o aviso de localização também é um alerta na tela.
    expect(await screen.findByText(/passada para outro motorista/)).toBeVisible();
  });

  it('comando recusado mostra o motivo e atualiza a tela', async () => {
    const { estado } = servidorDoMotorista({
      rota: 'EmAndamento',
      extras: {
        'POST /api/motorista/entregas/e1/chegada': () => {
          estado.entregas.e1!.status = 'Cancelada';
          return json({ codigo: 'transicao_invalida' }, 409);
        },
      },
    });
    await montar('/entregas/e1');

    fireEvent.click(await screen.findByRole('button', { name: 'Cheguei ao destino' }));

    expect(await screen.findByText(/não vale mais para a situação atual/)).toBeVisible();
    expect(await screen.findByText('Resultado registrado: Cancelada.')).toBeVisible();
  });

  it('falha da operação mostra aviso e deixa tentar de novo', async () => {
    let falhar = true;
    servidorDoMotorista({
      extras: {
        'GET /api/motorista/rotas': () =>
          falhar
            ? json({ codigo: 'erro_inesperado' }, 500)
            : json([{ id: 'r1', codigo: 'ROT-2026-0001', data: '2026-09-15', status: 'Planejada', saidaPlanejada: null, iniciadaEm: null, totalDeParadas: 2, paradasPendentes: 2 }]),
      },
    });
    await montar('/');

    expect(await screen.findByRole('alert')).toHaveTextContent('A operação está com problema agora');
    falhar = false;
    fireEvent.click(screen.getByRole('button', { name: 'Tentar de novo' }));

    expect(await screen.findByRole('button', { name: 'Iniciar rota' })).toBeVisible();
  });

  it('sem rota liberada mostra o estado vazio', async () => {
    servidorDoMotorista({ extras: { 'GET /api/motorista/rotas': () => json([]) } });

    await montar('/');

    expect(await screen.findByText('Nenhuma rota para você agora.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Atualizar' })).toBeVisible();
  });

  it('localização negada avisa e não impede concluir a entrega', async () => {
    const geolocalizacao = simularGeolocalizacao('negar');
    const { estado } = servidorDoMotorista({ rota: 'EmAndamento' });
    await montar('/entregas/e1');

    expect(await screen.findByText(/Localização bloqueada/)).toBeVisible();
    expect(geolocalizacao.watchPosition).toHaveBeenCalled();

    fireEvent.click(await screen.findByRole('button', { name: 'Entrega concluída' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Confirmar entrega concluída' }));

    await waitFor(() => {
      expect(estado.entregas.e1!.status).toBe('Entregue');
    });
  });

  it('localização só é pedida com rota em andamento', async () => {
    const geolocalizacao = simularGeolocalizacao('liberar');
    servidorDoMotorista({ rota: 'Planejada' });
    await montar('/');

    await screen.findByRole('button', { name: 'Iniciar rota' });
    expect(geolocalizacao.watchPosition).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: 'Iniciar rota' }));

    expect(await screen.findByText(/Localização ativa/)).toBeVisible();
    expect(geolocalizacao.watchPosition).toHaveBeenCalledTimes(1);
  });

  it('sem internet no aparelho é anunciado na hora', async () => {
    Object.defineProperty(navigator, 'onLine', { value: false, configurable: true });
    servidorDoMotorista();

    await montar('/');

    expect(await screen.findByText('Sem internet no aparelho')).toBeVisible();
  });

  it('sair encerra a sessão e volta ao login', async () => {
    let logado = true;
    const { chamadas } = servidorDoMotorista({
      extras: {
        'POST /api/motorista/autenticacao/renovar': () => (logado ? json(SESSAO) : json({ codigo: 'sessao_invalida' }, 401)),
        'POST /api/motorista/autenticacao/sair': () => {
          logado = false;
          return new Response(null, { status: 204 });
        },
      },
    });
    await montar('/');

    fireEvent.click(await screen.findByRole('button', { name: 'Sair' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Entrar no aplicativo' })).toBeVisible();
    expect(chamadas.mock.calls.some(([entrada]) => urlDe(entrada).endsWith('/api/motorista/autenticacao/sair'))).toBe(true);
  });

  it('mostra tela não encontrada em rota inexistente', async () => {
    servidorDoMotorista();

    await montar('/nao/existe');

    expect(await screen.findByRole('heading', { level: 2, name: 'Tela não encontrada' })).toBeVisible();
  });
});
