import { mensagemDeErro } from '../infra/api';

/** Falha mostrada ao motorista: o que aconteceu e, quando adianta, como tentar de novo. */
export function Aviso({ erro, aoTentarDeNovo }: { readonly erro: unknown; readonly aoTentarDeNovo?: () => void }) {
  return (
    <div className="cartao aviso" role="alert">
      <p>{mensagemDeErro(erro)}</p>
      {aoTentarDeNovo ? (
        <button type="button" className="acao acao--secundaria" onClick={aoTentarDeNovo}>
          Tentar de novo
        </button>
      ) : null}
    </div>
  );
}

export function Carregando({ texto }: { readonly texto: string }) {
  return (
    <p className="sumario" role="status" aria-live="polite">
      {texto}
    </p>
  );
}
