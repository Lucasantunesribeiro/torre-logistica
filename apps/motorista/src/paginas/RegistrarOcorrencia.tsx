import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router';

import { Aviso, Carregando } from '../componentes/Aviso';
import { apiDoMotorista, mensagemDeErro, MOTIVOS_DE_TENTATIVA } from '../infra/api';
import type { MotivoDeTentativa } from '../infra/api';
import { ROTULOS_DA_ENTREGA, ROTULOS_DO_MOTIVO } from '../infra/formatos';

/**
 * Ocorrência que impediu a entrega, com motivo tipado — nunca texto livre como única informação. Registra a
 * tentativa sem sucesso na timeline da entrega.
 */
export function RegistrarOcorrencia() {
  const { entregaId = '' } = useParams();
  const navegar = useNavigate();
  const cliente = useQueryClient();
  const [motivo, setMotivo] = useState<MotivoDeTentativa | null>(null);

  const entrega = useQuery({ queryKey: ['entrega', entregaId], queryFn: () => apiDoMotorista.obterEntrega(entregaId) });

  const registro = useMutation({
    mutationFn: (escolhido: MotivoDeTentativa) => apiDoMotorista.registrarTentativaFrustrada(entregaId, escolhido),
    onSuccess: () => {
      void navegar('/', { replace: true });
    },
    onSettled: () =>
      Promise.all([
        cliente.invalidateQueries({ queryKey: ['entrega', entregaId] }),
        cliente.invalidateQueries({ queryKey: ['rota'] }),
        cliente.invalidateQueries({ queryKey: ['rotas'] }),
      ]),
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

  const aceita = entrega.data.status === 'EmRota' || entrega.data.status === 'ProximaDoDestino';

  return (
    <section>
      <h2>Registrar ocorrência</h2>
      <p className="sumario">
        {entrega.data.destinatario.nome} · {entrega.data.codigo}
      </p>

      {aceita ? (
        <form
          className="cartao"
          onSubmit={(evento) => {
            evento.preventDefault();
            if (motivo) {
              registro.mutate(motivo);
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

          <button type="submit" className="acao acao--perigo" disabled={!motivo || registro.isPending}>
            {registro.isPending ? 'Registrando…' : 'Registrar tentativa sem sucesso'}
          </button>

          {registro.error ? (
            <p className="erro" role="alert">
              {mensagemDeErro(registro.error)}
            </p>
          ) : null}
        </form>
      ) : (
        <div className="cartao">
          <p>Esta entrega não aceita mais ocorrência: {ROTULOS_DA_ENTREGA[entrega.data.status]}.</p>
        </div>
      )}

      <Link className="link" to={`/entregas/${entregaId}`}>
        Voltar para a entrega
      </Link>
    </section>
  );
}
