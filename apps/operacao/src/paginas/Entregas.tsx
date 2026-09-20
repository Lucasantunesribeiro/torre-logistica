import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router';

import { Estado, Selo, instante } from '../componentes/Estado';
import { STATUS_DA_ENTREGA, listarEntregas, type StatusDaEntrega } from '../infra/api';

const tomPorStatus: Record<StatusDaEntrega, 'bom' | 'atencao' | 'ruim' | 'neutro'> = {
  Criada: 'neutro',
  Planejada: 'neutro',
  Atribuida: 'neutro',
  EmRota: 'atencao',
  ProximaDoDestino: 'atencao',
  Entregue: 'bom',
  TentativaFrustrada: 'ruim',
  Reagendada: 'atencao',
  Cancelada: 'ruim',
};

/**
 * Lista de entregas com filtro por status e busca por código.
 *
 * Tabela densa de propósito: quem opera compara linhas, e cartões separados obrigariam a rolar para ver
 * o que cabe numa tela só.
 */
export function Entregas() {
  const [status, setStatus] = useState<StatusDaEntrega | ''>('');
  const [codigo, setCodigo] = useState('');
  const [pagina, setPagina] = useState(1);

  const consulta = useQuery({
    queryKey: ['entregas', { status, codigo, pagina }],
    queryFn: () => listarEntregas({ status, codigo, pagina }),
  });

  return (
    <section>
      <h2>Entregas</h2>

      <div className="filtros">
        <label>
          Status
          <select
            value={status}
            onChange={(evento) => {
              setStatus(evento.target.value as StatusDaEntrega | '');
              setPagina(1);
            }}
          >
            <option value="">Todos</option>
            {STATUS_DA_ENTREGA.map((valor) => (
              <option key={valor} value={valor}>
                {valor}
              </option>
            ))}
          </select>
        </label>

        <label>
          Código
          <input
            type="search"
            value={codigo}
            placeholder="ENT-2026-"
            onChange={(evento) => {
              setCodigo(evento.target.value);
              setPagina(1);
            }}
          />
        </label>
      </div>

      <Estado
        carregando={consulta.isPending}
        erro={consulta.error}
        vazio={consulta.data?.itens.length === 0}
        mensagemVazio="Nenhuma entrega com esse filtro."
      >
        <table>
          <caption className="sumario">{consulta.data?.total ?? 0} entrega(s)</caption>
          <thead>
            <tr>
              <th scope="col">Código</th>
              <th scope="col">Status</th>
              <th scope="col">Destinatário</th>
              <th scope="col">Destino</th>
              <th scope="col">Janela</th>
            </tr>
          </thead>
          <tbody>
            {consulta.data?.itens.map((entrega) => (
              <tr key={entrega.id}>
                <td>
                  <Link to={`/entregas/${entrega.id}`}>{entrega.codigo}</Link>
                </td>
                <td>
                  <Selo tom={tomPorStatus[entrega.status]}>{entrega.status}</Selo>
                </td>
                <td>{entrega.destinatarioNome}</td>
                <td>
                  {entrega.endereco.bairro}, {entrega.endereco.cidade}
                </td>
                <td>{instante(entrega.janelaPrometida.de)}</td>
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
