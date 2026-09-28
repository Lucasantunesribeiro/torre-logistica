import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';

type Rota = (opcoes?: RequestInit) => Response;

/**
 * MapLibre desenha por WebGL, que o jsdom não tem, e o cliente de tempo real abre WebSocket.
 * Os dois são trocados por duplos: o que esta suíte prova é a tela — quais dados aparecem, o que o
 * operador consegue fazer —, não o desenho do mapa nem o transporte do SignalR.
 */
vi.mock('maplibre-gl', () => ({
  Map: class {
    on = (evento: string, retorno: () => void) => {
      if (evento === 'load') {
        retorno();
      }
    };
    getSource = () => undefined;
    addSource = () => undefined;
    addLayer = () => undefined;
    flyTo = () => undefined;
    remove = () => undefined;
  },
}));

vi.mock('maplibre-gl/dist/maplibre-gl.css', () => ({}));

vi.mock('@microsoft/signalr', () => {
  class Construtor {
    withUrl = () => this;
    withAutomaticReconnect = () => this;
    configureLogging = () => this;
    build = () => ({
      on: () => undefined,
      onreconnecting: () => undefined,
      onreconnected: () => undefined,
      onclose: () => undefined,
      start: () => Promise.resolve(),
      stop: () => Promise.resolve(),
      state: 'Disconnected',
    });
  }

  return { HubConnectionBuilder: Construtor, HubConnectionState: { Disconnected: 'Disconnected' }, LogLevel: { Warning: 3 } };
});

