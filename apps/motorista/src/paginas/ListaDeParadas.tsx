import { Link, useParams } from 'react-router';

import { Aviso, Carregando } from '../componentes/Aviso';
import { AvisoDeCopia } from '../componentes/EstadoDaSincronizacao';
import { enderecoEmLinha, formatarJanela, ROTULOS_DA_ENTREGA } from '../infra/formatos';
import { useRota } from '../offline/ProvedorDeSincronizacao';

/** Todas as paradas da rota, na ordem em que serão feitas. */
export function ListaDeParadas() {
  const { rotaId = '' } = useParams();
  const rota = useRota(rotaId);

  if (rota.consulta.isError) {
    return (
      <>
        <Aviso
          erro={rota.consulta.error}
          aoTentarDeNovo={() => {
            void rota.consulta.refetch();
          }}
        />
        <Link className="link" to="/">
          Voltar para a rota do dia
        </Link>
      </>
    );
  }

  if (!rota.dados) {
    return <Carregando texto="Carregando as paradas…" />;
  }

  return (
    <section>
      <h2>Paradas da rota {rota.dados.codigo}</h2>
      <AvisoDeCopia leitura={rota.leitura} />

      <ol className="paradas">
        {rota.dados.paradas.map((parada) => (
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
