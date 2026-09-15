import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router';

import { Aviso, Carregando } from '../componentes/Aviso';
import { apiDoMotorista, mensagemDeErro } from '../infra/api';
import type { Entrega } from '../infra/api';
import {
  enderecoEmLinha,
  formatarHora,
  formatarJanela,
  linkDeNavegacao,
  ROTULOS_DA_ENTREGA,
  ROTULOS_DO_MOTIVO,
} from '../infra/formatos';

/**
 * Tudo o que o motorista precisa para fazer a entrega, e as ações do momento — grandes, uma de cada vez.
 * Concluir pede confirmação: é a ação que não se desfaz.
 */
export function DetalheDaEntrega() {
  const { entregaId = '' } = useParams();
  const navegar = useNavigate();
  const cliente = useQueryClient();
  const [confirmando, setConfirmando] = useState(false);

  const entrega = useQuery({ queryKey: ['entrega', entregaId], queryFn: () => apiDoMotorista.obterEntrega(entregaId) });

  const atualizar = () => Promise.all([
    cliente.invalidateQueries({ queryKey: ['entrega', entregaId] }),
    cliente.invalidateQueries({ queryKey: ['rota'] }),
    cliente.invalidateQueries({ queryKey: ['rotas'] }),
  ]);

  const chegada = useMutation({ mutationFn: () => apiDoMotorista.registrarChegada(entregaId), onSettled: atualizar });
  const conclusao = useMutation({
    mutationFn: () => apiDoMotorista.concluirEntrega(entregaId),
    onSuccess: () => {
      void navegar('/', { replace: true });
    },
    onSettled: atualizar,
  });

  if (entrega.isPending) {
    return <Carregando texto="Carregando a entrega…" />;
  }

  if (entrega.isError) {
    return (
      <>
        <Aviso
          erro={entrega.error}
          aoTentarDeNovo={() => {
            void entrega.refetch();
          }}
        />
        <Link className="link" to="/">
          Voltar para a rota do dia
        </Link>
      </>
    );
  }

  const dados = entrega.data;
  const emExecucao = dados.status === 'EmRota' || dados.status === 'ProximaDoDestino';
  const erro = chegada.error ?? conclusao.error;

  return (
    <section>
      <h2>{dados.destinatario.nome}</h2>
      <p className="sumario">
        {dados.codigo}
        {dados.sequencia !== null ? ` · parada ${dados.sequencia}` : ''} ·{' '}
        <span className={`etiqueta etiqueta--${dados.status}`}>{ROTULOS_DA_ENTREGA[dados.status]}</span>
      </p>

      <div className="cartao">
        <dl className="dados">
          <dt>Endereço</dt>
          <dd>{enderecoEmLinha(dados.endereco)}</dd>
          <dt>Janela prometida</dt>
          <dd>{formatarJanela(dados.janelaPrometida.de, dados.janelaPrometida.ate)}</dd>
          {dados.destinatario.instrucoesDeEntrega ? (
            <>
              <dt>Instruções</dt>
              <dd>{dados.destinatario.instrucoesDeEntrega}</dd>
            </>
          ) : null}
          {dados.observacoes ? (
            <>
              <dt>Observações da operação</dt>
              <dd>{dados.observacoes}</dd>
            </>
          ) : null}
        </dl>

        <a className="acao acao--secundaria" href={linkDeNavegacao(dados)} target="_blank" rel="noreferrer">
          Abrir no mapa
        </a>
        {dados.destinatario.telefone ? (
          <a className="acao acao--secundaria" href={`tel:${dados.destinatario.telefone.replace(/[^\d+]/g, '')}`}>
            Ligar para {dados.destinatario.nome}
          </a>
        ) : null}
      </div>

      <div className="cartao">
        {emExecucao ? (
          <AcoesDaEntrega
            entrega={dados}
            confirmando={confirmando}
            aoPedirConfirmacao={setConfirmando}
            chegando={chegada.isPending}
            concluindo={conclusao.isPending}
            aoRegistrarChegada={() => {
              chegada.mutate();
            }}
            aoConcluir={() => {
              conclusao.mutate();
            }}
          />
        ) : (
          <p>
            {dados.status === 'Atribuida'
              ? 'Inicie a rota para registrar esta entrega.'
              : `Resultado registrado: ${ROTULOS_DA_ENTREGA[dados.status]}.`}
          </p>
        )}

        {erro ? (
          <p className="erro" role="alert">
            {mensagemDeErro(erro)}
          </p>
        ) : null}
      </div>

      <div className="cartao">
        <h3>Ocorrências</h3>
        {dados.execucao.tentativasFrustradas === 0 ? (
          <p className="sumario">Nenhuma ocorrência registrada.</p>
        ) : (
          <p>
            {dados.execucao.tentativasFrustradas} tentativa(s) sem sucesso
            {dados.execucao.motivoDaUltimaTentativa ? ` — último motivo: ${ROTULOS_DO_MOTIVO[dados.execucao.motivoDaUltimaTentativa]}` : ''}
            {dados.execucao.ultimaTentativaFrustradaEm ? `, às ${formatarHora(dados.execucao.ultimaTentativaFrustradaEm)}` : ''}.
          </p>
        )}
      </div>

      <Link className="link" to="/">
        Voltar para a rota do dia
      </Link>
    </section>
  );
}

function AcoesDaEntrega(props: {
  readonly entrega: Entrega;
  readonly confirmando: boolean;
  readonly aoPedirConfirmacao: (valor: boolean) => void;
  readonly chegando: boolean;
  readonly concluindo: boolean;
  readonly aoRegistrarChegada: () => void;
  readonly aoConcluir: () => void;
}) {
  const { entrega } = props;

  if (props.confirmando) {
    return (
      <>
        <p>Confirma que a entrega para {entrega.destinatario.nome} foi feita?</p>
        <button type="button" className="acao acao--primaria" disabled={props.concluindo} onClick={props.aoConcluir}>
          {props.concluindo ? 'Registrando…' : 'Confirmar entrega concluída'}
        </button>
        <button
          type="button"
          className="acao acao--secundaria"
          disabled={props.concluindo}
          onClick={() => {
            props.aoPedirConfirmacao(false);
          }}
        >
          Voltar
        </button>
      </>
    );
  }

  return (
    <>
      {entrega.status === 'EmRota' ? (
        <button type="button" className="acao acao--secundaria" disabled={props.chegando} onClick={props.aoRegistrarChegada}>
          {props.chegando ? 'Registrando…' : 'Cheguei ao destino'}
        </button>
      ) : null}

      <button
        type="button"
        className="acao acao--primaria"
        onClick={() => {
          props.aoPedirConfirmacao(true);
        }}
      >
        Entrega concluída
      </button>

      <Link className="acao acao--perigo" to={`/entregas/${entrega.id}/ocorrencia`}>
        Não foi possível entregar
      </Link>
    </>
  );
}
