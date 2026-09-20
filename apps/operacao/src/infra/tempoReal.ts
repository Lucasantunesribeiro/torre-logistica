import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';

import { ambiente } from './ambiente';
import { obterSessao } from './sessao';

/** Aviso de posição do motorista, como o hub publica. */
export interface PosicaoAoVivo {
  readonly motoristaId: string;
  readonly latitude: number;
  readonly longitude: number;
  readonly capturadaEm: string;
}

/** Nomes das mensagens que o hub envia. Espelham o contrato do servidor. */
export const MENSAGENS = {
  posicao: 'DriverPositionUpdated',
  status: 'DeliveryStatusChanged',
  risco: 'DeliveryRiskChanged',
  alertaCriado: 'AlertCreated',
  alertaResolvido: 'AlertResolved',
  ocorrencia: 'IncidentCreated',
} as const;

/** Situação da ligação ao vivo, para a tela dizer a verdade sobre o que está vendo. */
export type EstadoDoTempoReal = 'conectando' | 'conectado' | 'desconectado';

/**
 * Liga o console ao canal de tempo real da operação.
 *
 * O token vai na query string porque o navegador não envia cabeçalho `Authorization` no aperto de mão do
 * WebSocket — o servidor só aceita o token por ali no caminho do hub, e em nenhuma outra rota (ADR 0017).
 *
 * Aviso recebido **invalida** a consulta correspondente em vez de remendar o cache à mão: a fonte da
 * verdade continua sendo a API, e o aviso é só o gatilho para reler. Posição é a exceção — ela chega
 * muitas vezes por minuto e alimenta o mapa direto, sem ida ao servidor.
 */
export function useTempoRealDaOperacao(aoReceberPosicao?: (posicao: PosicaoAoVivo) => void): EstadoDoTempoReal {
  const clienteDeConsultas = useQueryClient();
  const [estado, setEstado] = useState<EstadoDoTempoReal>('conectando');

  useEffect(() => {
    let ativa = true;

    const conexao: HubConnection = new HubConnectionBuilder()
      .withUrl(`${ambiente.urlDaApi}/tempo-real/operacao`, {
        accessTokenFactory: () => obterSessao()?.tokenDeAcesso ?? '',
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    const invalidar = (chave: string) => () => {
      void clienteDeConsultas.invalidateQueries({ queryKey: [chave] });
    };

    conexao.on(MENSAGENS.posicao, (aviso: PosicaoAoVivo) => aoReceberPosicao?.(aviso));
    conexao.on(MENSAGENS.status, invalidar('entregas'));
    conexao.on(MENSAGENS.risco, invalidar('entregas'));
    conexao.on(MENSAGENS.alertaCriado, invalidar('alertas'));
    conexao.on(MENSAGENS.alertaResolvido, invalidar('alertas'));
    conexao.on(MENSAGENS.ocorrencia, invalidar('ocorrencias'));

    conexao.onreconnecting(() => {
      if (ativa) {
        setEstado('conectando');
      }
    });

    conexao.onreconnected(() => {
      if (ativa) {
        setEstado('conectado');
      }
    });

    conexao.onclose(() => {
      if (ativa) {
        setEstado('desconectado');
      }
    });

    conexao
      .start()
      .then(() => {
        if (ativa) {
          setEstado('conectado');
        }
      })
      .catch(() => {
        // Sem tempo real a tela continua útil: as consultas seguem pelo caminho normal.
        if (ativa) {
          setEstado('desconectado');
        }
      });

    return () => {
      ativa = false;

      if (conexao.state !== HubConnectionState.Disconnected) {
        void conexao.stop();
      }
    };
  }, [clienteDeConsultas, aoReceberPosicao]);

  return estado;
}
