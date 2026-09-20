import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router';

import { Estado, Selo, instante } from '../componentes/Estado';
import { listarAlertas, resolverAlerta } from '../infra/api';

const tomPorSeveridade = {
  Informativa: 'neutro',
  Atencao: 'atencao',
  Critica: 'ruim',
} as const;

/**
 * Alertas operacionais, abertos primeiro.
 *
 * Resolver pede uma observação: alerta fechado sem motivo vira ruído na próxima vez que alguém perguntar
 * por que aquilo aconteceu.
 */
export function Alertas() {
  const clienteDeConsultas = useQueryClient();
  const [estadoFiltrado, setEstadoFiltrado] = useState<'Aberto' | 'Resolvido'>('Aberto');
  const [emResolucao, setEmResolucao] = useState<string | null>(null);
  const [observacao, setObservacao] = useState('');

  const consulta = useQuery({
    queryKey: ['alertas', estadoFiltrado],
    queryFn: () => listarAlertas(estadoFiltrado),
  });

  const resolucao = useMutation({
    mutationFn: ({ id, texto }: { id: string; texto: string }) => resolverAlerta(id, texto),
    onSuccess: async () => {
      setEmResolucao(null);
      setObservacao('');
      await clienteDeConsultas.invalidateQueries({ queryKey: ['alertas'] });
    },
  });

  return (
    <section>
      <h2>Alertas</h2>

      <div className="filtros">
        <label>
          Estado
          <select
            value={estadoFiltrado}
            onChange={(evento) => setEstadoFiltrado(evento.target.value as 'Aberto' | 'Resolvido')}
          >
            <option value="Aberto">Abertos</option>
            <option value="Resolvido">Resolvidos</option>
          </select>
        </label>
      </div>

      <Estado
        carregando={consulta.isPending}
        erro={consulta.error}
        vazio={consulta.data?.itens.length === 0}
        mensagemVazio={estadoFiltrado === 'Aberto' ? 'Nenhum alerta aberto.' : 'Nenhum alerta resolvido.'}
      >
        <table>
          <caption className="sumario">{consulta.data?.total ?? 0} alerta(s)</caption>
          <thead>
            <tr>
              <th scope="col">Severidade</th>
              <th scope="col">Tipo</th>
              <th scope="col">Descrição</th>
              <th scope="col">Entrega</th>
              <th scope="col">Motorista</th>
              <th scope="col">Aberto em</th>
              {estadoFiltrado === 'Aberto' && <th scope="col">Ação</th>}
            </tr>
          </thead>
          <tbody>
            {consulta.data?.itens.map((alerta) => (
              <tr key={alerta.id}>
                <td>
                  <Selo tom={tomPorSeveridade[alerta.severidade]}>{alerta.severidade}</Selo>
                </td>
                <td>{alerta.tipo}</td>
                <td>{alerta.descricao}</td>
                <td>
                  {alerta.entregaId === null ? (
                    '—'
                  ) : (
                    <Link to={`/entregas/${alerta.entregaId}`}>{alerta.codigoDaEntrega ?? 'ver'}</Link>
                  )}
                </td>
                <td>{alerta.nomeDoMotorista ?? '—'}</td>
                <td>{instante(alerta.abertoEm)}</td>
                {estadoFiltrado === 'Aberto' && (
                  <td>
                    <button type="button" onClick={() => setEmResolucao(alerta.id)}>
                      Resolver
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </Estado>

      {emResolucao !== null && (
        <form
          className="formulario"
          onSubmit={(evento) => {
            evento.preventDefault();
            resolucao.mutate({ id: emResolucao, texto: observacao });
          }}
        >
          <label>
            O que foi feito
            <input
              type="text"
              required
              value={observacao}
              onChange={(evento) => setObservacao(evento.target.value)}
              placeholder="Falei com o motorista; seguiu para a próxima parada."
            />
          </label>

          <div className="acoes">
            <button type="submit" disabled={resolucao.isPending}>
              {resolucao.isPending ? 'Resolvendo…' : 'Confirmar'}
            </button>
            <button type="button" onClick={() => setEmResolucao(null)}>
              Cancelar
            </button>
          </div>

          {resolucao.isError && (
            <p className="formulario__erro" role="alert">
              Não foi possível resolver o alerta.
            </p>
          )}
        </form>
      )}
    </section>
  );
}
