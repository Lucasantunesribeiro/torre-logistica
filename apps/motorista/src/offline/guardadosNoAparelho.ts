import type { PosicaoParaEnvio } from '../infra/api';
import { RespostaInesperada } from '../infra/api';
import { ErroDeApi } from '../infra/sessao';
import type { MotoristaAutenticado } from '../infra/sessao';
import { apagar, apagarVarios, gravar, gravarVarios, limpar, listarDoUsuario, obter } from './bancoLocal';

/** Mesmo horizonte da sessão do aplicativo e da idade máxima de uma ação no servidor. */
const VALIDADE_DA_IDENTIDADE_EM_MS = 7 * 24 * 60 * 60 * 1000;

export interface Leitura<T> {
  readonly dados: T;
  /** Quando a leitura saiu do servidor. */
  readonly obtidaEm: number;
  /** Veio da cópia guardada, e não do servidor agora. */
  readonly doAparelho: boolean;
}

interface CopiaGuardada<T> {
  readonly chave: string;
  readonly usuarioId: string;
  readonly dados: T;
  readonly obtidaEm: number;
}

/**
 * Lê do servidor e guarda a cópia; sem rede, ou com o servidor fora, devolve a última cópia deste motorista.
 *
 * Resposta do servidor que diz "isto não é mais seu" (404, 409, 401) não cai para a cópia: mostrar a entrega
 * guardada de quem não a tem mais seria mentir. A cópia recusada é apagada.
 */
export async function lerComCopia<T>(usuarioId: string, chave: string, buscar: () => Promise<T>, agora: () => number = Date.now): Promise<Leitura<T>> {
  const chaveGuardada = `${usuarioId}:${chave}`;

  try {
    const dados = await buscar();
    const obtidaEm = agora();
    // Falha ao guardar não impede mostrar o que acabou de chegar; só não haverá cópia para depois.
    await gravar<CopiaGuardada<T>>('leituras', { chave: chaveGuardada, usuarioId, dados, obtidaEm }).catch(() => undefined);
    return { dados, obtidaEm, doAparelho: false };
  } catch (erro) {
    if (erro instanceof RespostaInesperada || (erro instanceof ErroDeApi && erro.status < 500 && erro.status !== 429)) {
      if (erro instanceof ErroDeApi && (erro.status === 404 || erro.status === 409)) {
        await apagar('leituras', chaveGuardada).catch(() => undefined);
      }
      throw erro;
    }

    const copia = await obter<CopiaGuardada<T>>('leituras', chaveGuardada).catch(() => undefined);
    if (!copia) {
      throw erro;
    }

    return { dados: copia.dados, obtidaEm: copia.obtidaEm, doAparelho: true };
  }
}

interface IdentidadeGuardada {
  readonly chave: 'motorista';
  readonly motorista: MotoristaAutenticado;
  readonly confirmadaEm: number;
}

/** Guarda quem está no aparelho — dados de exibição, nunca token — a cada sessão confirmada pelo servidor. */
export function guardarIdentidade(motorista: MotoristaAutenticado, agora: number = Date.now()): Promise<void> {
  return gravar<IdentidadeGuardada>('identidade', { chave: 'motorista', motorista, confirmadaEm: agora });
}

/**
 * Quem entrou neste aparelho, se o servidor confirmou a sessão há menos de 7 dias. Serve só para abrir o
 * aplicativo sem internet; a autorização continua sendo da API, a cada envio.
 */
export async function identidadeGuardada(agora: number = Date.now()): Promise<MotoristaAutenticado | null> {
  const guardada = await obter<IdentidadeGuardada>('identidade', 'motorista');
  if (!guardada) {
    return null;
  }

  if (agora - guardada.confirmadaEm > VALIDADE_DA_IDENTIDADE_EM_MS) {
    await apagar('identidade', 'motorista');
    return null;
  }

  return guardada.motorista;
}

/**
 * Saída do aparelho: apaga cópias, posições e identidade. As ações ainda não enviadas ficam — são trabalho
 * feito — e seguem para o servidor quando o mesmo motorista entrar de novo.
 *
 * Se o servidor não confirmou a saída (sem rede), fica anotado: na próxima abertura o aplicativo encerra a
 * sessão no servidor antes de qualquer renovação automática, para o aparelho não reentrar sozinho.
 */
export async function esquecerDoAparelho(saidaPendente: boolean): Promise<void> {
  await Promise.all([limpar('leituras'), limpar('posicoes'), apagar('identidade', 'motorista')]);
  if (saidaPendente) {
    await gravar('identidade', { chave: 'saida-pendente' });
  }
}

export async function haSaidaPendente(): Promise<boolean> {
  return (await obter('identidade', 'saida-pendente')) !== undefined;
}

export function confirmarSaida(): Promise<void> {
  return apagar('identidade', 'saida-pendente');
}

type PosicaoGuardada = PosicaoParaEnvio & { readonly usuarioId: string };

/** Posições ainda não enviadas, no disco: fechar o navegador no meio da rota não as perde. */
export function armazenamentoDePosicoes(usuarioId: string) {
  return {
    carregar: async (): Promise<PosicaoParaEnvio[]> =>
      (await listarDoUsuario<PosicaoGuardada>('posicoes', usuarioId)).map((guardada) => ({
        eventoDeLocalizacaoId: guardada.eventoDeLocalizacaoId,
        latitude: guardada.latitude,
        longitude: guardada.longitude,
        precisaoEmMetros: guardada.precisaoEmMetros,
        capturadaEm: guardada.capturadaEm,
        sequencia: guardada.sequencia,
        velocidadeEmMetrosPorSegundo: guardada.velocidadeEmMetrosPorSegundo,
        direcaoEmGraus: guardada.direcaoEmGraus,
      })),
    guardar: (posicoes: readonly PosicaoParaEnvio[]): Promise<void> =>
      gravarVarios<PosicaoGuardada>('posicoes', posicoes.map((posicao) => ({ ...posicao, usuarioId }))),
    remover: (ids: readonly string[]): Promise<void> => apagarVarios('posicoes', ids),
  };
}
