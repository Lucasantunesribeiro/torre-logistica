import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';

type Manipulador = (corpo: unknown) => Response;

interface EntregaFalsa {
  status: string;
  nome: string;
  tentativas: number;
  motivo: string | null;
}

interface OperacaoRecebida {
  operacaoDoClienteId: string;
  tipo: string;
  alvoId: string;
  motivo: string | null;
  criadaEm: string;
}

interface ResultadoFalso {
  operacaoDoClienteId: string;
  desfecho: string;
  repetida: boolean;
  codigo: string | null;
  mensagem: string | null;
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
 * Servidor falso com estado: aplica as operações da fila como a máquina de estados da API, com o registro
 * por identificador do aparelho — a mesma operação repetida devolve o resultado guardado, sem aplicar de novo.
 */
function servidorDoMotorista(
  opcoes: {
    rota?: string;
    extras?: Record<string, Manipulador>;
    /** Aplica a operação e perde a resposta no caminho — o aparelho não sabe que deu certo. */
    perderResposta?: () => boolean;
  } = {},
) {
  const estado = {
    rota: opcoes.rota ?? 'Planejada',
    entregas: {
      e1: { status: opcoes.rota === 'EmAndamento' ? 'EmRota' : 'Atribuida', nome: 'Carla Nunes', tentativas: 0, motivo: null },
      e2: { status: opcoes.rota === 'EmAndamento' ? 'EmRota' : 'Atribuida', nome: 'Bruno Alves', tentativas: 0, motivo: null },
    } as Record<string, EntregaFalsa>,
  };
  const rede = { ativa: true };
  const comandos: string[] = [];
  const lotes: OperacaoRecebida[][] = [];
  const respostas: ResultadoFalso[] = [];
  const registradas = new Map<string, ResultadoFalso>();

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
  const emExecucao = (status: string) => status === 'EmRota' || status === 'ProximaDoDestino';

  /** Devolve o código do conflito, ou null quando aplicou. */
  function aplicar(operacao: OperacaoRecebida): string | null {
    const entrega = estado.entregas[operacao.alvoId];
    switch (operacao.tipo) {
      case 'IniciarRota':
        if (estado.rota !== 'Planejada') return 'transicao_invalida';
        estado.rota = 'EmAndamento';
        Object.values(estado.entregas).forEach((item) => {
          if (item.status === 'Atribuida') item.status = 'EmRota';
        });
        return null;
      case 'RegistrarChegada':
        if (entrega?.status !== 'EmRota') return 'transicao_invalida';
        entrega.status = 'ProximaDoDestino';
        return null;
      case 'ConcluirEntrega':
        if (!entrega || !emExecucao(entrega.status)) return 'transicao_invalida';
        entrega.status = 'Entregue';
        return null;
      case 'RegistrarTentativaFrustrada':
        if (!entrega || !emExecucao(entrega.status)) return 'transicao_invalida';
        entrega.status = 'TentativaFrustrada';
        entrega.tentativas += 1;
        entrega.motivo = operacao.motivo;
        return null;
      case 'ConcluirRota':
        if (estado.rota !== 'EmAndamento' || !Object.values(estado.entregas).every((item) => resolvida(item.status))) return 'rota_com_entregas_pendentes';
        estado.rota = 'Concluida';
        return null;
      default:
        return 'tipo_de_operacao_invalido';
    }
  }

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
    'POST /api/motorista/posicoes': () => json({ recebidas: 1, aceitas: 1, duplicadas: 0, rejeitadas: 0 }),
    'POST /api/motorista/sincronizacao': (corpo) => {
      const { operacoes } = corpo as { operacoes: OperacaoRecebida[] };
      lotes.push(operacoes);

      const resultados = operacoes.map((operacao) => {
        const anterior = registradas.get(operacao.operacaoDoClienteId);
        if (anterior) {
          return { ...anterior, repetida: true };
        }

        const codigo = aplicar(operacao);
        const resultado: ResultadoFalso = codigo
          ? { operacaoDoClienteId: operacao.operacaoDoClienteId, desfecho: 'Conflito', repetida: false, codigo, mensagem: 'Transição inválida.' }
          : { operacaoDoClienteId: operacao.operacaoDoClienteId, desfecho: 'Aplicada', repetida: false, codigo: null, mensagem: null };
        registradas.set(operacao.operacaoDoClienteId, resultado);
        if (!codigo) comandos.push(`${operacao.tipo} ${operacao.alvoId}`);
        return resultado;
      });

      respostas.push(...resultados);
      if (opcoes.perderResposta?.()) {
        throw new TypeError('Failed to fetch');
      }

      return json({ resultados });
    },
  };

