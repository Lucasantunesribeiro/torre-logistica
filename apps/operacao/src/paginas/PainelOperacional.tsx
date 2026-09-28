import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router';

import { Estado, Selo, instante, tomDaSeveridade } from '../componentes/Estado';
import { listarAlertas, listarEntregas, listarOcorrencias } from '../infra/api';

/** Só o total interessa nestes contadores: pedir uma linha basta. */
const contarEntregas = (status: Parameters<typeof listarEntregas>[0]) =>
  listarEntregas({ ...status, tamanhoDaPagina: 1 });

/**
 * Painel de abertura: o estado da operação em poucos números e a fila do que precisa de gente.
 *
 * Nenhum gráfico aqui. Indicadores com série histórica são da Fase 19; o que esta tela responde é
 * "há algo exigindo ação agora?", e para isso contagem e lista bastam.
 */
export function PainelOperacional() {
  const emRota = useQuery({
    queryKey: ['entregas', 'contagem', 'EmRota'],
    queryFn: () => contarEntregas({ status: 'EmRota' }),
  });

  const chegando = useQuery({
    queryKey: ['entregas', 'contagem', 'ProximaDoDestino'],
    queryFn: () => contarEntregas({ status: 'ProximaDoDestino' }),
  });

  const frustradas = useQuery({
    queryKey: ['entregas', 'contagem', 'TentativaFrustrada'],
    queryFn: () => contarEntregas({ status: 'TentativaFrustrada' }),
  });

  const alertas = useQuery({ queryKey: ['alertas', 'Aberto'], queryFn: () => listarAlertas('Aberto') });
  const ocorrencias = useQuery({ queryKey: ['ocorrencias', 1], queryFn: () => listarOcorrencias(1) });

  const criticos = alertas.data?.itens.filter((alerta) => alerta.severidade === 'Critica').length ?? 0;

  return (
    <section>
      <h2>Painel operacional</h2>

      <div className="indicadores">
        <article>
          <p className="indicador__valor">{emRota.data?.total ?? '—'}</p>
          <p className="indicador__rotulo">a caminho</p>
        </article>

        <article>
          <p className="indicador__valor">{chegando.data?.total ?? '—'}</p>
          <p className="indicador__rotulo">chegando ao destino</p>
        </article>

        <article>
          <p className="indicador__valor">{frustradas.data?.total ?? '—'}</p>
          <p className="indicador__rotulo">tentativas sem sucesso</p>
        </article>

        <article className={criticos > 0 ? 'indicador--ruim' : undefined}>
          <p className="indicador__valor">{alertas.data?.total ?? '—'}</p>
          <p className="indicador__rotulo">alertas abertos{criticos > 0 ? ` · ${criticos} crítico(s)` : ''}</p>
        </article>
      </div>

      <h3>Alertas abertos</h3>
      <Estado
        carregando={alertas.isPending}
        erro={alertas.error}
        vazio={alertas.data?.itens.length === 0}
        mensagemVazio="Nada aberto. A operação está limpa."
      >
        <ul className="lista">
          {alertas.data?.itens.slice(0, 8).map((alerta) => (
            <li key={alerta.id}>
              <Selo tom={tomDaSeveridade(alerta.severidade)}>{alerta.severidade}</Selo>{' '}
              {alerta.descricao}
              {alerta.entregaId === null ? null : (
                <>
                  {' · '}
                  <Link to={`/entregas/${alerta.entregaId}`}>{alerta.codigoDaEntrega ?? 'entrega'}</Link>
                </>
              )}
              <span className="sumario"> · {instante(alerta.abertoEm)}</span>
            </li>
          ))}
        </ul>
      </Estado>

      <h3>Últimas ocorrências</h3>
      <Estado
        carregando={ocorrencias.isPending}
        erro={ocorrencias.error}
        vazio={ocorrencias.data?.itens.length === 0}
        mensagemVazio="Nenhuma ocorrência registrada."
      >
        <ul className="lista">
          {ocorrencias.data?.itens.slice(0, 8).map((ocorrencia) => (
            <li key={ocorrencia.id}>
              {ocorrencia.tipo} ·{' '}
              <Link to={`/entregas/${ocorrencia.entregaId}`}>{ocorrencia.codigoDaEntrega}</Link>
              <span className="sumario"> · {instante(ocorrencia.ocorridaEm)}</span>
            </li>
          ))}
        </ul>
      </Estado>
    </section>
  );
}
