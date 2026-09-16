import type { OperacaoParaEnvio, ResultadoDaSincronizacao, ResultadoDeOperacao } from '../infra/api';
import { ErroDeApi } from '../infra/sessao';
import { aguardandoEnvio, limparResolvidas, operacoesDoUsuario, salvarOperacoes } from './fila';
import type { OperacaoLocal } from './fila';

/** Mesmo limite do servidor por envio. */
export const TAMANHO_MAXIMO_DO_LOTE = 100;

export interface ResumoDaSincronizacao {
  readonly enviadas: number;
  readonly sincronizadas: number;
  readonly conflitos: number;
  readonly recusadas: number;
  readonly adiadas: number;
}

export interface OpcoesDoSincronizador {
  readonly usuarioId: string;
  readonly enviar: (operacoes: readonly OperacaoParaEnvio[]) => Promise<ResultadoDaSincronizacao>;
  readonly online?: () => boolean;
  readonly agora?: () => number;
  readonly aoConcluir?: (resumo: ResumoDaSincronizacao) => void;
}

/**
 * Envia a fila do aparelho ao servidor, em ordem, um lote por vez.
 *
 * Não depende de Background Sync (CLAUDE.md, seção 29): quem chama decide quando — ao abrir o aplicativo, ao
 * voltar a conexão, depois de cada ação e periodicamente. Toda chamada é segura de repetir: o servidor
 * aplica cada `ClientOperationId` uma vez e devolve o mesmo desfecho a qualquer repetição.
 */
export class Sincronizador {
  private emCurso: Promise<void> | null = null;
  private pedidoDuranteEnvio = false;
  private readonly online: () => boolean;
  private readonly agora: () => number;

  constructor(private readonly opcoes: OpcoesDoSincronizador) {
    this.online = opcoes.online ?? (() => navigator.onLine);
    this.agora = opcoes.agora ?? (() => Date.now());
  }

  /** Um envio por vez. Pedido feito durante um envio vira mais uma volta ao final dele. */
  sincronizar(): Promise<void> {
    if (this.emCurso) {
      this.pedidoDuranteEnvio = true;
      return this.emCurso;
    }

    this.emCurso = (async () => {
      try {
        do {
          this.pedidoDuranteEnvio = false;
          await this.rodada();
        } while (this.pedidoDuranteEnvio);
      } finally {
        this.emCurso = null;
      }
    })();

    return this.emCurso;
  }

  private async rodada(): Promise<void> {
    if (!this.online()) {
      return;
    }

    const resumo = { enviadas: 0, sincronizadas: 0, conflitos: 0, recusadas: 0, adiadas: 0 };

    for (;;) {
      // "Uploading" aqui só pode ser de um envio interrompido — aba fechada, aparelho reiniciado —, porque
      // este sincronizador faz um envio por vez. Vai de novo, com o mesmo identificador.
      const lote = (await operacoesDoUsuario(this.opcoes.usuarioId)).filter(aguardandoEnvio).slice(0, TAMANHO_MAXIMO_DO_LOTE);
      if (lote.length === 0) {
        break;
      }

      const instante = new Date(this.agora()).toISOString();
      const enviando = lote.map<OperacaoLocal>((operacao) => ({
        ...operacao,
        status: 'Uploading',
        tentativas: operacao.tentativas + 1,
        ultimaTentativaEm: instante,
      }));
      await salvarOperacoes(enviando);
      resumo.enviadas += enviando.length;

      let resposta: ResultadoDaSincronizacao;
      try {
        resposta = await this.opcoes.enviar(enviando.map(paraEnvio));
      } catch (erro) {
        await salvarOperacoes(enviando.map((operacao) => depoisDaFalhaDoEnvio(operacao, erro)));
        resumo.adiadas += enviando.length;
        break;
      }

      const porId = new Map(
        resposta.resultados
          .filter((resultado) => resultado.operacaoDoClienteId)
          .map((resultado) => [resultado.operacaoDoClienteId!.toLowerCase(), resultado]),
      );
      const atualizadas = enviando.map((operacao) => comResultado(operacao, porId.get(operacao.id.toLowerCase()), instante));
      await salvarOperacoes(atualizadas);

      for (const operacao of atualizadas) {
        if (operacao.status === 'Synced') resumo.sincronizadas += 1;
        else if (operacao.status === 'Conflict') resumo.conflitos += 1;
        else if (operacao.status === 'Failed') resumo.recusadas += 1;
        else resumo.adiadas += 1;
      }

      // Gravação concorrente no servidor: tenta na próxima volta, sem martelar agora.
      if (atualizadas.some(aguardandoEnvio)) {
        break;
      }
    }

    await limparResolvidas(this.opcoes.usuarioId, this.agora());
    this.opcoes.aoConcluir?.(resumo);
  }
}

function paraEnvio(operacao: OperacaoLocal): OperacaoParaEnvio {
  return {
    operacaoDoClienteId: operacao.id,
    tipo: operacao.tipo,
    alvoId: operacao.payload.alvoId,
    motivo: operacao.payload.motivo,
    observacao: operacao.payload.observacao,
    criadaEm: operacao.criadaEm,
  };
}

/**
 * Sem rede, servidor fora, sessão a renovar ou limite de envio: a ação continua na fila. Só o lote recusado
 * inteiro por formato (400) é definitivo — o mesmo conteúdo seria recusado de novo.
 */
function depoisDaFalhaDoEnvio(operacao: OperacaoLocal, erro: unknown): OperacaoLocal {
  if (erro instanceof ErroDeApi && erro.status === 400) {
    return { ...operacao, status: 'Failed', codigo: erro.codigo ?? 'lote_recusado', mensagem: erro.message };
  }

  return { ...operacao, status: 'Pending' };
}

function comResultado(operacao: OperacaoLocal, resultado: ResultadoDeOperacao | undefined, instante: string): OperacaoLocal {
  switch (resultado?.desfecho) {
    case 'Aplicada':
      return { ...operacao, status: 'Synced', sincronizadaEm: instante, codigo: null, mensagem: null };
    case 'Conflito':
      return { ...operacao, status: 'Conflict', codigo: resultado.codigo ?? null, mensagem: resultado.mensagem ?? null };
    case 'Recusada':
      return { ...operacao, status: 'Failed', codigo: resultado.codigo ?? null, mensagem: resultado.mensagem ?? null };
    default:
      // Sem resultado para ela, ou "tente de novo": nada foi aplicado; fica na fila.
      return { ...operacao, status: 'Pending' };
  }
}
