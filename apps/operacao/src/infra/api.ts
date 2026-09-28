import { z } from 'zod';

import { requisicaoAutenticada } from './sessao';

/**
 * Leitura e comandos do console, sobre a sessão autenticada.
 *
 * Os esquemas descrevem só os campos que as telas usam: validar o que não se lê não protege nada e
 * transformaria cada campo novo da API numa quebra do console. Campo que falta, porém, vira erro aqui e
 * não `undefined` três componentes adiante.
 */

const paginaDe = <T extends z.ZodTypeAny>(item: T) =>
  z.object({
    itens: z.array(item),
    pagina: z.number().int(),
    tamanhoDaPagina: z.number().int(),
    total: z.number().int(),
  });

export const STATUS_DA_ENTREGA = [
  'Criada',
  'Planejada',
  'Atribuida',
  'EmRota',
  'ProximaDoDestino',
  'Entregue',
  'TentativaFrustrada',
  'Reagendada',
  'Cancelada',
] as const;

const esquemaDeStatus = z.enum(STATUS_DA_ENTREGA);
const esquemaDeSituacao = z.enum(['Normal', 'Atencao', 'Risco', 'Atrasada']);
const esquemaDeCoordenada = z.object({ latitude: z.number(), longitude: z.number() });

const esquemaDeEndereco = z.object({
  logradouro: z.string(),
  numero: z.string(),
  complemento: z.string().nullable(),
  bairro: z.string(),
  cidade: z.string(),
  uf: z.string(),
  cep: z.string(),
});

const esquemaDeEntrega = z.object({
  id: z.string(),
  codigo: z.string(),
  status: esquemaDeStatus,
  clienteNome: z.string(),
  destinatarioNome: z.string(),
  endereco: esquemaDeEndereco,
  localizacao: esquemaDeCoordenada.nullable(),
  janelaPrometida: z.object({ de: z.string(), ate: z.string() }),
  observacoes: z.string().nullable(),
  execucao: z.object({
    motoristaId: z.string().nullable(),
    saiuParaRotaEm: z.string().nullable(),
    chegadaRegistradaEm: z.string().nullable(),
    entregueEm: z.string().nullable(),
    tentativasFrustradas: z.number().int(),
  }),
  criadaEm: z.string(),
});

const esquemaDeEvento = z.object({
  sequencia: z.number().int(),
  tipo: z.string(),
  statusResultante: esquemaDeStatus,
  ocorridoEm: z.string(),
});

const esquemaDePrevisao = z.object({
  disponivel: z.boolean(),
  ativa: z.boolean(),
  situacao: esquemaDeSituacao.nullable(),
  chegadaPrevistaEm: z.string().nullable(),
  folgaEmSegundos: z.number().int().nullable(),
  explicacao: z.string().nullable(),
});

const esquemaDeComprovante = z.object({
  id: z.string(),
  recebidoPor: z.string(),
  observacao: z.string().nullable(),
  registradoEm: z.string(),
  arquivos: z.array(z.object({ tipo: z.string(), url: z.string(), urlExpiraEm: z.string() })),
});

const esquemaDeMotorista = z.object({
  id: z.string(),
  nome: z.string(),
  telefone: z.string().nullable(),
  ativo: z.boolean(),
});

const esquemaDePosicao = z.object({
  latitude: z.number(),
  longitude: z.number(),
  precisaoEmMetros: z.number(),
  capturadaEm: z.string(),
  recebidaEm: z.string(),
});

const esquemaDeParada = z.object({
  sequencia: z.number().int(),
  entregaId: z.string(),
  codigoDaEntrega: z.string(),
  status: esquemaDeStatus,
});

const esquemaDeStatusDaRota = z.enum(['EmMontagem', 'Planejada', 'EmAndamento', 'Concluida', 'Cancelada']);

/**
 * Rota na LISTAGEM: o backend projeta só o que a linha da tabela precisa — sem carregar o motorista
 * nem as paradas, que são consulta a mais. Por isso `motoristaId` e a contagem, não o objeto e o array.
 */
const esquemaDeRotaResumo = z.object({
  id: z.string(),
  codigo: z.string(),
  data: z.string(),
  status: esquemaDeStatusDaRota,
  hubId: z.string().nullable(),
  motoristaId: z.string().nullable(),
  veiculoId: z.string().nullable(),
  saidaPlanejada: z.string().nullable(),
  quantidadeDeParadas: z.number().int(),
  versao: z.number().int(),
});

/**
 * Rota no DETALHE: a forma completa, com o motorista e o veículo resolvidos e a lista de paradas.
 * Contrato genuinamente diferente do resumo — daí dois esquemas, não campos opcionais no mesmo.
 */
