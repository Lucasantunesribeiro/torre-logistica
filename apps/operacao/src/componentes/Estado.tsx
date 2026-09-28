import type { ReactNode } from 'react';

import { ErroDaApi, type Severidade } from '../infra/api';

type Tom = 'bom' | 'atencao' | 'ruim' | 'neutro';

/**
 * Tom visual de cada severidade — a mesma escala do domínio, num só lugar.
 *
 * Centralizado para alerta, ocorrência e painel não divergirem: Baixa é ruído de fundo (neutro),
 * Média pede atenção, e Alta/Crítica são o vermelho que interrompe a leitura.
 */
const TOM_POR_SEVERIDADE: Record<Severidade, Tom> = {
  Baixa: 'neutro',
  Media: 'atencao',
  Alta: 'ruim',
  Critica: 'ruim',
};

export function tomDaSeveridade(severidade: Severidade): Tom {
  return TOM_POR_SEVERIDADE[severidade];
}

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
      <p className="estado estado--carregando" role="status">
        Carregando…
      </p>
    );
  }

  if (erro !== null && erro !== undefined) {
    // Sem permissão é diferente de falha: não adianta "tente de novo", e o tom é informativo, não
    // de erro. A tela é aberta pelo menu, então isto é rede de segurança — o menu já não deveria
    // oferecer o que o perfil não acessa.
    if (erro instanceof ErroDaApi && erro.status === 403) {
      return (
        <p className="estado estado--proibido" role="status">
          Seu perfil não tem acesso a esta área.
        </p>
      );
    }

    return (
      <p className="estado estado--erro" role="alert">
        {erro instanceof ErroDaApi ? erro.message : 'Não foi possível carregar. Tente de novo.'}
      </p>
    );
  }

  if (vazio === true) {
    return <p className="estado estado--vazio">{mensagemVazio ?? 'Nada para mostrar.'}</p>;
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
