import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';

import { Aviso, Carregando } from '../componentes/Aviso';
import { AvisoDeCopia, MENSAGEM_DE_FALHA_AO_GUARDAR } from '../componentes/EstadoDaSincronizacao';
import type { Entrega } from '../infra/api';
import {
  enderecoEmLinha,
  formatarHora,
  formatarJanela,
  linkDeNavegacao,
  ROTULOS_DA_ENTREGA,
  ROTULOS_DO_MOTIVO,
} from '../infra/formatos';
import { useEntrega, useRegistrarAcao } from '../offline/ProvedorDeSincronizacao';

/**
 * Tudo o que o motorista precisa para fazer a entrega, e as ações do momento — grandes, uma de cada vez.
 * Concluir pede confirmação: é a ação que não se desfaz. As ações valem com ou sem internet: ficam guardadas
 * no aparelho e seguem para a operação quando houver conexão.
 */
export function DetalheDaEntrega() {
  const { entregaId = '' } = useParams();
  const navegar = useNavigate();
  const [confirmando, setConfirmando] = useState(false);

  const entrega = useEntrega(entregaId);
  const acao = useRegistrarAcao();

  if (entrega.consulta.isError) {
    return (
      <>
        <Aviso
          erro={entrega.consulta.error}
          aoTentarDeNovo={() => {
            void entrega.consulta.refetch();
          }}
        />
        <Link className="link" to="/">
          Voltar para a rota do dia
        </Link>
      </>
    );
  }

  if (!entrega.dados) {
    return <Carregando texto="Carregando a entrega…" />;
  }

  const dados = entrega.dados;
  const emExecucao = dados.status === 'EmRota' || dados.status === 'ProximaDoDestino';

  return (
    <section>
      <h2>{dados.destinatario.nome}</h2>
      <p className="sumario">
        {dados.codigo}
        {dados.sequencia !== null ? ` · parada ${dados.sequencia}` : ''} ·{' '}
        <span className={`etiqueta etiqueta--${dados.status}`}>{ROTULOS_DA_ENTREGA[dados.status]}</span>
      </p>
      <AvisoDeCopia leitura={entrega.leitura} />

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
            registrando={acao.registrando}
            aoRegistrarChegada={() => {
              void acao.executar({ tipo: 'RegistrarChegada', alvoId: dados.id, descricao: `Chegada à entrega de ${dados.destinatario.nome}` });
            }}
            aoConcluir={() => {
              void acao.executar(
                { tipo: 'ConcluirEntrega', alvoId: dados.id, descricao: `Conclusão da entrega de ${dados.destinatario.nome}` },
                () => {
                  void navegar('/', { replace: true });
                },
              );
            }}
          />
        ) : (
          <p>
            {dados.status === 'Atribuida'
              ? 'Inicie a rota para registrar esta entrega.'
              : `Resultado registrado: ${ROTULOS_DA_ENTREGA[dados.status]}.`}
          </p>
        )}

        {acao.falhou ? (
          <p className="erro" role="alert">
            {MENSAGEM_DE_FALHA_AO_GUARDAR}
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
  readonly registrando: boolean;
  readonly aoRegistrarChegada: () => void;
  readonly aoConcluir: () => void;
}) {
  const { entrega } = props;

  if (props.confirmando) {
    return (
      <>
        <p>Confirma que a entrega para {entrega.destinatario.nome} foi feita?</p>
        <button type="button" className="acao acao--primaria" disabled={props.registrando} onClick={props.aoConcluir}>
          {props.registrando ? 'Registrando…' : 'Confirmar entrega concluída'}
        </button>
        <button
          type="button"
          className="acao acao--secundaria"
          disabled={props.registrando}
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
        <button type="button" className="acao acao--secundaria" disabled={props.registrando} onClick={props.aoRegistrarChegada}>
          {props.registrando ? 'Registrando…' : 'Cheguei ao destino'}
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