const esquemaDeRotaDetalhe = z.object({
  id: z.string(),
  codigo: z.string(),
  data: z.string(),
  status: esquemaDeStatusDaRota,
  hub: z.object({ id: z.string(), nome: z.string() }).nullable(),
  motorista: z.object({ id: z.string(), nome: z.string() }).nullable(),
  veiculo: z.object({ id: z.string(), placa: z.string(), identificacao: z.string() }).nullable(),
  saidaPlanejada: z.string().nullable(),
  paradas: z.array(esquemaDeParada),
  criadaEm: z.string(),
  planejadaEm: z.string().nullable(),
  iniciadaEm: z.string().nullable(),
  concluidaEm: z.string().nullable(),
  canceladaEm: z.string().nullable(),
  versao: z.number().int(),
});

/**
 * Severidade operacional — a mesma escala do domínio (`SeveridadeDoAlerta`/`SeveridadeDaOcorrencia`).
 * Um único enum aqui evita que alerta e ocorrência divirjam entre si ou do backend.
 */
const esquemaDeSeveridade = z.enum(['Baixa', 'Media', 'Alta', 'Critica']);

const esquemaDeAlerta = z.object({
  id: z.string(),
  tipo: z.string(),
  severidade: esquemaDeSeveridade,
  estado: z.enum(['Aberto', 'Resolvido']),
  descricao: z.string(),
  entregaId: z.string().nullable(),
  codigoDaEntrega: z.string().nullable(),
  motoristaId: z.string().nullable(),
  nomeDoMotorista: z.string().nullable(),
  abertoEm: z.string(),
});

const esquemaDeOcorrencia = z.object({
  id: z.string(),
  entregaId: z.string(),
  codigoDaEntrega: z.string(),
  nomeDoMotorista: z.string().nullable(),
  tipo: z.string(),
  severidade: esquemaDeSeveridade,
  observacao: z.string().nullable(),
  ocorridaEm: z.string(),
});

const esquemaDeIntegracao = z.object({
  id: z.string(),
  nome: z.string(),
  identificadorPublico: z.string(),
  ativa: z.boolean(),
  criadaEm: z.string(),
  ultimoUsoEm: z.string().nullable(),
});

const esquemaDeAssinaturaDeWebhook = z.object({
  id: z.string(),
  nome: z.string(),
  url: z.string(),
  eventos: z.array(z.string()),
  ativa: z.boolean(),
  criadaEm: z.string(),
});

const esquemaDeEntregaDeWebhook = z.object({
  id: z.string(),
  assinaturaId: z.string(),
  tipo: z.string(),
  url: z.string(),
  estado: z.enum(['Pendente', 'Entregue', 'Falhada']),
  tentativas: z.number().int(),
  criadaEm: z.string(),
  ultimoStatus: z.number().int().nullable(),
  ultimoErro: z.string().nullable(),
});

export type StatusDaEntrega = z.infer<typeof esquemaDeStatus>;
export type SituacaoDoSla = z.infer<typeof esquemaDeSituacao>;
export type Entrega = z.infer<typeof esquemaDeEntrega>;
export type EventoDaEntrega = z.infer<typeof esquemaDeEvento>;
export type Previsao = z.infer<typeof esquemaDePrevisao>;
export type Comprovante = z.infer<typeof esquemaDeComprovante>;
export type Motorista = z.infer<typeof esquemaDeMotorista>;
export type Posicao = z.infer<typeof esquemaDePosicao>;
export type RotaResumo = z.infer<typeof esquemaDeRotaResumo>;
export type RotaDetalhe = z.infer<typeof esquemaDeRotaDetalhe>;
export type Severidade = z.infer<typeof esquemaDeSeveridade>;
export type Alerta = z.infer<typeof esquemaDeAlerta>;
export type Ocorrencia = z.infer<typeof esquemaDeOcorrencia>;
export type Integracao = z.infer<typeof esquemaDeIntegracao>;
export type AssinaturaDeWebhook = z.infer<typeof esquemaDeAssinaturaDeWebhook>;
export type EntregaDeWebhook = z.infer<typeof esquemaDeEntregaDeWebhook>;
export interface Pagina<T> {
  readonly itens: readonly T[];
  readonly pagina: number;
  readonly tamanhoDaPagina: number;
  readonly total: number;
}

/**
 * Esquemas de leitura expostos para o teste de contrato validar contra a API real.
 *
 * O drift que quebrou o console em produção (enum de severidade, forma de Rota) passou pelos testes
 * porque os mocks repetiam o contrato errado do próprio frontend. Este mapa deixa um teste separado
 * (`contrato.test.ts`) conferir cada esquema contra o backend de verdade — a única fonte que não
 * mente sobre o contrato.
 */
