import { z } from 'zod';

import { ErroDeApi, erroDaResposta, requisicaoAutenticada } from './sessao';

const esquemaDeStatusDaEntrega = z.enum([
  'Criada',
  'Planejada',
  'Atribuida',
  'EmRota',
  'ProximaDoDestino',
  'Entregue',
  'TentativaFrustrada',
  'Reagendada',
  'Cancelada',
]);

const esquemaDeStatusDaRota = z.enum(['EmMontagem', 'Planejada', 'EmAndamento', 'Concluida', 'Cancelada']);

export const MOTIVOS_DE_TENTATIVA = [
  'DestinatarioAusente',
  'EnderecoNaoLocalizado',
  'RecusadaPeloDestinatario',
  'LocalFechado',
  'AcessoImpedido',
  'ProblemaComVeiculo',
  'ProblemaComMercadoria',
  'Outro',
] as const;

/** O único motivo que exige descrição: o vocabulário fechado não diz o que houve. */
export const MOTIVO_QUE_EXIGE_DESCRICAO = 'Outro';

/** Tamanho máximo da descrição, igual ao da API. */
export const TAMANHO_MAXIMO_DA_DESCRICAO = 500;

const esquemaDeMotivo = z.enum(MOTIVOS_DE_TENTATIVA);

const esquemaDeEndereco = z.object({
  logradouro: z.string(),
  numero: z.string(),
  complemento: z.string().nullable(),
  bairro: z.string(),
  cidade: z.string(),
  uf: z.string(),
  cep: z.string(),
});

const esquemaDeJanela = z.object({ de: z.string(), ate: z.string() });

const esquemaDeItemDeRota = z.object({
  id: z.string(),
  codigo: z.string(),
  data: z.string(),
  status: esquemaDeStatusDaRota,
  saidaPlanejada: z.string().nullable(),
  iniciadaEm: z.string().nullable(),
  totalDeParadas: z.number().int(),
  paradasPendentes: z.number().int(),
});

const esquemaDeParada = z.object({
  sequencia: z.number().int(),
  entregaId: z.string(),
  codigoDaEntrega: z.string(),
  status: esquemaDeStatusDaEntrega,
  destinatarioNome: z.string(),
  endereco: esquemaDeEndereco,
  janelaPrometida: esquemaDeJanela,
});

const esquemaDeRota = z.object({
  id: z.string(),
  codigo: z.string(),
  data: z.string(),
  status: esquemaDeStatusDaRota,
  saidaPlanejada: z.string().nullable(),
  iniciadaEm: z.string().nullable(),
  concluidaEm: z.string().nullable(),
  hubNome: z.string().nullable(),
  veiculo: z.object({ placa: z.string(), identificacao: z.string() }).nullable(),
  paradas: z.array(esquemaDeParada),
});

const esquemaDeEntrega = z.object({
  id: z.string(),
  codigo: z.string(),
  status: esquemaDeStatusDaEntrega,
  rotaId: z.string().nullable(),
  sequencia: z.number().int().nullable(),
  destinatario: z.object({
    nome: z.string(),
    telefone: z.string().nullable(),
    instrucoesDeEntrega: z.string().nullable(),
  }),
  endereco: esquemaDeEndereco,
  localizacao: z.object({ latitude: z.number(), longitude: z.number() }).nullable(),
  janelaPrometida: esquemaDeJanela,
  observacoes: z.string().nullable(),
  execucao: z.object({
    saiuParaRotaEm: z.string().nullable(),
    chegadaRegistradaEm: z.string().nullable(),
    entregueEm: z.string().nullable(),
    tentativasFrustradas: z.number().int(),
    motivoDaUltimaTentativa: esquemaDeMotivo.nullable(),
    ultimaTentativaFrustradaEm: z.string().nullable(),
  }),
});

const esquemaDoLote = z.object({
  recebidas: z.number().int(),
  aceitas: z.number().int(),
  duplicadas: z.number().int(),
  rejeitadas: z.number().int(),
});

/** Ações do motorista que passam pela fila do aparelho. */
export const TIPOS_DE_OPERACAO = ['IniciarRota', 'RegistrarChegada', 'ConcluirEntrega', 'RegistrarTentativaFrustrada', 'ConcluirRota'] as const;

const esquemaDoResultadoDeOperacao = z.object({
  operacaoDoClienteId: z.string().nullish(),
  desfecho: z.enum(['Aplicada', 'Conflito', 'Recusada', 'TentarDeNovo']),
  repetida: z.boolean(),
  codigo: z.string().nullish(),
  mensagem: z.string().nullish(),
});

const esquemaDaSincronizacao = z.object({ resultados: z.array(esquemaDoResultadoDeOperacao) });

export type TipoDeOperacao = (typeof TIPOS_DE_OPERACAO)[number];
export type ResultadoDeOperacao = z.infer<typeof esquemaDoResultadoDeOperacao>;
export type ResultadoDaSincronizacao = z.infer<typeof esquemaDaSincronizacao>;

