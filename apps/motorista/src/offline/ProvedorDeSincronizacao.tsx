import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';

import { apiDoMotorista } from '../infra/api';
import { assinarFila, enfileirar, marcarCiente, operacoesDoUsuario } from './fila';
import type { OperacaoLocal, PedidoDeOperacao } from './fila';
import { lerComCopia } from './guardadosNoAparelho';
import { projetarEntrega, projetarRota, projetarRotas } from './projecao';
import { Sincronizador } from './sincronizador';

interface ValorDaSincronizacao {
  readonly usuarioId: string;
  readonly operacoes: readonly OperacaoLocal[];
  /** Guarda a ação no aparelho e dispara o envio. Rejeita só se o aparelho não conseguir guardar. */
  readonly registrar: (pedido: PedidoDeOperacao) => Promise<void>;
  readonly marcarCiente: (id: string) => Promise<void>;
  readonly armazenamentoFalhou: boolean;
}

const ContextoDaSincronizacao = createContext<ValorDaSincronizacao | null>(null);

/**
 * Fila de ações do motorista e seu envio, para as telas com sessão.
 *
 * Envia ao abrir o aplicativo (o que ficou de antes de fechar ou reiniciar), quando a conexão volta, quando o
 * aplicativo volta a ficar visível, logo depois de cada ação e a cada intervalo — sem depender de Background
 * Sync, que nem todo navegador tem.
 */
export function ProvedorDeSincronizacao(props: { readonly usuarioId: string; readonly intervaloEmMs?: number; readonly children: ReactNode }) {
  const { usuarioId, intervaloEmMs = 30_000 } = props;
  const cliente = useQueryClient();
  const [operacoes, setOperacoes] = useState<readonly OperacaoLocal[]>([]);
  const [armazenamentoFalhou, setArmazenamentoFalhou] = useState(false);

  const sincronizador = useMemo(
    () =>
      new Sincronizador({
        usuarioId,
        enviar: apiDoMotorista.sincronizar,
        aoConcluir: (resumo) => {
          if (resumo.sincronizadas + resumo.conflitos + resumo.recusadas > 0) {
            // O servidor decidiu algo: a tela passa a mostrar o estado confirmado.
            void cliente.invalidateQueries({ queryKey: ['rotas'] });
            void cliente.invalidateQueries({ queryKey: ['rota'] });
            void cliente.invalidateQueries({ queryKey: ['entrega'] });
          }
        },
      }),
    [usuarioId, cliente],
  );

  const sincronizar = useCallback(() => {
    sincronizador.sincronizar().then(
      () => {
        setArmazenamentoFalhou(false);
      },
      () => {
        setArmazenamentoFalhou(true);
      },
    );
  }, [sincronizador]);

  useEffect(() => {
    let ativo = true;
    const recarregar = () => {
      operacoesDoUsuario(usuarioId).then(
        (lista) => {
          if (ativo) setOperacoes(lista);
        },
        () => {
          if (ativo) setArmazenamentoFalhou(true);
        },
      );
    };

    recarregar();
    const cancelar = assinarFila(recarregar);
    return () => {
      ativo = false;
      cancelar();
    };
  }, [usuarioId]);

  useEffect(() => {
    const aoFicarVisivel = () => {
      if (document.visibilityState === 'visible') sincronizar();
    };

    sincronizar();
    window.addEventListener('online', sincronizar);
    document.addEventListener('visibilitychange', aoFicarVisivel);
    const temporizador = setInterval(sincronizar, intervaloEmMs);

    return () => {
      window.removeEventListener('online', sincronizar);
      document.removeEventListener('visibilitychange', aoFicarVisivel);
      clearInterval(temporizador);
    };
  }, [sincronizar, intervaloEmMs]);

  const registrar = useCallback(
    async (pedido: PedidoDeOperacao) => {
      const operacao = await enfileirar(usuarioId, pedido);
      // A tela reflete a ação já, sem esperar a releitura da fila.
      setOperacoes((atuais) => (atuais.some((item) => item.id === operacao.id) ? atuais : [...atuais, operacao]));
      sincronizar();
    },
    [usuarioId, sincronizar],
  );

  const valor = useMemo<ValorDaSincronizacao>(
    () => ({ usuarioId, operacoes, registrar, marcarCiente, armazenamentoFalhou }),
    [usuarioId, operacoes, registrar, armazenamentoFalhou],
  );

  return <ContextoDaSincronizacao.Provider value={valor}>{props.children}</ContextoDaSincronizacao.Provider>;
}

export function useSincronizacao(): ValorDaSincronizacao {
  const valor = useContext(ContextoDaSincronizacao);
  if (!valor) {
    throw new Error('useSincronizacao precisa estar dentro de ProvedorDeSincronizacao.');
  }

  return valor;
}

/*
 * Leituras do motorista: servidor, ou a cópia do aparelho sem conexão, sempre com as ações da fila por cima.
 * `networkMode: 'always'` porque a consulta sabe cair para a cópia: pausar sem rede deixaria a tela em branco.
 */

export function useRotas() {
  const { usuarioId, operacoes } = useSincronizacao();
  const consulta = useQuery({
    queryKey: ['rotas'],
    queryFn: () => lerComCopia(usuarioId, 'rotas', apiDoMotorista.listarRotas),
    networkMode: 'always',
  });
  const dados = useMemo(
    () => (consulta.data ? projetarRotas(consulta.data.dados, operacoes, consulta.data.obtidaEm) : undefined),
    [consulta.data, operacoes],
  );

  return { consulta, dados, leitura: consulta.data };
}

export function useRota(rotaId: string) {
  const { usuarioId, operacoes } = useSincronizacao();
  const consulta = useQuery({
    queryKey: ['rota', rotaId],
    queryFn: () => lerComCopia(usuarioId, `rota:${rotaId}`, () => apiDoMotorista.obterRota(rotaId)),
    networkMode: 'always',
  });
  const dados = useMemo(
    () => (consulta.data ? projetarRota(consulta.data.dados, operacoes, consulta.data.obtidaEm) : undefined),
    [consulta.data, operacoes],
  );

  return { consulta, dados, leitura: consulta.data };
}

export function useEntrega(entregaId: string) {
  const { usuarioId, operacoes } = useSincronizacao();
  const consulta = useQuery({
    queryKey: ['entrega', entregaId],
    queryFn: () => lerComCopia(usuarioId, `entrega:${entregaId}`, () => apiDoMotorista.obterEntrega(entregaId)),
    networkMode: 'always',
  });
  const dados = useMemo(
    () => (consulta.data ? projetarEntrega(consulta.data.dados, operacoes, consulta.data.obtidaEm) : undefined),
    [consulta.data, operacoes],
  );

  return { consulta, dados, leitura: consulta.data };
}

/** Registrar uma ação a partir de uma tela: estado de "guardando" e falha de armazenamento. */
export function useRegistrarAcao() {
  const { registrar } = useSincronizacao();
  const [registrando, setRegistrando] = useState(false);
  const [falhou, setFalhou] = useState(false);

  const executar = useCallback(
    async (pedido: PedidoDeOperacao, depois?: () => void) => {
      setRegistrando(true);
      setFalhou(false);
      try {
        await registrar(pedido);
        depois?.();
      } catch {
        setFalhou(true);
      } finally {
        setRegistrando(false);
      }
    },
    [registrar],
  );

  return { executar, registrando, falhou };
}
