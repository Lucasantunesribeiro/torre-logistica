import { createContext, useContext, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';

import { assinarSessao, entrar, obterSessao, renovarSessao, sair } from '../infra/sessao';
import type { Credenciais, MotoristaAutenticado, SessaoAtiva } from '../infra/sessao';
import { confirmarSaida, esquecerDoAparelho, guardarIdentidade, haSaidaPendente, identidadeGuardada } from '../offline/guardadosNoAparelho';

export type EstadoDaSessao =
  | { readonly situacao: 'verificando' }
  | { readonly situacao: 'anonima' }
  | { readonly situacao: 'autenticada'; readonly motorista: MotoristaAutenticado };

interface ValorDaSessao {
  readonly estado: EstadoDaSessao;
  readonly entrar: (credenciais: Credenciais) => Promise<SessaoAtiva>;
  readonly sair: () => Promise<void>;
}

const ContextoDaSessao = createContext<ValorDaSessao | null>(null);

function estadoDe(sessao: SessaoAtiva | null): EstadoDaSessao {
  return sessao ? { situacao: 'autenticada', motorista: sessao.usuario } : { situacao: 'anonima' };
}

async function sairDoAparelho(): Promise<void> {
  const confirmada = await sair();
  // Armazenamento indisponível não impede sair: a sessão em memória já terminou.
  await esquecerDoAparelho(!confirmada).catch(() => undefined);
}

/**
 * Mantém a interface sincronizada com a sessão.
 *
 * Ao abrir, tenta a renovação pelo cookie: o motorista que já entrou não digita senha de novo. Sem rede, não
 * há como o servidor confirmar — então quem entrou neste aparelho nos últimos 7 dias abre a rota guardada e
 * continua registrando ações na fila; a API autoriza cada uma quando a conexão voltar, e sessão recusada
 * leva ao login sem perder a fila.
 */
export function ProvedorDeSessao({ children }: { readonly children: ReactNode }) {
  const [estado, setEstado] = useState<EstadoDaSessao>(() => {
    const sessao = obterSessao();
    return sessao ? estadoDe(sessao) : { situacao: 'verificando' };
  });

  useEffect(() => {
    const cancelarAssinatura = assinarSessao((sessao) => {
      setEstado(estadoDe(sessao));
      if (sessao) {
        void guardarIdentidade(sessao.usuario).catch(() => undefined);
      }
    });

    async function iniciar() {
      // Saída feita sem rede: encerra no servidor antes que a renovação automática reabra a sessão.
      if (await haSaidaPendente().catch(() => false)) {
        if (await sair()) {
          await confirmarSaida().catch(() => undefined);
        }
        setEstado({ situacao: 'anonima' });
        return;
      }

      try {
        await renovarSessao();
      } catch {
        const motorista = await identidadeGuardada().catch(() => null);
        setEstado(motorista ? { situacao: 'autenticada', motorista } : { situacao: 'anonima' });
      }
    }

    if (!obterSessao()) {
      void iniciar();
    }

    return cancelarAssinatura;
  }, []);

  const valor = useMemo<ValorDaSessao>(() => ({ estado, entrar, sair: sairDoAparelho }), [estado]);

  return <ContextoDaSessao.Provider value={valor}>{children}</ContextoDaSessao.Provider>;
}

export function useSessao(): ValorDaSessao {
  const valor = useContext(ContextoDaSessao);
  if (!valor) {
    throw new Error('useSessao precisa estar dentro de ProvedorDeSessao.');
  }

  return valor;
}
