import { afterEach, describe, expect, it, vi } from 'vitest';

import type { PosicaoParaEnvio } from '../infra/api';
import { RastreadorDeLocalizacao } from './rastreador';
import type { EstadoDoRastreador } from './rastreador';

function geolocalizacaoFalsa() {
  let sucesso: PositionCallback = () => undefined;
  let falha: PositionErrorCallback = () => undefined;

  return {
    api: {
      watchPosition: vi.fn((aoSucesso: PositionCallback, aoFalhar?: PositionErrorCallback | null) => {
        sucesso = aoSucesso;
        falha = aoFalhar ?? (() => undefined);
        return 42;
      }),
      clearWatch: vi.fn(),
      getCurrentPosition: vi.fn(),
    },
    emitir(timestamp: number, latitude = -22.9) {
      sucesso({
        timestamp,
        coords: { latitude, longitude: -47.06, accuracy: 7, speed: 3.5, heading: Number.NaN, altitude: null, altitudeAccuracy: null },
      } as GeolocationPosition);
    },
    falhar(code: number) {
      falha({ code, PERMISSION_DENIED: 1, POSITION_UNAVAILABLE: 2, TIMEOUT: 3, message: 'falha' });
    },
  };
}

function criar(opcoes: { enviar?: (posicoes: readonly PosicaoParaEnvio[]) => Promise<unknown>; limite?: number } = {}) {
  const geo = geolocalizacaoFalsa();
  const estados: EstadoDoRastreador[] = [];
  let id = 0;
  const enviar = vi.fn(opcoes.enviar ?? (() => Promise.resolve({})));

  const rastreador = new RastreadorDeLocalizacao({
    geolocalizacao: geo.api,
    enviar,
    aoMudar: (estado) => estados.push(estado),
    agora: () => 1_000,
    gerarId: () => `id-${++id}`,
    ...(opcoes.limite === undefined ? {} : { limiteDePendentes: opcoes.limite }),
  });

  return { geo, estados, enviar, rastreador };
}

const rastreadores: RastreadorDeLocalizacao[] = [];

afterEach(() => {
  rastreadores.splice(0).forEach((rastreador) => {
    rastreador.parar();
  });
});

describe('rastreador de localização', () => {
  it('coleta com identificador do aparelho, sequência crescente e envia em lote', async () => {
    const { geo, enviar, rastreador } = criar();
    rastreadores.push(rastreador);
    rastreador.iniciar();

    // O aparelho pode repetir o instante da leitura: a sequência cresce mesmo assim.
    geo.emitir(1_726_400_000_000);
    geo.emitir(1_726_400_000_000);
    await rastreador.descarregar();

    expect(enviar).toHaveBeenCalledTimes(1);
    const lote = enviar.mock.calls[0]![0];
    expect(lote.map((posicao) => posicao.eventoDeLocalizacaoId)).toEqual(['id-1', 'id-2']);
    expect(lote[1]!.sequencia).toBeGreaterThan(lote[0]!.sequencia);
    expect(lote[0]).toMatchObject({ precisaoEmMetros: 7, velocidadeEmMetrosPorSegundo: 3.5, direcaoEmGraus: null });
    expect(lote[0]!.capturadaEm).toBe(new Date(1_726_400_000_000).toISOString());
    expect(rastreador.estado).toMatchObject({ gps: 'ativo', pendentes: 0, ultimoEnvioEm: 1_000, falhaNoEnvio: false });
  });

  it('mantém as posições quando o envio falha e reenvia com os mesmos identificadores', async () => {
    let falhar = true;
    const { geo, enviar, rastreador } = criar({ enviar: () => (falhar ? Promise.reject(new Error('rede')) : Promise.resolve({})) });
    rastreadores.push(rastreador);
    rastreador.iniciar();
    geo.emitir(1_726_400_000_000);

    await rastreador.descarregar();
    expect(rastreador.estado).toMatchObject({ pendentes: 1, falhaNoEnvio: true });

    falhar = false;
    await rastreador.descarregar();

    expect(enviar.mock.calls.map(([lote]) => lote[0]!.eventoDeLocalizacaoId)).toEqual(['id-1', 'id-1']);
    expect(rastreador.estado).toMatchObject({ pendentes: 0, falhaNoEnvio: false });
  });

  it('permissão negada é informada como negada', () => {
    const { geo, rastreador } = criar();
    rastreadores.push(rastreador);
    rastreador.iniciar();

    expect(rastreador.estado.gps).toBe('aguardando-permissao');
    geo.falhar(1);

    expect(rastreador.estado.gps).toBe('negado');
  });

  it('sem sinal fica falhando e volta a ativo quando a posição chega', () => {
    const { geo, rastreador } = criar();
    rastreadores.push(rastreador);
    rastreador.iniciar();

    geo.falhar(3);
    expect(rastreador.estado.gps).toBe('falhando');

    geo.emitir(1_726_400_000_000);
    expect(rastreador.estado.gps).toBe('ativo');
  });

  it('navegador sem geolocalização fica indisponível', () => {
    const estados: EstadoDoRastreador[] = [];
    const rastreador = new RastreadorDeLocalizacao({
      geolocalizacao: undefined,
      enviar: () => Promise.resolve({}),
      aoMudar: (estado) => estados.push(estado),
    });

    rastreador.iniciar();

    expect(rastreador.estado.gps).toBe('indisponivel');
  });

  it('sem conexão por muito tempo guarda só as mais recentes', async () => {
    const { geo, enviar, rastreador } = criar({ limite: 3 });
    rastreadores.push(rastreador);
    rastreador.iniciar();

    for (let indice = 0; indice < 5; indice++) {
      geo.emitir(1_726_400_000_000 + indice * 1_000);
    }
    await rastreador.descarregar();

    expect(enviar.mock.calls[0]![0].map((posicao) => posicao.eventoDeLocalizacaoId)).toEqual(['id-3', 'id-4', 'id-5']);
  });

  it('parar desliga o monitoramento e envia o que ficou', async () => {
    const { geo, enviar, rastreador } = criar();
    rastreador.iniciar();
    geo.emitir(1_726_400_000_000);

    rastreador.parar();
    await Promise.resolve();

    expect(geo.api.clearWatch).toHaveBeenCalledWith(42);
    expect(enviar).toHaveBeenCalledTimes(1);
    expect(rastreador.estado.gps).toBe('inativo');
  });
});
