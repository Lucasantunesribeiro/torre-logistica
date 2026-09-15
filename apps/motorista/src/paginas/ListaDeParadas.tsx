import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from 'react-router';

import { Aviso, Carregando } from '../componentes/Aviso';
import { apiDoMotorista } from '../infra/api';
import { enderecoEmLinha, formatarJanela, ROTULOS_DA_ENTREGA } from '../infra/formatos';

/** Todas as paradas da rota, na ordem em que serão feitas. */
export function ListaDeParadas() {
  const { rotaId = '' } = useParams();
  const rota = useQuery({ queryKey: ['rota', rotaId], queryFn: () => apiDoMotorista.obterRota(rotaId) });

  if (rota.isPending) {
    return <Carregando texto="Carregando as paradas…" />;
  }

  if (rota.isError) {
    return (
      <>
        <Aviso
          erro={rota.error}
          aoTentarDeNovo={() => {
            void rota.refetch();
          }}
        />
        <Link className="link" to="/">
          Voltar para a rota do dia
        </Link>
      </>
    );
  }

  return (
    <section>
      <h2>Paradas da rota {rota.data.codigo}</h2>

      <ol className="paradas">
        {rota.data.paradas.map((parada) => (
          <li key={parada.entregaId}>
            <Link to={`/entregas/${parada.entregaId}`}>
              <span className="paradas__numero" aria-hidden="true">
                {parada.sequencia}
              </span>
              <strong>{parada.destinatarioNome}</strong>
              <span>{enderecoEmLinha(parada.endereco)}</span>
              <span className="sumario-curto">
                {ROTULOS_DA_ENTREGA[parada.status]} · janela {formatarJanela(parada.janelaPrometida.de, parada.janelaPrometida.ate)}
              </span>
            </Link>
          </li>
        ))}
      </ol>

      <Link className="link" to="/">
        Voltar para a rota do dia
      </Link>
    </section>
  );
}