export const esquemasDeContrato = {
  '/api/entregas': paginaDe(esquemaDeEntrega),
  '/api/rotas': paginaDe(esquemaDeRotaResumo),
  '/api/motoristas': paginaDe(esquemaDeMotorista),
  '/api/alertas?estado=Aberto': paginaDe(esquemaDeAlerta),
  '/api/ocorrencias': paginaDe(esquemaDeOcorrencia),
} as const;

/** Falha que a tela sabe mostrar: traz o código de erro da API quando há um. */
export class ErroDaApi extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly codigo: string | null,
  ) {
    super(message);
    this.name = 'ErroDaApi';
  }
}

async function chamar<T extends z.ZodTypeAny>(
  caminho: string,
  esquema: T,
  opcoes: RequestInit = {},
): Promise<z.infer<T>> {
  const resposta = await requisicaoAutenticada(caminho, opcoes);

  if (!resposta.ok) {
    throw await erroDe(resposta);
  }

  const analisado = esquema.safeParse(await resposta.json());

  if (!analisado.success) {
    // Contrato quebrado é defeito nosso, não do usuário: falha alto em vez de renderizar vazio.
    throw new ErroDaApi(`Resposta inesperada de ${caminho}.`, resposta.status, 'contrato_inesperado');
  }

  return analisado.data;
}

async function erroDe(resposta: Response): Promise<ErroDaApi> {
  try {
    const corpo = (await resposta.json()) as { detail?: string; codigo?: string };
    return new ErroDaApi(corpo.detail ?? 'Falha na operação.', resposta.status, corpo.codigo ?? null);
  } catch {
    return new ErroDaApi('Falha na operação.', resposta.status, null);
  }
}

function comando(corpo?: unknown): RequestInit {
  return {
    method: 'POST',
    ...(corpo === undefined
      ? {}
      : { body: JSON.stringify(corpo), headers: { 'Content-Type': 'application/json' } }),
  };
}

function consulta(parametros: Record<string, string | number | undefined>): string {
  const busca = new URLSearchParams();

  for (const [chave, valor] of Object.entries(parametros)) {
    if (valor !== undefined && valor !== '') {
      busca.set(chave, String(valor));
    }
  }

  const texto = busca.toString();
  return texto === '' ? '' : `?${texto}`;
}

export interface FiltroDeEntregas {
  readonly pagina?: number;
  readonly status?: StatusDaEntrega | '';
  readonly codigo?: string;
  /** Use 1 quando só interessa o total, como nos contadores do painel. */
  readonly tamanhoDaPagina?: number;
}

/** Entregas da organização. */
export const listarEntregas = (filtro: FiltroDeEntregas = {}): Promise<Pagina<Entrega>> =>
  chamar(
    `/api/entregas${consulta({
      pagina: filtro.pagina ?? 1,
      tamanhoDaPagina: filtro.tamanhoDaPagina ?? 25,
      status: filtro.status,
      codigo: filtro.codigo,
    })}`,
    paginaDe(esquemaDeEntrega),
  );

export const obterEntrega = (id: string): Promise<Entrega> => chamar(`/api/entregas/${id}`, esquemaDeEntrega);

export const listarEventosDaEntrega = (id: string): Promise<readonly EventoDaEntrega[]> =>
  chamar(`/api/entregas/${id}/eventos`, z.array(esquemaDeEvento));

export const obterPrevisao = (id: string): Promise<Previsao> => chamar(`/api/entregas/${id}/previsao`, esquemaDePrevisao);

export const obterComprovante = (id: string): Promise<Comprovante> =>
  chamar(`/api/entregas/${id}/comprovante`, esquemaDeComprovante);

export const listarOcorrenciasDaEntrega = (id: string): Promise<readonly Ocorrencia[]> =>
  chamar(`/api/entregas/${id}/ocorrencias`, z.array(esquemaDeOcorrencia));

export const emitirLinkDeRastreamento = (id: string): Promise<{ token: string; expiraEm: string }> =>
  chamar(
    `/api/entregas/${id}/link-de-rastreamento`,
    z.object({ token: z.string(), expiraEm: z.string() }),
    comando(),
  );

export const listarRotas = (pagina = 1): Promise<Pagina<RotaResumo>> =>
  chamar(`/api/rotas${consulta({ pagina, tamanhoDaPagina: 25 })}`, paginaDe(esquemaDeRotaResumo));

export const obterRota = (id: string): Promise<RotaDetalhe> => chamar(`/api/rotas/${id}`, esquemaDeRotaDetalhe);

export const listarMotoristas = (pagina = 1): Promise<Pagina<Motorista>> =>
  chamar(`/api/motoristas${consulta({ pagina, tamanhoDaPagina: 25 })}`, paginaDe(esquemaDeMotorista));

export const obterMotorista = (id: string): Promise<Motorista> => chamar(`/api/motoristas/${id}`, esquemaDeMotorista);

