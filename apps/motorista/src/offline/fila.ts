import type { MotivoDeTentativa, TipoDeOperacao } from '../infra/api';
import { uuidv7 } from '../infra/uuidv7';
import { apagarVarios, gravar, gravarVarios, listarDoUsuario, obter } from './bancoLocal';

/**
 * Estados de uma ação guardada no aparelho (CLAUDE.md, seção 30):
 * - Pending: guardada, esperando envio;
 * - Uploading: num envio em curso — se o aparelho desligar neste meio, volta a ser enviada;
 * - Synced: aplicada pelo servidor (inclusive quando a repetição encontrou a primeira);
 * - Conflict: a situação mudou enquanto a ação esperava, e ela não vale mais;
 * - Failed: recusada por regra. Nem conflito nem falha são reenviados: repetir não muda o desfecho.
 */
export type StatusDaOperacao = 'Pending' | 'Uploading' | 'Synced' | 'Conflict' | 'Failed';

export interface OperacaoLocal {
  /** ClientOperationId: nasce no aparelho, antes de qualquer envio, e é o mesmo em toda repetição. */
  readonly id: string;
  readonly usuarioId: string;
  readonly tipo: TipoDeOperacao;
  readonly payload: { readonly alvoId: string; readonly motivo: MotivoDeTentativa | null };
  /** Frase para o motorista reconhecer a ação ("Conclusão da entrega de Carla Nunes"). */
  readonly descricao: string;
  readonly criadaEm: string;
  /** Ordem em que o motorista fez as ações: saída antes da chegada, chegada antes da conclusão. */
  readonly ordem: number;
  readonly status: StatusDaOperacao;
  readonly tentativas: number;
  readonly ultimaTentativaEm: string | null;
  readonly sincronizadaEm: string | null;
  readonly codigo: string | null;
  readonly mensagem: string | null;
  /** O motorista já viu o conflito ou a recusa. */
  readonly ciente: boolean;
}

export interface PedidoDeOperacao {
  readonly tipo: TipoDeOperacao;
  readonly alvoId: string;
  readonly motivo?: MotivoDeTentativa;
  readonly descricao: string;
}

const RETENCAO_DO_RESOLVIDO_EM_MS = 24 * 60 * 60 * 1000;

const ouvintes = new Set<() => void>();
let ultimaOrdem = 0;

export function assinarFila(ouvinte: () => void): () => void {
  ouvintes.add(ouvinte);
  return () => {
    ouvintes.delete(ouvinte);
  };
}

function avisar(): void {
  for (const ouvinte of ouvintes) {
    ouvinte();
  }
}

export const aguardandoEnvio = (operacao: OperacaoLocal): boolean =>
  operacao.status === 'Pending' || operacao.status === 'Uploading';

export const precisaDeAtencao = (operacao: OperacaoLocal): boolean =>
  (operacao.status === 'Conflict' || operacao.status === 'Failed') && !operacao.ciente;

function porOrdem(a: OperacaoLocal, b: OperacaoLocal): number {
  return a.ordem - b.ordem || a.criadaEm.localeCompare(b.criadaEm) || a.id.localeCompare(b.id);
}

/** Guarda a ação no aparelho. Só depois de guardada ela vale para a tela. */
export async function enfileirar(usuarioId: string, pedido: PedidoDeOperacao, agora: number = Date.now()): Promise<OperacaoLocal> {
  ultimaOrdem = Math.max(agora, ultimaOrdem + 1);

  const operacao: OperacaoLocal = {
    id: uuidv7(agora),
    usuarioId,
    tipo: pedido.tipo,
    payload: { alvoId: pedido.alvoId, motivo: pedido.motivo ?? null },
    descricao: pedido.descricao,
    criadaEm: new Date(agora).toISOString(),
    ordem: ultimaOrdem,
    status: 'Pending',
    tentativas: 0,
    ultimaTentativaEm: null,
    sincronizadaEm: null,
    codigo: null,
    mensagem: null,
    ciente: false,
  };

  await gravar('operacoes', operacao);
  avisar();
  return operacao;
}

export async function operacoesDoUsuario(usuarioId: string): Promise<OperacaoLocal[]> {
  const operacoes = await listarDoUsuario<OperacaoLocal>('operacoes', usuarioId);
  return operacoes.sort(porOrdem);
}

export async function salvarOperacoes(operacoes: readonly OperacaoLocal[]): Promise<void> {
  if (operacoes.length === 0) {
    return;
  }

  await gravarVarios('operacoes', operacoes);
  avisar();
}

export async function marcarCiente(id: string): Promise<void> {
  const operacao = await obter<OperacaoLocal>('operacoes', id);
  if (!operacao) {
    return;
  }

  await gravar('operacoes', { ...operacao, ciente: true });
  avisar();
}

/** Tira da fila o que está resolvido há mais de um dia. Nada que ainda espera envio sai daqui. */
export async function limparResolvidas(usuarioId: string, agora: number = Date.now()): Promise<void> {
  const limite = agora - RETENCAO_DO_RESOLVIDO_EM_MS;
  const antigas = (await operacoesDoUsuario(usuarioId)).filter((operacao) => {
    if (operacao.status === 'Synced') {
      return operacao.sincronizadaEm !== null && Date.parse(operacao.sincronizadaEm) < limite;
    }

    return operacao.ciente && operacao.ultimaTentativaEm !== null && Date.parse(operacao.ultimaTentativaEm) < limite;
  });

  if (antigas.length > 0) {
    await apagarVarios('operacoes', antigas.map((operacao) => operacao.id));
    avisar();
  }
}