  Object.assign(rotas, opcoes.extras);

  const chamadas = vi.fn((entrada: RequestInfo | URL, init?: RequestInit) => {
    if (!rede.ativa) {
      return Promise.reject(new TypeError('Failed to fetch'));
    }

    const metodo = init?.method ?? 'GET';
    const chave = `${metodo} ${new URL(urlDe(entrada)).pathname}`;
    const corpo: unknown = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined;
    const manipulador = rotas[chave];

    try {
      return Promise.resolve(manipulador ? manipulador(corpo) : json({ codigo: 'nao_encontrado' }, 404));
    } catch (erro) {
      return Promise.reject(erro instanceof Error ? erro : new Error(String(erro)));
    }
  });

  vi.stubGlobal('fetch', chamadas);
  return { estado, comandos, lotes, respostas, chamadas, rede };
}

type Servidor = ReturnType<typeof servidorDoMotorista>;

/** Liga ou desliga a internet do aparelho, como o navegador anuncia. */
function conexao(servidor: Servidor, ligada: boolean) {
  servidor.rede.ativa = ligada;
  Object.defineProperty(navigator, 'onLine', { value: ligada, configurable: true });
  act(() => {
    window.dispatchEvent(new Event(ligada ? 'online' : 'offline'));
  });
}

// A sessão vive em memória de módulo: cada montagem carrega a aplicação do zero, como abrir o navegador. O
// IndexedDB do teste continua o mesmo entre montagens do mesmo teste — é o disco do aparelho.
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

