import type { PosicaoParaEnvio } from '../infra/api';
import { uuidv7 } from '../infra/uuidv7';

export type EstadoDoGps = 'inativo' | 'aguardando-permissao' | 'ativo' | 'negado' | 'indisponivel' | 'falhando';

export interface EstadoDoRastreador {
  readonly gps: EstadoDoGps;
  readonly pendentes: number;
  readonly ultimoEnvioEm: number | null;
  readonly falhaNoEnvio: boolean;
}

export interface OpcoesDoRastreador {
  /** `navigator.geolocation`, ou `undefined` quando o navegador não oferece. */
  readonly geolocalizacao: Geolocation | undefined;
  readonly enviar: (posicoes: readonly PosicaoParaEnvio[]) => Promise<unknown>;
  readonly aoMudar: (estado: EstadoDoRastreador) => void;
  readonly intervaloDeEnvioEmMs?: number;
  readonly limiteDePendentes?: number;
  readonly agora?: () => number;
  readonly gerarId?: () => string;
}

const TAMANHO_MAXIMO_DO_LOTE = 500;

/**
 * Coleta a localização enquanto o aplicativo está aberto e envia em lote.
 *
 * Limites reais, e não prometidos (docs/adr/0020-pwa-do-motorista.md):
 * - `watchPosition` só entrega posição com a página ativa. Com a tela bloqueada, o aplicativo em segundo
 *   plano ou a aba escondida, o navegador suspende ou espaça as leituras — não existe API web de
 *   localização em segundo plano. O aplicativo diz isso ao motorista.
 * - Permissão negada não se pede de novo por código: só o motorista libera nas configurações do navegador.
 *
 * Cada posição nasce com identificador (UUIDv7) e sequência crescente no aparelho. Se o envio falhar, o lote
 * fica em memória, limitado, e vai de novo com os mesmos identificadores — o servidor não duplica. A fila
 * persistente, que sobrevive a fechar o navegador, é da operação offline.
 */
export class RastreadorDeLocalizacao {
  private readonly pendentes: PosicaoParaEnvio[] = [];
  private readonly intervaloDeEnvioEmMs: number;
  private readonly limiteDePendentes: number;
  private readonly agora: () => number;
  private readonly gerarId: () => string;
  private gps: EstadoDoGps = 'inativo';
  private ultimaSequencia = 0;
  private ultimoEnvioEm: number | null = null;
  private falhaNoEnvio = false;
  private enviando = false;
  private idDoMonitoramento: number | null = null;
  private temporizador: ReturnType<typeof setInterval> | null = null;

  constructor(private readonly opcoes: OpcoesDoRastreador) {
    this.intervaloDeEnvioEmMs = opcoes.intervaloDeEnvioEmMs ?? 15_000;
    this.limiteDePendentes = opcoes.limiteDePendentes ?? 200;
    this.agora = opcoes.agora ?? (() => Date.now());
    this.gerarId = opcoes.gerarId ?? (() => uuidv7(this.agora()));
  }

  get estado(): EstadoDoRastreador {
    return {
      gps: this.gps,
      pendentes: this.pendentes.length,
      ultimoEnvioEm: this.ultimoEnvioEm,
      falhaNoEnvio: this.falhaNoEnvio,
    };
  }

  iniciar(): void {
    if (this.idDoMonitoramento !== null) {
      return;
    }

    const geolocalizacao = this.opcoes.geolocalizacao;
    if (!geolocalizacao) {
      this.mudar('indisponivel');
      return;
    }

    this.mudar('aguardando-permissao');
    this.idDoMonitoramento = geolocalizacao.watchPosition(
      (posicao) => {
        this.registrar(posicao);
      },
      (erro) => {
        this.aoFalhar(erro);
      },
      { enableHighAccuracy: true, maximumAge: 10_000, timeout: 30_000 },
    );

    this.temporizador = setInterval(() => {
      void this.descarregar();
    }, this.intervaloDeEnvioEmMs);
  }

  /** Para de coletar e tenta enviar o que ficou. */
  parar(): void {
    if (this.idDoMonitoramento !== null) {
      this.opcoes.geolocalizacao?.clearWatch(this.idDoMonitoramento);
      this.idDoMonitoramento = null;
    }

    if (this.temporizador !== null) {
      clearInterval(this.temporizador);
      this.temporizador = null;
    }

    void this.descarregar();
    this.mudar('inativo');
  }

  /** Envia o que está pendente. Uma chamada por vez; em falha, nada se perde. */
  async descarregar(): Promise<void> {
    if (this.enviando || this.pendentes.length === 0) {
      return;
    }

    const lote = this.pendentes.slice(0, TAMANHO_MAXIMO_DO_LOTE);
    this.enviando = true;

    try {
      await this.opcoes.enviar(lote);
      // Recusa de item (fora da rota, imprecisa demais) volta no resultado e não adianta reenviar.
      this.pendentes.splice(0, lote.length);
      this.ultimoEnvioEm = this.agora();
      this.falhaNoEnvio = false;
    } catch {
      this.falhaNoEnvio = true;
    } finally {
      this.enviando = false;
      this.notificar();
    }
  }

  private registrar(posicao: GeolocationPosition): void {
    // Sequência crescente mesmo que o aparelho repita o instante da leitura.
    this.ultimaSequencia = Math.max(this.ultimaSequencia + 1, Math.floor(posicao.timestamp));

    this.pendentes.push({
      eventoDeLocalizacaoId: this.gerarId(),
      latitude: posicao.coords.latitude,
      longitude: posicao.coords.longitude,
      precisaoEmMetros: posicao.coords.accuracy,
      capturadaEm: new Date(posicao.timestamp).toISOString(),
      sequencia: this.ultimaSequencia,
      velocidadeEmMetrosPorSegundo: numeroOuNulo(posicao.coords.speed),
      direcaoEmGraus: numeroOuNulo(posicao.coords.heading),
    });

    // Sem conexão por muito tempo, as mais antigas saem primeiro: a posição atual vale mais que o trajeto.
    if (this.pendentes.length > this.limiteDePendentes) {
      this.pendentes.splice(0, this.pendentes.length - this.limiteDePendentes);
    }

    this.mudar('ativo');
  }

  private aoFalhar(erro: GeolocationPositionError): void {
    if (erro.code === erro.PERMISSION_DENIED) {
      this.mudar('negado');
      return;
    }

    // Sem sinal ou tempo esgotado: o monitoramento continua e tenta de novo sozinho.
    this.mudar('falhando');
  }

  private mudar(gps: EstadoDoGps): void {
    this.gps = gps;
    this.notificar();
  }

  private notificar(): void {
    this.opcoes.aoMudar(this.estado);
  }
}

function numeroOuNulo(valor: number | null): number | null {
  return valor === null || Number.isNaN(valor) ? null : valor;
}