const SESSAO = {
  tokenDeAcesso: 'TOKEN',
  expiraEm: '2026-09-20T12:15:00Z',
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

const ENTREGA = {
  id: 'e1',
  codigo: 'ENT-2026-000042',
  status: 'EmRota',
  clienteNome: 'Distribuidora Vale',
  destinatarioNome: 'Carla Nunes',
  endereco: {
    logradouro: 'Rua das Flores',
    numero: '100',
    complemento: null,
    bairro: 'Cambuí',
    cidade: 'Campinas',
    uf: 'SP',
    cep: '13010000',
  },
  localizacao: { latitude: -22.9, longitude: -47.06 },
  janelaPrometida: { de: '2026-09-21T12:00:00Z', ate: '2026-09-21T15:00:00Z' },
  observacoes: null,
  execucao: {
    motoristaId: 'm1',
    saiuParaRotaEm: '2026-09-21T11:00:00Z',
    chegadaRegistradaEm: null,
    entregueEm: null,
    tentativasFrustradas: 0,
  },
  criadaEm: '2026-09-20T09:00:00Z',
};

const ALERTA = {
  id: 'a1',
  tipo: 'RiscoDeAtraso',
  severidade: 'Critica',
  estado: 'Aberto',
  descricao: 'Entrega com risco de atraso',
  entregaId: 'e1',
  codigoDaEntrega: 'ENT-2026-000042',
  motoristaId: 'm1',
  nomeDoMotorista: 'Rafael Lima',
  abertoEm: '2026-09-21T11:30:00Z',
};

const OCORRENCIA = {
  id: 'oc1',
  entregaId: 'e1',
  codigoDaEntrega: 'ENT-2026-000042',
  nomeDoMotorista: 'Rafael Lima',
  tipo: 'ProblemaComVeiculo',
  severidade: 'Media',
  observacao: 'Pneu furado',
  ocorridaEm: '2026-09-21T11:20:00Z',
};

function json(corpo: unknown, status = 200): Response {
  return new Response(JSON.stringify(corpo), { status, headers: { 'Content-Type': 'application/json' } });
}

function pagina(itens: readonly unknown[]): Response {
  return json({ itens, pagina: 1, tamanhoDaPagina: 25, total: itens.length });
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
const comSessao: Rota = () => json(SESSAO);

/** Rotas que o painel abre ao carregar. Sem elas, cada teste veria erro em vez da tela. */
const painel: Record<string, Rota> = {
  '/health/ready': prontidao,
  '/api/autenticacao/renovar': comSessao,
  '/api/entregas': () => pagina([ENTREGA]),
  '/api/alertas': () => pagina([ALERTA]),
  '/api/ocorrencias': () => pagina([OCORRENCIA]),
};

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

describe('Sessão do console', () => {
  it('sem sessão válida leva ao login', async () => {
    servidor({ '/health/ready': prontidao, '/api/autenticacao/renovar': semSessao });

    await montar('/');

    expect(await screen.findByRole('heading', { level: 2, name: 'Entrar no console' })).toBeVisible();
  });

  it('recupera a sessão pelo cookie ao abrir, sem pedir senha', async () => {
    servidor(painel);

    await montar('/');

    expect(await screen.findByText('Paula Siqueira')).toBeVisible();
    expect(screen.getByText('Operador · Transportadora Aurora')).toBeVisible();
  });

  it('login bem-sucedido abre o console', async () => {
    const chamadas = servidor({ ...painel, '/api/autenticacao/renovar': semSessao, '/api/autenticacao/login': comSessao });
    await montar('/');
    await screen.findByLabelText('Organização');

    preencherLogin();

    expect(await screen.findByRole('heading', { level: 2, name: 'Painel operacional' })).toBeVisible();
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
    const chamadas = servidor({ ...painel, '/api/autenticacao/sair': () => new Response(null, { status: 204 }) });
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
});

describe('Painel operacional', () => {
  it('mostra contadores e a fila do que precisa de gente', async () => {
    servidor(painel);

    await montar('/');

    expect(await screen.findByRole('heading', { level: 2, name: 'Painel operacional' })).toBeVisible();
    expect(screen.getByText('a caminho')).toBeVisible();
    expect(await screen.findByText(/Entrega com risco de atraso/)).toBeVisible();
    expect(screen.getByText(/ProblemaComVeiculo/)).toBeVisible();
  });

  it('diz que está limpo quando não há alerta aberto', async () => {
    servidor({ ...painel, '/api/alertas': () => pagina([]) });

    await montar('/');

    expect(await screen.findByText('Nada aberto. A operação está limpa.')).toBeVisible();
  });
});

describe('Entregas', () => {
  it('lista entregas e abre o detalhe pelo código', async () => {
    servidor({
      ...painel,
      '/api/entregas/e1': () => json(ENTREGA),
      '/api/entregas/e1/eventos': () =>
        json([{ sequencia: 1, tipo: 'Criada', statusResultante: 'Criada', ocorridoEm: '2026-09-20T09:00:00Z' }]),
      '/api/entregas/e1/previsao': () =>
        json({
          disponivel: true,
          ativa: true,
          situacao: 'Risco',
          chegadaPrevistaEm: '2026-09-21T15:40:00Z',
          folgaEmSegundos: -2400,
          explicacao: 'Chegada prevista depois do fim da janela.',
        }),
      '/api/entregas/e1/ocorrencias': () => json([OCORRENCIA]),
      '/api/entregas/e1/comprovante': () => json({ codigo: 'comprovante_nao_encontrado' }, 404),
    });

    await montar('/entregas');

    fireEvent.click(await screen.findByRole('link', { name: 'ENT-2026-000042' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'ENT-2026-000042' })).toBeVisible();
    expect(await screen.findByText('Chegada prevista depois do fim da janela.')).toBeVisible();
    expect(await screen.findByText('Esta entrega ainda não tem comprovante.')).toBeVisible();
    expect(await screen.findByText(/Pneu furado/)).toBeVisible();
  });

  it('emite o link de rastreamento e mostra o valor uma vez', async () => {
    servidor({
      ...painel,
      '/api/entregas/e1': () => json(ENTREGA),
      '/api/entregas/e1/eventos': () => json([]),
      '/api/entregas/e1/previsao': () =>
        json({ disponivel: false, ativa: false, situacao: null, chegadaPrevistaEm: null, folgaEmSegundos: null, explicacao: null }),
      '/api/entregas/e1/ocorrencias': () => json([]),
      '/api/entregas/e1/comprovante': () => json({ codigo: 'comprovante_nao_encontrado' }, 404),
      '/api/entregas/e1/link-de-rastreamento': () => json({ token: 'TOKEN-PUBLICO', expiraEm: '2026-10-20T12:00:00Z' }),
    });

    await montar('/entregas/e1');

    fireEvent.click(await screen.findByRole('button', { name: 'Emitir link de rastreamento' }));

    expect(await screen.findByText(/TOKEN-PUBLICO/)).toBeVisible();
  });
});

describe('Alertas', () => {
  it('resolve um alerta com observação', async () => {
    const chamadas = servidor({
      ...painel,
      '/api/alertas/a1/resolucao': () => json({}),
    });

    await montar('/alertas');

    fireEvent.click(await screen.findByRole('button', { name: 'Resolver' }));
    fireEvent.change(screen.getByLabelText('O que foi feito'), { target: { value: 'Falei com o motorista.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar' }));

    const resolucao = await vi.waitFor(() => {
      const chamada = chamadas.mock.calls.find(([entrada]) => urlDe(entrada).endsWith('/api/alertas/a1/resolucao'));
      expect(chamada).toBeDefined();
      return chamada;
    });

    expect(JSON.parse(resolucao?.[1]?.body as string)).toEqual({ observacao: 'Falei com o motorista.' });
  });
});

describe('Mapa da operação', () => {
  it('mostra as entregas a caminho e avisa quando não há provedor de mapa', async () => {
    servidor({ ...painel, '/api/motoristas/m1/posicao-atual': () => json({ codigo: 'posicao_nao_encontrada' }, 404) });

    await montar('/mapa');

    expect(await screen.findByRole('heading', { level: 2, name: 'Mapa da operação' })).toBeVisible();
    expect(await screen.findByRole('button', { name: 'ENT-2026-000042' })).toBeVisible();
    expect(screen.getByText(/Sem provedor de mapa configurado/)).toBeVisible();
    expect(screen.getByRole('application', { name: 'Mapa da operação' })).toBeVisible();
  });
});

describe('Webhooks', () => {
  it('reenvia uma entrega que desistiu', async () => {
    const chamadas = servidor({
      ...painel,
      '/api/webhooks/assinaturas': () =>
        pagina([
          {
            id: 'w1',
            nome: 'ERP do cliente',
            url: 'https://erp.exemplo.test/hooks',
            eventos: [],
            ativa: true,
            criadaEm: '2026-09-20T09:00:00Z',
          },
        ]),
      '/api/webhooks/entregas': () =>
        pagina([
          {
            id: 'we1',
            assinaturaId: 'w1',
            tipo: 'delivery.completed',
            url: 'https://erp.exemplo.test/hooks',
            estado: 'Falhada',
            tentativas: 6,
            criadaEm: '2026-09-20T10:00:00Z',
            ultimoStatus: 500,
            ultimoErro: 'O assinante respondeu 500.',
          },
        ]),
      '/api/webhooks/entregas/we1/reenvio': () =>
        json({
          id: 'we1',
          assinaturaId: 'w1',
          tipo: 'delivery.completed',
          url: 'https://erp.exemplo.test/hooks',
          estado: 'Pendente',
          tentativas: 0,
          criadaEm: '2026-09-20T10:00:00Z',
          ultimoStatus: null,
          ultimoErro: null,
        }),
    });

    await montar('/webhooks');

    fireEvent.click(await screen.findByRole('button', { name: 'Reenviar' }));

    await vi.waitFor(() => {
      expect(chamadas.mock.calls.some(([entrada]) => urlDe(entrada).endsWith('/api/webhooks/entregas/we1/reenvio'))).toBe(
        true,
      );
    });
  });
});

describe('Indicadores', () => {
  const INDICADORES = {
    de: '2026-08-21T12:00:00Z',
    ate: '2026-09-20T12:00:00Z',
    entregasConcluidas: 5,
    entregasCanceladas: 1,
    pontualidadeEmPercentual: { valor: 60, base: 5, definicao: 'Entregas concluídas dentro da janela prometida.' },
    sucessoNaPrimeiraTentativaEmPercentual: { valor: 80, base: 5, definicao: 'Concluídas sem tentativa frustrada.' },
    atrasoMedioEmMinutos: { valor: 75, base: 2, definicao: 'Média de minutos além da janela.' },
    tempoMedioPorParadaEmMinutos: { valor: null, base: 0, definicao: 'Da chegada à conclusão.' },
    tempoMedioEmRotaEmMinutos: { valor: 178, base: 5, definicao: 'Da saída para rota à conclusão.' },
    entregasPorMotorista: [{ rotulo: 'Rafael Nunes', quantidade: 5, valor: 60 }],
    ocorrenciasPorMotivo: [{ rotulo: 'TentativaDeEntrega', quantidade: 2, valor: null }],
    pontualidadePorCliente: [{ rotulo: 'Mercado Aurora', quantidade: 5, valor: 60 }],
    entregasPorRota: [{ rotulo: 'ROT-2026-0001', quantidade: 4, valor: 50 }],
  };

  it('mostra o número, a base e a definição de cada indicador', async () => {
    servidor({ ...painel, '/api/indicadores': () => json(INDICADORES) });

    await montar('/indicadores');

    expect(await screen.findByText('A operação está cumprindo a janela que prometeu?')).toBeVisible();
    expect(screen.getByText('60')).toBeVisible();
    expect(screen.getByText('Entregas concluídas dentro da janela prometida.')).toBeVisible();
    // Três indicadores têm as mesmas cinco entregas na base; o atraso médio tem só duas, e diz isso.
    expect(screen.getAllByText('5 entrega(s) na base do cálculo', { selector: '.indicador__base' })).toHaveLength(3);
    expect(screen.getByText('2 entrega(s) na base do cálculo', { selector: '.indicador__base' })).toBeVisible();
    expect(screen.getByText(/5 entrega\(s\) concluída\(s\) no período/)).toBeVisible();
    expect(screen.getByText(/1 cancelada\(s\), fora das contas/)).toBeVisible();
  });

  it('sem base, diz que não há dados em vez de mostrar zero', async () => {
    servidor({ ...painel, '/api/indicadores': () => json(INDICADORES) });

    await montar('/indicadores');

    expect(await screen.findByText('sem dados no período')).toBeVisible();
    expect(screen.getByText('Quanto tempo o motorista fica parado em cada destino?')).toBeVisible();
  });

  it('cada recorte aparece com a pergunta que ele responde', async () => {
    servidor({ ...painel, '/api/indicadores': () => json(INDICADORES) });

    await montar('/indicadores');

    expect(await screen.findByText('Quem está carregando a operação, e com que pontualidade?')).toBeVisible();
    expect(screen.getByRole('rowheader', { name: 'Rafael Nunes' })).toBeVisible();
    expect(screen.getByRole('rowheader', { name: 'Mercado Aurora' })).toBeVisible();
    expect(screen.getByRole('rowheader', { name: 'ROT-2026-0001' })).toBeVisible();
    expect(screen.getByRole('rowheader', { name: 'TentativaDeEntrega' })).toBeVisible();
  });

  it('trocar o período refaz a consulta', async () => {
    const chamadas = servidor({ ...painel, '/api/indicadores': () => json(INDICADORES) });

    await montar('/indicadores');
    await screen.findByText('A operação está cumprindo a janela que prometeu?');

    fireEvent.change(screen.getByLabelText('Período'), { target: { value: '7' } });

    await vi.waitFor(() => {
      const periodos = chamadas.mock.calls
        .map(([entrada]) => urlDe(entrada))
        .filter((url) => url.includes('/api/indicadores'))
        .map((url) => new URL(url).searchParams.get('de'));

      expect(new Set(periodos).size).toBe(2);
    });
  });
});

describe('Entrada da demonstração', () => {
  const OFERTA = { habilitada: true, convite: 'Entre como operador numa transportadora fictícia.' };

  it('sem demonstração no ambiente, o botão não aparece', async () => {
    servidor({
      '/health/ready': prontidao,
      '/api/autenticacao/renovar': semSessao,
      '/api/demonstracao': () => json({ habilitada: false, convite: null }),
    });

    await montar('/');
    await screen.findByRole('heading', { level: 2, name: 'Entrar no console' });

    expect(screen.queryByRole('button', { name: 'Explorar demonstração' })).toBeNull();
  });

  it('com demonstração, o botão aparece com o convite e a ressalva', async () => {
    servidor({
      '/health/ready': prontidao,
      '/api/autenticacao/renovar': semSessao,
      '/api/demonstracao': () => json(OFERTA),
    });

    await montar('/');

    expect(await screen.findByRole('button', { name: 'Explorar demonstração' })).toBeVisible();
    expect(screen.getByText(OFERTA.convite)).toBeVisible();
    expect(screen.getByText(/não administra a organização/)).toBeVisible();
  });

  it('o botão abre a sessão e leva ao console, sem pedir senha', async () => {
    const chamadas = servidor({
      ...painel,
      '/api/autenticacao/renovar': semSessao,
      '/api/demonstracao': () => json(OFERTA),
      '/api/demonstracao/sessao': () => json(SESSAO),
    });

    await montar('/');
    fireEvent.click(await screen.findByRole('button', { name: 'Explorar demonstração' }));

    expect(await screen.findByText('Paula Siqueira')).toBeVisible();
    expect(
      chamadas.mock.calls.some(([entrada]) => urlDe(entrada).endsWith('/api/demonstracao/sessao')),
    ).toBe(true);
  });

  it('falha na demonstração vira mensagem, não tela quebrada', async () => {
    servidor({
      '/health/ready': prontidao,
      '/api/autenticacao/renovar': semSessao,
      '/api/demonstracao': () => json(OFERTA),
      '/api/demonstracao/sessao': () => json({ codigo: 'nao_encontrado' }, 404),
    });

    await montar('/');
    fireEvent.click(await screen.findByRole('button', { name: 'Explorar demonstração' }));

    expect(await screen.findByRole('alert')).toBeVisible();
    // A tela de login continua de pé para quem tem credencial.
    expect(screen.getByRole('button', { name: 'Entrar' })).toBeVisible();
  });
});