async function abrirPrimeiraEntrega() {
  fireEvent.click(await screen.findByRole('link', { name: 'Abrir entrega' }));
  expect(await screen.findByRole('heading', { level: 2, name: 'Carla Nunes' })).toBeVisible();
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

  /** Critério de aceite da Fase 11, agora com toda ação pela fila do aparelho. */
  it('motorista completa o fluxo básico só pela PWA', async () => {
    const { estado, comandos, chamadas } = servidorDoMotorista();
    await montar('/');

    fireEvent.click(await screen.findByRole('button', { name: 'Iniciar rota' }));

    // Próxima entrega: a primeira da rota.
    expect(await screen.findByRole('heading', { level: 3, name: 'Próxima entrega' })).toBeVisible();
    await abrirPrimeiraEntrega();

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

    await waitFor(() => {
      expect(comandos).toEqual(['IniciarRota r1', 'RegistrarChegada e1', 'ConcluirEntrega e1', 'RegistrarTentativaFrustrada e2', 'ConcluirRota r1']);
    });
    expect(estado.entregas.e2).toMatchObject({ status: 'TentativaFrustrada', motivo: 'LocalFechado' });

    // Nenhuma ação crítica pelo comando direto: só pela sincronização, com identificador do aparelho.
    const caminhos = chamadas.mock.calls.map(([entrada]) => new URL(urlDe(entrada)).pathname);
    expect(caminhos.filter((caminho) => /\/(inicio|chegada|conclusao|tentativa-frustrada)$/.test(caminho))).toEqual([]);
  });

  it('motivo "Outro" só é registrado com descrição, e ela segue na operação', async () => {
    const servidor = servidorDoMotorista({ rota: 'EmAndamento' });
    await montar('/entregas/e1/ocorrencia');

    expect(await screen.findByRole('heading', { level: 2, name: 'Registrar ocorrência' })).toBeVisible();
    fireEvent.click(screen.getByLabelText('Outro motivo (descreva)'));

    const registrar = screen.getByRole('button', { name: 'Registrar tentativa sem sucesso' });
    expect(registrar).toBeDisabled();

    fireEvent.change(screen.getByLabelText('Descreva o que aconteceu'), { target: { value: 'Rua interditada por obra.' } });
    expect(registrar).toBeEnabled();
    fireEvent.click(registrar);

    await waitFor(() => {
      expect(servidor.comandos).toEqual(['RegistrarTentativaFrustrada e1']);
    });
    expect(servidor.lotes[0]![0]).toMatchObject({ motivo: 'Outro', observacao: 'Rua interditada por obra.' });
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

describe('PWA do motorista sem conexão', () => {
  it('offline e reconexão: ações ficam guardadas e seguem num único lote, na ordem', async () => {
    const servidor = servidorDoMotorista({ rota: 'EmAndamento' });
    await montar('/');
    await abrirPrimeiraEntrega();

    conexao(servidor, false);

    fireEvent.click(screen.getByRole('button', { name: 'Cheguei ao destino' }));
    // A tela reflete a ação na hora, sem servidor.
    await waitFor(() => {
      expect(screen.queryByRole('button', { name: 'Cheguei ao destino' })).not.toBeInTheDocument();
    });
    expect(await screen.findByText('1 ação guardada no aparelho, aguardando envio')).toBeVisible();

    fireEvent.click(screen.getByRole('button', { name: 'Entrega concluída' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Confirmar entrega concluída' }));

    // Rota do dia pela cópia guardada, com a conclusão aplicada: a próxima é a segunda.
    expect(await screen.findByText('Bruno Alves')).toBeVisible();
    expect(await screen.findByText('2 ações guardadas no aparelho, aguardando envio')).toBeVisible();
    expect(servidor.lotes).toEqual([]);

    conexao(servidor, true);

    await waitFor(() => {
      expect(servidor.comandos).toEqual(['RegistrarChegada e1', 'ConcluirEntrega e1']);
    });
    expect(servidor.lotes).toHaveLength(1);
    expect(servidor.lotes[0]!.map((operacao) => operacao.tipo)).toEqual(['RegistrarChegada', 'ConcluirEntrega']);
    await waitFor(() => {
      expect(screen.queryByText(/aguardando envio/)).not.toBeInTheDocument();
    });
  });

  it('fechar o aplicativo sem internet: reabre com a rota guardada e envia a fila quando a conexão volta', async () => {
    const servidor = servidorDoMotorista({ rota: 'EmAndamento' });
    const primeiraAbertura = await montar('/');
    await abrirPrimeiraEntrega();

    conexao(servidor, false);
    fireEvent.click(screen.getByRole('button', { name: 'Cheguei ao destino' }));
    expect(await screen.findByText('1 ação guardada no aparelho, aguardando envio')).toBeVisible();

    // O navegador fecha: memória, sessão e cache de consultas se vão; o IndexedDB fica.
    primeiraAbertura.unmount();
    await montar('/');

    expect(await screen.findByRole('heading', { level: 2, name: 'Rota do dia' })).toBeVisible();
    expect(await screen.findByText(/mostrando o que foi guardado/)).toBeVisible();
    expect(await screen.findByText('1 ação guardada no aparelho, aguardando envio')).toBeVisible();
    expect(await screen.findByText('No destino')).toBeVisible();

    conexao(servidor, true);

    await waitFor(() => {
      expect(servidor.comandos).toEqual(['RegistrarChegada e1']);
    });
    await waitFor(() => {
      expect(screen.queryByText(/aguardando envio/)).not.toBeInTheDocument();
    });
  });

  it('resposta perdida: a repetição recebe o mesmo resultado e nada é aplicado duas vezes', async () => {
    let perder = true;
    const servidor = servidorDoMotorista({
      rota: 'EmAndamento',
      perderResposta: () => {
        const agora = perder;
        perder = false;
        return agora;
      },
    });
    await montar('/');
    await abrirPrimeiraEntrega();

    fireEvent.click(screen.getByRole('button', { name: 'Cheguei ao destino' }));
    await waitFor(() => {
      expect(servidor.lotes).toHaveLength(1);
    });
    expect(await screen.findByText('1 ação guardada no aparelho, aguardando envio')).toBeVisible();

    // Nova tentativa, como ao voltar a conexão.
    conexao(servidor, true);

    await waitFor(() => {
      expect(servidor.lotes).toHaveLength(2);
    });
    expect(servidor.lotes[1]![0]!.operacaoDoClienteId).toBe(servidor.lotes[0]![0]!.operacaoDoClienteId);
    expect(servidor.respostas[1]).toMatchObject({ desfecho: 'Aplicada', repetida: true });
    expect(servidor.comandos).toEqual(['RegistrarChegada e1']);
    await waitFor(() => {
      expect(screen.queryByText(/aguardando envio/)).not.toBeInTheDocument();
    });
  });

  it('ação já sincronizada não é enviada de novo', async () => {
    const servidor = servidorDoMotorista({ rota: 'EmAndamento' });
    await montar('/');
    await abrirPrimeiraEntrega();

    fireEvent.click(screen.getByRole('button', { name: 'Cheguei ao destino' }));
    await waitFor(() => {
      expect(servidor.comandos).toEqual(['RegistrarChegada e1']);
    });
    await waitFor(() => {
      expect(screen.queryByText(/aguardando envio/)).not.toBeInTheDocument();
    });

    conexao(servidor, true);
    conexao(servidor, true);
    await new Promise((resolver) => setTimeout(resolver, 50));

    expect(servidor.lotes).toHaveLength(1);
  });

  it('conflito real: cancelamento feito enquanto o motorista estava sem internet não é sobrescrito', async () => {
    const servidor = servidorDoMotorista({ rota: 'EmAndamento' });
    await montar('/');
    await abrirPrimeiraEntrega();

    conexao(servidor, false);
    fireEvent.click(screen.getByRole('button', { name: 'Entrega concluída' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Confirmar entrega concluída' }));
    expect(await screen.findByText('1 ação guardada no aparelho, aguardando envio')).toBeVisible();

    // Enquanto isso, a operação cancela a entrega.
    servidor.estado.entregas.e1!.status = 'Cancelada';
    conexao(servidor, true);

    expect(await screen.findByText(/A situação mudou antes de a ação chegar/)).toBeVisible();
    expect(screen.getByText('Conclusão da entrega de Carla Nunes')).toBeVisible();
    expect(servidor.estado.entregas.e1!.status).toBe('Cancelada');
    expect(servidor.comandos).toEqual([]);

    // Estado recuperável: a tela passa a mostrar o cancelamento, e o motorista confirma que viu.
    fireEvent.click(screen.getByRole('link', { name: /Ver todas as paradas/ }));
    expect(await screen.findByText(/Cancelada · janela/)).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Entendi' }));
    await waitFor(() => {
      expect(screen.queryByText(/A situação mudou antes de a ação chegar/)).not.toBeInTheDocument();
    });
  });

  it('sair com ações não enviadas avisa que elas ficam guardadas', async () => {
    const servidor = servidorDoMotorista({ rota: 'EmAndamento' });
    await montar('/');
    await abrirPrimeiraEntrega();

    conexao(servidor, false);
    fireEvent.click(screen.getByRole('button', { name: 'Cheguei ao destino' }));
    expect(await screen.findByText('1 ação guardada no aparelho, aguardando envio')).toBeVisible();

    fireEvent.click(screen.getByRole('button', { name: 'Sair' }));

    expect(await screen.findByText('Há 1 ação ainda não enviada.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Sair mesmo assim' })).toBeVisible();
  });

  it('saída feita sem internet é confirmada no servidor antes de reabrir a sessão', async () => {
    const servidor = servidorDoMotorista({ rota: 'EmAndamento' });
    const primeiraAbertura = await montar('/');
    await screen.findByRole('heading', { level: 2, name: 'Rota do dia' });

    conexao(servidor, false);
    fireEvent.click(screen.getByRole('button', { name: 'Sair' }));
    expect(await screen.findByRole('heading', { level: 2, name: 'Entrar no aplicativo' })).toBeVisible();

    primeiraAbertura.unmount();
    conexao(servidor, true);
    servidor.chamadas.mockClear();
    await montar('/');

    // O cookie ainda renovaria a sessão no servidor; o aparelho encerra antes, e pede login.
    expect(await screen.findByRole('heading', { level: 2, name: 'Entrar no aplicativo' })).toBeVisible();
    const caminhos = servidor.chamadas.mock.calls.map(([entrada]) => new URL(urlDe(entrada)).pathname);
    expect(caminhos).toContain('/api/motorista/autenticacao/sair');
    expect(caminhos).not.toContain('/api/motorista/autenticacao/renovar');
  });
});
