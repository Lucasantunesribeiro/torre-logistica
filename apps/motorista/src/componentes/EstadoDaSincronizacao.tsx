import { aguardandoEnvio, precisaDeAtencao } from '../offline/fila';
import type { OperacaoLocal } from '../offline/fila';
import type { Leitura } from '../offline/guardadosNoAparelho';
import { useSincronizacao } from '../offline/ProvedorDeSincronizacao';
import { formatarHora } from '../infra/formatos';

export const MENSAGEM_DE_FALHA_AO_GUARDAR = 'Não foi possível guardar a ação neste aparelho. Tente de novo.';

/** O que aconteceu com a ação, na língua do motorista, e o que fazer. */
export function mensagemDoDesfecho(operacao: OperacaoLocal): string {
  switch (operacao.codigo) {
    case 'transicao_invalida':
      return 'A situação mudou antes de a ação chegar à operação — por exemplo, a entrega foi cancelada ou reagendada. Confira a tela atualizada.';
    case 'entrega_reatribuida':
      return 'Esta entrega foi passada para outro motorista antes de a ação chegar à operação.';
    case 'entrega_nao_encontrada':
    case 'rota_nao_encontrada':
      return 'Este item não está mais na sua rota.';
    case 'rota_com_entregas_pendentes':
      return 'Ainda havia entregas sem resultado quando o encerramento da rota chegou.';
    case 'operacao_antiga':
      return 'A ação ficou mais de 7 dias sem ser enviada e não vale mais. Fale com a operação.';
    default:
      return operacao.mensagem ?? 'A operação não aceitou esta ação. Fale com a operação se tiver dúvida.';
  }
}

/**
 * Feedback de sincronização sempre visível nas telas com sessão: quantas ações esperam envio e, uma a uma,
 * as que não foram aplicadas — até o motorista dizer que viu.
 */
export function EstadoDaSincronizacao() {
  const { operacoes, marcarCiente, armazenamentoFalhou } = useSincronizacao();
  const aguardando = operacoes.filter(aguardandoEnvio);
  const atencao = operacoes.filter(precisaDeAtencao);

  if (aguardando.length === 0 && atencao.length === 0 && !armazenamentoFalhou) {
    return null;
  }

  return (
    <div className="sincronizacao">
      {armazenamentoFalhou ? (
        <p className="gps gps--negado" role="alert">
          O armazenamento do aparelho não respondeu.
          <span className="gps__dica">Ações novas podem não ficar guardadas sem internet. Mantenha a conexão ligada.</span>
        </p>
      ) : null}

      {aguardando.length > 0 ? (
        <p className="gps gps--falhando" role="status">
          {aguardando.length === 1
            ? '1 ação guardada no aparelho, aguardando envio'
            : `${aguardando.length} ações guardadas no aparelho, aguardando envio`}
          <span className="gps__dica">Ela segue sozinha quando a conexão voltar. Pode continuar a rota.</span>
        </p>
      ) : null}

      {atencao.map((operacao) => (
        <div key={operacao.id} className="cartao aviso" role="alert">
          <p>
            <strong>{operacao.descricao}</strong> não foi aplicada.
          </p>
          <p>{mensagemDoDesfecho(operacao)}</p>
          <button
            type="button"
            className="acao acao--secundaria"
            onClick={() => {
              void marcarCiente(operacao.id);
            }}
          >
            Entendi
          </button>
        </div>
      ))}
    </div>
  );
}

/** Aviso de que a tela mostra a cópia guardada, e de quando ela é. */
export function AvisoDeCopia({ leitura }: { readonly leitura: Leitura<unknown> | undefined }) {
  if (!leitura?.doAparelho) {
    return null;
  }

  return (
    <p className="sumario" role="status">
      Sem conexão com a operação: mostrando o que foi guardado às {formatarHora(new Date(leitura.obtidaEm).toISOString())}.
    </p>
  );
}
