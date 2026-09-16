import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';

import { Aviso, Carregando } from '../componentes/Aviso';
import { MENSAGEM_DE_FALHA_AO_GUARDAR } from '../componentes/EstadoDaSincronizacao';
import { MOTIVO_QUE_EXIGE_DESCRICAO, MOTIVOS_DE_TENTATIVA, TAMANHO_MAXIMO_DA_DESCRICAO } from '../infra/api';
import type { MotivoDeTentativa } from '../infra/api';
import { ROTULOS_DA_ENTREGA, ROTULOS_DO_MOTIVO } from '../infra/formatos';
import { useEntrega, useRegistrarAcao } from '../offline/ProvedorDeSincronizacao';

/**
 * Ocorrência que impediu a entrega, com motivo tipado — nunca texto livre como única informação. A descrição
 * é complemento; só é exigida em "Outro motivo", quando a lista não diz o que houve. Registra a tentativa sem
 * sucesso na timeline da entrega, com ou sem internet.
 */
export function RegistrarOcorrencia() {
  const { entregaId = '' } = useParams();
  const navegar = useNavigate();
  const [motivo, setMotivo] = useState<MotivoDeTentativa | null>(null);
  const [observacao, setObservacao] = useState('');

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
  const aceita = dados.status === 'EmRota' || dados.status === 'ProximaDoDestino';
  const exigeDescricao = motivo === MOTIVO_QUE_EXIGE_DESCRICAO;
  const descricaoPreenchida = observacao.trim().length > 0;
  const podeRegistrar = motivo !== null && (!exigeDescricao || descricaoPreenchida) && !acao.registrando;

  return (
    <section>
      <h2>Registrar ocorrência</h2>
      <p className="sumario">
        {dados.destinatario.nome} · {dados.codigo}
      </p>

      {aceita ? (
        <form
          className="cartao"
          onSubmit={(evento) => {
            evento.preventDefault();
            if (motivo && (!exigeDescricao || descricaoPreenchida)) {
              void acao.executar(
                {
                  tipo: 'RegistrarTentativaFrustrada',
                  alvoId: dados.id,
                  motivo,
                  observacao: observacao.trim() || undefined,
                  descricao: `Tentativa sem sucesso na entrega de ${dados.destinatario.nome}`,
                },
                () => {
                  void navegar('/', { replace: true });
                },
              );
            }
          }}
        >
          <fieldset className="motivos">
            <legend>O que impediu a entrega?</legend>
            {MOTIVOS_DE_TENTATIVA.map((opcao) => (
              <label key={opcao}>
                <input
                  type="radio"
                  name="motivo"
                  value={opcao}
                  checked={motivo === opcao}
                  onChange={() => {
                    setMotivo(opcao);
                  }}
                />
                {ROTULOS_DO_MOTIVO[opcao]}
              </label>
            ))}
          </fieldset>

          <label className="campo" htmlFor="observacao">
            {exigeDescricao ? 'Descreva o que aconteceu' : 'Quer acrescentar alguma coisa? (opcional)'}
          </label>
          <textarea
            id="observacao"
            name="observacao"
            rows={3}
            maxLength={TAMANHO_MAXIMO_DA_DESCRICAO}
            value={observacao}
            onChange={(evento) => {
              setObservacao(evento.target.value);
            }}
          />

          <button type="submit" className="acao acao--perigo" disabled={!podeRegistrar}>
            {acao.registrando ? 'Registrando…' : 'Registrar tentativa sem sucesso'}
          </button>

          {acao.falhou ? (
            <p className="erro" role="alert">
              {MENSAGEM_DE_FALHA_AO_GUARDAR}
            </p>
          ) : null}
        </form>
      ) : (
        <div className="cartao">
          <p>Esta entrega não aceita mais ocorrência: {ROTULOS_DA_ENTREGA[dados.status]}.</p>
        </div>
      )}

      <Link className="link" to={`/entregas/${entregaId}`}>
        Voltar para a entrega
      </Link>
    </section>
  );
}
