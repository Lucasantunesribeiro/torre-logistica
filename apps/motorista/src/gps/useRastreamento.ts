import { useEffect, useState } from 'react';

import { apiDoMotorista } from '../infra/api';
import { armazenamentoDePosicoes } from '../offline/guardadosNoAparelho';
import { RastreadorDeLocalizacao } from './rastreador';
import type { EstadoDoRastreador } from './rastreador';

const INATIVO: EstadoDoRastreador = { gps: 'inativo', pendentes: 0, ultimoEnvioEm: null, falhaNoEnvio: false };

/**
 * Liga a coleta de localização enquanto há rota em andamento — e só enquanto isso. Fora da execução da
 * rota o aplicativo não pede nem coleta localização (coleta mínima, ADR 0015). Posições não enviadas ficam
 * no aparelho, do motorista da sessão.
 */
export function useRastreamento(ativo: boolean, usuarioId: string): EstadoDoRastreador {
  const [estado, setEstado] = useState<EstadoDoRastreador>(INATIVO);

  useEffect(() => {
    if (!ativo) {
      return undefined;
    }

    const rastreador = new RastreadorDeLocalizacao({
      geolocalizacao: typeof navigator === 'undefined' ? undefined : navigator.geolocation,
      enviar: (posicoes) => apiDoMotorista.enviarPosicoes(posicoes),
      aoMudar: setEstado,
      armazenamento: armazenamentoDePosicoes(usuarioId),
    });

    rastreador.iniciar();

    return () => {
      rastreador.parar();
    };
  }, [ativo, usuarioId]);

  return ativo ? estado : INATIVO;
}
