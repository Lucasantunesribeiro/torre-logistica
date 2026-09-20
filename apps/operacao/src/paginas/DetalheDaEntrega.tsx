import { useMutation, useQuery } from '@tanstack/react-query';
import { useParams } from 'react-router';

import { Estado, Selo, instante } from '../componentes/Estado';
import {
  ErroDaApi,
  emitirLinkDeRastreamento,
  listarEventosDaEntrega,
  listarOcorrenciasDaEntrega,
  obterComprovante,
  obterEntrega,
  obterPrevisao,
  type SituacaoDoSla,
} from '../infra/api';

const tomPorSituacao: Record<SituacaoDoSla, 'bom' | 'atencao' | 'ruim'> = {
  Normal: 'bom',
  Atencao: 'atencao',
  Risco: 'atencao',
  Atrasada: 'ruim',
};

/**
 * Tudo sobre uma entrega numa tela: estado, previsão, timeline, ocorrências e prova.
 *
 * As consultas são independentes de propósito. Comprovante e previsão podem não existir ainda, e uma
 * falha em qualquer delas não pode esconder o resto — quem está atendendo um cliente ao telefone precisa
 * do que já se sabe, não de uma tela em branco.
 */
export function DetalheDaEntrega() {
  const { id = '' } = useParams<{ id: string }>();

  const entrega = useQuery({ queryKey: ['entregas', id], queryFn: () => obterEntrega(id) });
  const eventos = useQuery({ queryKey: ['entregas', id, 'eventos'], queryFn: () => listarEventosDaEntrega(id) });
  const previsao = useQuery({ queryKey: ['entregas', id, 'previsao'], queryFn: () => obterPrevisao(id) });
  const ocorrencias = useQuery({
    queryKey: ['ocorrencias', id],
    queryFn: () => listarOcorrenciasDaEntrega(id),
  });

  const comprovante = useQuery({
    queryKey: ['entregas', id, 'comprovante'],
    queryFn: () => obterComprovante(id),
    retry: false,
  });

  const link = useMutation({ mutationFn: () => emitirLinkDeRastreamento(id) });

  const semComprovante = comprovante.error instanceof ErroDaApi && comprovante.error.status === 404;

  return (
    <section>
      <Estado carregando={entrega.isPending} erro={entrega.error}>
        <h2>{entrega.data?.codigo}</h2>
        <p className="sumario">
          <Selo tom="neutro">{entrega.data?.status}</Selo> · {entrega.data?.clienteNome} →{' '}
          {entrega.data?.destinatarioNome}
        </p>

        <div className="colunas">
          <div>
            <h3>Entrega</h3>
            <dl className="dados">
              <dt>Destino</dt>
              <dd>
                {entrega.data?.endereco.logradouro}, {entrega.data?.endereco.numero} —{' '}
                {entrega.data?.endereco.bairro}, {entrega.data?.endereco.cidade}/{entrega.data?.endereco.uf}
              </dd>

              <dt>Janela prometida</dt>
              <dd>
                {instante(entrega.data?.janelaPrometida.de ?? null)} até{' '}
                {instante(entrega.data?.janelaPrometida.ate ?? null)}
              </dd>

              <dt>Saiu para rota</dt>
              <dd>{instante(entrega.data?.execucao.saiuParaRotaEm ?? null)}</dd>

              <dt>Entregue em</dt>
              <dd>{instante(entrega.data?.execucao.entregueEm ?? null)}</dd>

              <dt>Tentativas sem sucesso</dt>
              <dd>{entrega.data?.execucao.tentativasFrustradas ?? 0}</dd>
            </dl>
          </div>

          <div>
            <h3>Previsão</h3>
            <Estado carregando={previsao.isPending} erro={previsao.error}>
              {previsao.data?.disponivel === true ? (
                <dl className="dados">
                  <dt>Situação</dt>
                  <dd>
                    {previsao.data.situacao === null ? (
                      '—'
                    ) : (
                      <Selo tom={tomPorSituacao[previsao.data.situacao]}>{previsao.data.situacao}</Selo>
                    )}
                  </dd>

                  <dt>Chegada prevista</dt>
                  <dd>{instante(previsao.data.chegadaPrevistaEm)}</dd>

                  <dt>Por quê</dt>
                  <dd>{previsao.data.explicacao ?? '—'}</dd>
                </dl>
              ) : (
                <p className="estado">Ainda não há previsão para esta entrega.</p>
              )}
            </Estado>
          </div>
        </div>

        <h3>Acompanhamento do destinatário</h3>
        <p className="sumario">
          O link é mostrado uma única vez. Emitir outro invalida o anterior.
        </p>
        <div className="acoes">
          <button type="button" disabled={link.isPending} onClick={() => link.mutate()}>
            {link.isPending ? 'Emitindo…' : 'Emitir link de rastreamento'}
          </button>
        </div>
        {link.data !== undefined && (
          <p className="destaque">
            <code>/e/{link.data.token}</code> — válido até {instante(link.data.expiraEm)}
          </p>
        )}
        {link.isError && (
          <p className="formulario__erro" role="alert">
            Não foi possível emitir o link.
          </p>
        )}

        <h3>Timeline</h3>
        <Estado carregando={eventos.isPending} erro={eventos.error} vazio={eventos.data?.length === 0}>
          <ol className="linha-do-tempo">
            {eventos.data?.map((evento) => (
              <li key={evento.sequencia}>
                <span className="quando">{instante(evento.ocorridoEm)}</span>
                <span className="oque">{evento.tipo}</span>
                <span className="sumario">→ {evento.statusResultante}</span>
              </li>
            ))}
          </ol>
        </Estado>

        <h3>Ocorrências</h3>
        <Estado
          carregando={ocorrencias.isPending}
          erro={ocorrencias.error}
          vazio={ocorrencias.data?.length === 0}
          mensagemVazio="Nenhuma ocorrência nesta entrega."
        >
          <ul className="lista">
            {ocorrencias.data?.map((ocorrencia) => (
              <li key={ocorrencia.id}>
                <Selo tom={ocorrencia.severidade === 'Critica' ? 'ruim' : 'atencao'}>{ocorrencia.severidade}</Selo>{' '}
                {ocorrencia.tipo} — {instante(ocorrencia.ocorridaEm)}
                {ocorrencia.observacao === null ? null : <span className="sumario"> · {ocorrencia.observacao}</span>}
              </li>
            ))}
          </ul>
        </Estado>

        <h3>Prova de entrega</h3>
        {semComprovante ? (
          <p className="estado">Esta entrega ainda não tem comprovante.</p>
        ) : (
          <Estado carregando={comprovante.isPending} erro={comprovante.error}>
            <dl className="dados">
              <dt>Recebido por</dt>
              <dd>{comprovante.data?.recebidoPor}</dd>

              <dt>Registrado em</dt>
              <dd>{instante(comprovante.data?.registradoEm ?? null)}</dd>
            </dl>

            <ul className="lista">
              {comprovante.data?.arquivos.map((arquivo) => (
                <li key={arquivo.url}>
                  <a href={arquivo.url} rel="noreferrer noopener" target="_blank">
                    {arquivo.tipo === 'Foto' ? 'Ver foto' : 'Ver assinatura'}
                  </a>
                  <span className="sumario"> · link expira {instante(arquivo.urlExpiraEm)}</span>
                </li>
              ))}
            </ul>
          </Estado>
        )}
      </Estado>
    </section>
  );
}