/** Operação como a API de sincronização recebe. O identificador nasce no aparelho. */
export interface OperacaoParaEnvio {
  readonly operacaoDoClienteId: string;
  readonly tipo: TipoDeOperacao;
  readonly alvoId: string;
  readonly motivo: MotivoDeTentativa | null;
  readonly observacao: string | null;
  readonly criadaEm: string;
}

export type StatusDaEntrega = z.infer<typeof esquemaDeStatusDaEntrega>;
export type StatusDaRota = z.infer<typeof esquemaDeStatusDaRota>;
export type MotivoDeTentativa = z.infer<typeof esquemaDeMotivo>;
export type Endereco = z.infer<typeof esquemaDeEndereco>;
export type ItemDeRota = z.infer<typeof esquemaDeItemDeRota>;
export type Parada = z.infer<typeof esquemaDeParada>;
export type Rota = z.infer<typeof esquemaDeRota>;
export type Entrega = z.infer<typeof esquemaDeEntrega>;
export type ResultadoDoLote = z.infer<typeof esquemaDoLote>;

/** Posição como a API recebe. O identificador e a sequência nascem no aparelho. */
export interface PosicaoParaEnvio {
  readonly eventoDeLocalizacaoId: string;
  readonly latitude: number;
  readonly longitude: number;
  readonly precisaoEmMetros: number;
  readonly capturadaEm: string;
  readonly sequencia: number;
  readonly velocidadeEmMetrosPorSegundo: number | null;
  readonly direcaoEmGraus: number | null;
}

/** Resposta fora do formato combinado: tratada como erro da operação, nunca como dado. */
export class RespostaInesperada extends Error {
  constructor() {
    super('A operação respondeu num formato inesperado.');
    this.name = 'RespostaInesperada';
  }
}

async function ler<T>(caminho: string, esquema: z.ZodType<T>, opcoes?: RequestInit): Promise<T> {
  const resposta = await requisicaoAutenticada(caminho, opcoes);
  if (!resposta.ok) {
    throw await erroDaResposta(resposta);
  }

  const resultado = esquema.safeParse(await resposta.json());
  if (!resultado.success) {
    throw new RespostaInesperada();
  }

  return resultado.data;
}

export const apiDoMotorista = {
  listarRotas: () => ler('/api/motorista/rotas', z.array(esquemaDeItemDeRota)),
  obterRota: (rotaId: string) => ler(`/api/motorista/rotas/${encodeURIComponent(rotaId)}`, esquemaDeRota),
  obterEntrega: (entregaId: string) => ler(`/api/motorista/entregas/${encodeURIComponent(entregaId)}`, esquemaDeEntrega),

  // Toda ação crítica sai por aqui, com o identificador do aparelho: nenhuma depende de o POST não repetir.
  // Os comandos diretos da API continuam existindo para outros clientes, mas a PWA não os usa.
  sincronizar: (operacoes: readonly OperacaoParaEnvio[]) =>
    ler('/api/motorista/sincronizacao', esquemaDaSincronizacao, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ operacoes }),
    }),

  enviarPosicoes: (posicoes: readonly PosicaoParaEnvio[]) =>
    ler('/api/motorista/posicoes', esquemaDoLote, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ posicoes }),
    }),
};

export type ApiDoMotorista = typeof apiDoMotorista;

/**
 * Mensagem para o motorista, pela situação — curta, sem jargão e dizendo o que fazer.
 */
export function mensagemDeErro(erro: unknown): string {
  if (erro instanceof RespostaInesperada) {
    return 'A operação respondeu algo inesperado. Tente de novo em instantes.';
  }

  if (!(erro instanceof ErroDeApi)) {
    return 'Sem conexão com a operação. Verifique a internet e tente de novo.';
  }

  switch (erro.codigo) {
    case 'entrega_reatribuida':
      return 'Esta entrega foi passada para outro motorista. Fale com a operação se tiver dúvida.';
    case 'motorista_nao_associado':
      return 'Sua conta ainda não está ligada a um motorista. Fale com a operação.';
    case 'conflito_de_versao':
      return 'A entrega foi alterada ao mesmo tempo. A tela foi atualizada; confira e tente de novo.';
    case 'rota_com_entregas_pendentes':
      return 'Ainda há entregas sem resultado nesta rota.';
    default:
      break;
  }

  if (erro.status === 401) {
    return 'Sua sessão terminou. Entre de novo.';
  }

  if (erro.status === 404) {
    return 'Não encontramos este item na sua rota.';
  }

  if (erro.status === 409) {
    return 'Esta ação não vale mais para a situação atual. A tela foi atualizada.';
  }

  if (erro.status === 429) {
    return 'Muitas tentativas seguidas. Aguarde um instante e tente de novo.';
  }

  return 'A operação está com problema agora. Tente de novo em instantes.';
}
