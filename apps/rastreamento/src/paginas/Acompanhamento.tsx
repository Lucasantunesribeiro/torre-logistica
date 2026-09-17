import { useEffect, useState } from 'react';
import { useParams } from 'react-router';

import {
  consultarAcompanhamento,
  type Acompanhamento as DadosDoAcompanhamento,
  type Marco,
  type ResultadoDoAcompanhamento,
  type SituacaoDoSla,
  type StatusDaEntrega,
} from '../infra/api';

const ROTULOS_DE_STATUS: Record<StatusDaEntrega, string> = {
  Criada: 'Pedido registrado',
  Planejada: 'Pedido registrado',
  Atribuida: 'Preparando para sair',
  EmRota: 'A caminho',
  ProximaDoDestino: 'Chegando',
  Entregue: 'Entregue',
  TentativaFrustrada: 'Não foi possível entregar',
  Reagendada: 'Nova tentativa marcada',
  Cancelada: 'Cancelada',
};

const ROTULOS_DE_MARCO: Record<Marco['tipo'], string> = {
  Criada: 'Pedido registrado',
  SaiuParaRota: 'Saiu para entrega',
  ProximidadeDetectada: 'Chegando ao seu endereço',
  ChegadaRegistrada: 'Chegou ao seu endereço',
  Entregue: 'Entregue',
  TentativaFrustrada: 'Tentativa sem sucesso',
  Reagendada: 'Nova tentativa marcada',
  Cancelada: 'Entrega cancelada',
};

const ROTULOS_DE_SITUACAO: Record<SituacaoDoSla, string> = {
  Normal: 'No horário',
  Atencao: 'Pode atrasar',
  Risco: 'Risco de atraso',
  Atrasada: 'Atrasada',
};

function formatarDataHora(iso: string): string {
  return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

function formatarHora(iso: string): string {
  return new Date(iso).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
}

/**
 * Página que o link do destinatário abre.
 *
 * Mostra a encomenda, não a operação: não há motorista, veículo, rota, nem endereço completo. A
 * posição, quando aparece, é a região aproximada que a API já devolve arredondada — o texto diz a
 * grossura para a pessoa não confundir com "o entregador está nesta esquina".
 */
export function Acompanhamento() {
  const { token } = useParams<{ token: string }>();

  // O resultado carrega o token que o produziu. Isso evita limpar o estado dentro do efeito — e,
  // de quebra, faz a troca de link mostrar "carregando" sem exibir por um instante os dados do
  // link anterior.
  const [carregado, setCarregado] = useState<{
    readonly token: string;
    readonly resultado: ResultadoDoAcompanhamento;
  } | null>(null);

  useEffect(() => {
    if (token === undefined) {
      return;
    }

    const cancelamento = new AbortController();

    void consultarAcompanhamento(token, cancelamento.signal).then((obtido) => {
      if (!cancelamento.signal.aborted) {
        setCarregado({ token, resultado: obtido });
      }
    });

    return () => {
      cancelamento.abort();
    };
  }, [token]);

  const resultado: ResultadoDoAcompanhamento | null =
    token === undefined
      ? { estado: 'naoEncontrado' }
      : carregado !== null && carregado.token === token
        ? carregado.resultado
        : null;

  if (resultado === null) {
    return (
      <section>
        <h2>Acompanhamento de entrega</h2>
        <p role="status">Carregando o acompanhamento…</p>
      </section>
    );
  }

  if (resultado.estado !== 'ok') {
    return (
      <section>
        <h2>Acompanhamento de entrega</h2>
        <p>
          {resultado.estado === 'naoEncontrado'
            ? 'Este link de acompanhamento não é válido ou expirou. Peça um link novo a quem enviou sua encomenda.'
            : 'Não foi possível carregar o acompanhamento agora. Tente de novo em alguns instantes.'}
        </p>
      </section>
    );
  }

  return <Conteudo dados={resultado.dados} />;
}

function Conteudo({ dados }: { readonly dados: DadosDoAcompanhamento }) {
  return (
    <section>
      <h2>Acompanhamento de entrega</h2>

      <p className="codigo">Pedido {dados.codigo}</p>
      <p className="status">{ROTULOS_DE_STATUS[dados.status]}</p>

      <dl>
        <dt>Entrega prevista para</dt>
        <dd>
          {formatarDataHora(dados.janelaDe)} até {formatarHora(dados.janelaAte)}
        </dd>

        {dados.chegadaPrevistaEm !== null && (
          <>
            <dt>Chegada estimada</dt>
            <dd>
              {formatarDataHora(dados.chegadaPrevistaEm)}
              {dados.situacao !== null && ` — ${ROTULOS_DE_SITUACAO[dados.situacao]}`}
            </dd>
          </>
        )}

        <dt>Destino</dt>
        <dd>
          {dados.destino.bairro}, {dados.destino.cidade} — {dados.destino.uf}
        </dd>
      </dl>

      {dados.posicao !== null && (
        <p className="posicao">
          Entrega na região do seu bairro às {formatarHora(dados.posicao.atualizadaEm)}. A posição é
          aproximada, com cerca de {(dados.posicao.precisaoAproximadaEmMetros / 1000).toFixed(1)} km
          de margem.
        </p>
      )}

      <h3>Andamento</h3>
      <ol className="marcos">
        {dados.marcos.map((marco) => (
          <li key={`${marco.tipo}-${marco.ocorridoEm}`}>
            <span className="quando">{formatarDataHora(marco.ocorridoEm)}</span>{' '}
            <span className="oque">{ROTULOS_DE_MARCO[marco.tipo]}</span>
          </li>
        ))}
      </ol>

      {dados.comprovante !== null && (
        <>
          <h3>Comprovante</h3>
          <p>
            Recebido por {dados.comprovante.recebidoPor} em{' '}
            {formatarDataHora(dados.comprovante.registradoEm)}.
          </p>
          <ul className="arquivos">
            {dados.comprovante.arquivos.map((arquivo) => (
              <li key={arquivo.url}>
                <a href={arquivo.url} rel="noreferrer noopener" target="_blank">
                  {arquivo.tipo === 'Foto' ? 'Ver foto da entrega' : 'Ver assinatura'}
                </a>
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  );
}
