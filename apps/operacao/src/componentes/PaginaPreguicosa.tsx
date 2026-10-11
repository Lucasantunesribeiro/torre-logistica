import { Component, type ReactNode, Suspense } from 'react';

/**
 * Invólucro para páginas carregadas sob demanda (lazy).
 *
 * Duas responsabilidades que todo `React.lazy` precisa ter: um estado de carregamento enquanto o pedaço
 * de código chega (Suspense) e uma saída digna quando ele NÃO chega — rede caindo no meio do download do
 * chunk, deploy novo que trocou o arquivo. Sem o limite de erro, uma falha de chunk derruba a tela toda
 * em branco; com ele, vira um aviso com "tentar de novo".
 */
class LimiteDeErroDeChunk extends Component<{ readonly children: ReactNode }, { readonly falhou: boolean }> {
  constructor(props: { readonly children: ReactNode }) {
    super(props);
    this.state = { falhou: false };
  }

  static getDerivedStateFromError(): { readonly falhou: boolean } {
    return { falhou: true };
  }

  override render(): ReactNode {
    if (this.state.falhou) {
      return (
        <p className="estado estado--erro" role="alert">
          Não foi possível carregar esta parte do console.{' '}
          <button type="button" className="ligacao" onClick={() => window.location.reload()}>
            Tentar de novo
          </button>
        </p>
      );
    }

    return this.props.children;
  }
}

export function PaginaPreguicosa({ children }: { readonly children: ReactNode }) {
  return (
    <LimiteDeErroDeChunk>
      <Suspense
        fallback={
          <p className="estado estado--carregando" role="status">
            Carregando…
          </p>
        }
      >
        {children}
      </Suspense>
    </LimiteDeErroDeChunk>
  );
}
