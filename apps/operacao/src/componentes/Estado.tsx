import type { ReactNode } from 'react';

import { ErroDaApi } from '../infra/api';

/**
 * Os três estados que toda tela de leitura tem, num lugar só.
 *
 * Repetir "carregando / deu erro / não há nada" em cada tela faria cada uma errar de um jeito diferente —
 * e a que errasse mostraria tabela vazia como se fosse resposta.
 */
export function Estado({
  carregando,
  erro,
  vazio,
  mensagemVazio,
  children,
}: {
  readonly carregando: boolean;
  readonly erro: unknown;
  readonly vazio?: boolean;
  readonly mensagemVazio?: string;
  readonly children: ReactNode;
}) {
  if (carregando) {
    return (
      <p className="estado" role="status">
        Carregando…
      </p>
    );
  }

  if (erro !== null && erro !== undefined) {
    return (
      <p className="estado estado--erro" role="alert">
        {erro instanceof ErroDaApi ? erro.message : 'Não foi possível carregar. Tente de novo.'}
      </p>
    );
  }

  if (vazio === true) {
    return <p className="estado">{mensagemVazio ?? 'Nada para mostrar.'}</p>;
  }

  return <>{children}</>;
}

/** Etiqueta de situação com cor por severidade. */
export function Selo({ tom, children }: { readonly tom: 'bom' | 'atencao' | 'ruim' | 'neutro'; readonly children: ReactNode }) {
  return <span className={`selo selo--${tom}`}>{children}</span>;
}

/** Formata um instante ISO no fuso do navegador, curto. */
export function instante(iso: string | null): string {
  return iso === null ? '—' : new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

/** Formata só a hora. */
export function hora(iso: string | null): string {
  return iso === null ? '—' : new Date(iso).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
}
