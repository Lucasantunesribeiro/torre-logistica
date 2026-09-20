import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';

import { Estado, Selo, instante } from '../componentes/Estado';
import { listarRotas } from '../infra/api';

const tomPorStatus = {
  EmMontagem: 'neutro',
  Planejada: 'neutro',
  EmAndamento: 'atencao',
  Concluida: 'bom',
  Cancelada: 'ruim',
} as const;

/**
 * Rotas do dia, com o andamento das paradas.
 *
 * A contagem de paradas pendentes fica na própria linha: é o número que decide se alguém precisa olhar
 * aquela rota agora, e obrigá-lo a abrir o detalhe para descobrir isso desperdiçaria o clique.
 */
export function Rotas() {
  const [pagina, setPagina] = useState(1);

  const consulta = useQuery({
    queryKey: ['rotas', pagina],
    queryFn: () => listarRotas(pagina),
  });

  return (
    <section>
      <h2>Rotas</h2>

      <Estado
        carregando={consulta.isPending}
        erro={consulta.error}
        vazio={consulta.data?.itens.length === 0}
        mensagemVazio="Nenhuma rota cadastrada."
      >
        <table>
          <caption className="sumario">{consulta.data?.total ?? 0} rota(s)</caption>
          <thead>
            <tr>
              <th scope="col">Código</th>
              <th scope="col">Data</th>
              <th scope="col">Status</th>
              <th scope="col">Motorista</th>
              <th scope="col">Paradas</th>
              <th scope="col">Saída planejada</th>
            </tr>
          </thead>
          <tbody>
            {consulta.data?.itens.map((rota) => {
              const pendentes = rota.paradas.filter(
                (parada) => parada.status !== 'Entregue' && parada.status !== 'Cancelada',
              ).length;

              return (
                <tr key={rota.id}>
                  <td>{rota.codigo}</td>
                  <td>{rota.data}</td>
                  <td>
                    <Selo tom={tomPorStatus[rota.status]}>{rota.status}</Selo>
                  </td>
                  <td>{rota.motorista?.nome ?? '—'}</td>
                  <td>
                    {pendentes} de {rota.paradas.length} pendente(s)
                  </td>
                  <td>{instante(rota.saidaPlanejada)}</td>
                </tr>
              );
            })}
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
