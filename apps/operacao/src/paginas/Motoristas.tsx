import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router';

import { Estado, Selo } from '../componentes/Estado';
import { listarMotoristas } from '../infra/api';

/** Motoristas da organização. O detalhe mostra a última posição conhecida. */
export function Motoristas() {
  const [pagina, setPagina] = useState(1);

  const consulta = useQuery({
    queryKey: ['motoristas', pagina],
    queryFn: () => listarMotoristas(pagina),
  });

  return (
    <section>
      <h2>Motoristas</h2>

      <Estado
        carregando={consulta.isPending}
        erro={consulta.error}
        vazio={consulta.data?.itens.length === 0}
        mensagemVazio="Nenhum motorista cadastrado."
      >
        <table>
          <caption className="sumario">{consulta.data?.total ?? 0} motorista(s)</caption>
          <thead>
            <tr>
              <th scope="col">Nome</th>
              <th scope="col">Telefone</th>
              <th scope="col">Situação</th>
            </tr>
          </thead>
          <tbody>
            {consulta.data?.itens.map((motorista) => (
              <tr key={motorista.id}>
                <td>
                  <Link to={`/motoristas/${motorista.id}`}>{motorista.nome}</Link>
                </td>
                <td>{motorista.telefone ?? '—'}</td>
                <td>
                  <Selo tom={motorista.ativo ? 'bom' : 'neutro'}>{motorista.ativo ? 'Ativo' : 'Inativo'}</Selo>
                </td>
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
