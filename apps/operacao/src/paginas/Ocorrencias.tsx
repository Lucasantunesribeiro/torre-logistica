import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router';

import { Estado, Selo, instante, tomDaSeveridade } from '../componentes/Estado';
import { listarOcorrencias } from '../infra/api';

/** Ocorrências registradas na última milha, das mais recentes para as mais antigas. */
export function Ocorrencias() {
  const [pagina, setPagina] = useState(1);

  const consulta = useQuery({
    queryKey: ['ocorrencias', pagina],
    queryFn: () => listarOcorrencias(pagina),
  });

  return (
    <section>
      <h2>Ocorrências</h2>

      <Estado
        carregando={consulta.isPending}
        erro={consulta.error}
        vazio={consulta.data?.itens.length === 0}
        mensagemVazio="Nenhuma ocorrência registrada."
      >
        <table>
          <caption className="sumario">{consulta.data?.total ?? 0} ocorrência(s)</caption>
          <thead>
            <tr>
              <th scope="col">Severidade</th>
              <th scope="col">Tipo</th>
              <th scope="col">Entrega</th>
              <th scope="col">Motorista</th>
              <th scope="col">Observação</th>
              <th scope="col">Ocorrida em</th>
            </tr>
          </thead>
          <tbody>
            {consulta.data?.itens.map((ocorrencia) => (
              <tr key={ocorrencia.id}>
                <td>
                  <Selo tom={tomDaSeveridade(ocorrencia.severidade)}>{ocorrencia.severidade}</Selo>
                </td>
                <td>{ocorrencia.tipo}</td>
                <td>
                  <Link to={`/entregas/${ocorrencia.entregaId}`}>{ocorrencia.codigoDaEntrega}</Link>
                </td>
                <td>{ocorrencia.nomeDoMotorista ?? '—'}</td>
                <td>{ocorrencia.observacao ?? '—'}</td>
                <td>{instante(ocorrencia.ocorridaEm)}</td>
              </tr>
            ))}
          </tbody>
        </table>

        <div className="paginacao">
          <button type="button" disabled={pagina <= 1} onClick={() => setPagina((atual) => atual - 1)}>
            Anterior
          </button>
          <span>página {pagina}</span>
          <button
            type="button"
            disabled={(consulta.data?.itens.length ?? 0) < (consulta.data?.tamanhoDaPagina ?? 25)}
            onClick={() => setPagina((atual) => atual + 1)}
          >
            Próxima
          </button>
        </div>
      </Estado>
    </section>
  );
}
