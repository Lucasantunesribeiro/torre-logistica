import { createContext, useContext, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';

import { assinarSessao, entrar, obterSessao, renovarSessao, sair } from '../infra/sessao';
import type { Credenciais, MotoristaAutenticado, SessaoAtiva } from '../infra/sessao';

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

/**
 * Mantém a interface sincronizada com a sessão. Ao abrir, tenta a renovação pelo cookie: o motorista que
 * já entrou hoje não digita senha de novo.
 */
export function ProvedorDeSessao({ children }: { readonly children: ReactNode }) {
  const [estado, setEstado] = useState<EstadoDaSessao>(() => {
    const sessao = obterSessao();
    return sessao ? estadoDe(sessao) : { situacao: 'verificando' };
  });

  useEffect(() => {
    const cancelarAssinatura = assinarSessao((sessao) => {
      setEstado(estadoDe(sessao));
    });

    if (!obterSessao()) {
      void renovarSessao().catch(() => {
        // Sem rede não dá para saber se a sessão vale; o caminho seguro é pedir login.
        setEstado({ situacao: 'anonima' });
      });
    }

    return cancelarAssinatura;
  }, []);

  const valor = useMemo<ValorDaSessao>(() => ({ estado, entrar, sair }), [estado]);

  return <ContextoDaSessao.Provider value={valor}>{children}</ContextoDaSessao.Provider>;
}

export function useSessao(): ValorDaSessao {
  const valor = useContext(ContextoDaSessao);
  if (!valor) {
    throw new Error('useSessao precisa estar dentro de ProvedorDeSessao.');
  }

  return valor;
}