export const obterPosicaoAtual = (motoristaId: string): Promise<Posicao> =>
  chamar(`/api/motoristas/${motoristaId}/posicao-atual`, esquemaDePosicao);

export const listarAlertas = (estado?: 'Aberto' | 'Resolvido', pagina = 1): Promise<Pagina<Alerta>> =>
  chamar(`/api/alertas${consulta({ pagina, tamanhoDaPagina: 25, estado })}`, paginaDe(esquemaDeAlerta));

export const resolverAlerta = (id: string, observacao: string): Promise<unknown> =>
  chamar(`/api/alertas/${id}/resolucao`, z.unknown(), comando({ observacao }));

export const listarOcorrencias = (pagina = 1): Promise<Pagina<Ocorrencia>> =>
  chamar(`/api/ocorrencias${consulta({ pagina, tamanhoDaPagina: 25 })}`, paginaDe(esquemaDeOcorrencia));

export const listarIntegracoes = (pagina = 1): Promise<Pagina<Integracao>> =>
  chamar(`/api/integracoes${consulta({ pagina, tamanhoDaPagina: 25 })}`, paginaDe(esquemaDeIntegracao));

export const criarIntegracao = (nome: string): Promise<{ id: string; nome: string; chave: string }> =>
  chamar(
    '/api/integracoes',
    z.object({ id: z.string(), nome: z.string(), chave: z.string() }),
    comando({ nome }),
  );

export const revogarIntegracao = (id: string): Promise<Integracao> =>
  chamar(`/api/integracoes/${id}/revogacao`, esquemaDeIntegracao, comando());

export const listarAssinaturasDeWebhook = (pagina = 1): Promise<Pagina<AssinaturaDeWebhook>> =>
  chamar(`/api/webhooks/assinaturas${consulta({ pagina, tamanhoDaPagina: 25 })}`, paginaDe(esquemaDeAssinaturaDeWebhook));

export const criarAssinaturaDeWebhook = (
  nome: string,
  url: string,
  eventos: readonly string[],
): Promise<{ id: string; nome: string; url: string; segredo: string }> =>
  chamar(
    '/api/webhooks/assinaturas',
    z.object({ id: z.string(), nome: z.string(), url: z.string(), segredo: z.string() }),
    comando({ nome, url, eventos }),
  );

export const revogarAssinaturaDeWebhook = (id: string): Promise<AssinaturaDeWebhook> =>
  chamar(
    `/api/webhooks/assinaturas/${id}/revogacao`,
    esquemaDeAssinaturaDeWebhook,
    comando(),
  );

export const listarEntregasDeWebhook = (
  estado?: 'Pendente' | 'Entregue' | 'Falhada',
  pagina = 1,
): Promise<Pagina<EntregaDeWebhook>> =>
  chamar(
    `/api/webhooks/entregas${consulta({ pagina, tamanhoDaPagina: 25, estado })}`,
    paginaDe(esquemaDeEntregaDeWebhook),
  );

export const reenviarEntregaDeWebhook = (id: string): Promise<EntregaDeWebhook> =>
  chamar(
    `/api/webhooks/entregas/${id}/reenvio`,
    esquemaDeEntregaDeWebhook,
    comando(),
  );

const esquemaDeIndicador = z.object({
  valor: z.number().nullable(),
  base: z.number().int(),
  definicao: z.string(),
});

const esquemaDeLinhaDoIndicador = z.object({
  rotulo: z.string(),
  quantidade: z.number().int(),
  valor: z.number().nullable(),
});

const esquemaDeIndicadores = z.object({
  de: z.string(),
  ate: z.string(),
  entregasConcluidas: z.number().int(),
  entregasCanceladas: z.number().int(),
  pontualidadeEmPercentual: esquemaDeIndicador,
  sucessoNaPrimeiraTentativaEmPercentual: esquemaDeIndicador,
  atrasoMedioEmMinutos: esquemaDeIndicador,
  tempoMedioPorParadaEmMinutos: esquemaDeIndicador,
  tempoMedioEmRotaEmMinutos: esquemaDeIndicador,
  entregasPorMotorista: z.array(esquemaDeLinhaDoIndicador),
  ocorrenciasPorMotivo: z.array(esquemaDeLinhaDoIndicador),
  pontualidadePorCliente: z.array(esquemaDeLinhaDoIndicador),
  entregasPorRota: z.array(esquemaDeLinhaDoIndicador),
});

export type Indicador = z.infer<typeof esquemaDeIndicador>;
export type LinhaDoIndicador = z.infer<typeof esquemaDeLinhaDoIndicador>;
export type IndicadoresDaOperacao = z.infer<typeof esquemaDeIndicadores>;

export const obterIndicadores = (de: string, ate: string): Promise<IndicadoresDaOperacao> =>
  chamar(`/api/indicadores${consulta({ de, ate })}`, esquemaDeIndicadores);
