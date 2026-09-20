import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { Estado, Selo, instante } from '../componentes/Estado';
import {
  criarAssinaturaDeWebhook,
  listarAssinaturasDeWebhook,
  listarEntregasDeWebhook,
  reenviarEntregaDeWebhook,
  revogarAssinaturaDeWebhook,
} from '../infra/api';

const EVENTOS = ['delivery.started', 'delivery.at_risk', 'delivery.failed_attempt', 'delivery.completed'] as const;

/**
 * Assinaturas de webhook e a fila de entregas.
 *
 * A lista de entregas falhadas é o ponto da tela: é ali que se vê o que não chegou ao assinante e se manda
 * de novo. Sem ela, a desistência do entregador seria invisível — que é exatamente o que a Fase 17
 * existiu para evitar.
 */
export function Webhooks() {
  const clienteDeConsultas = useQueryClient();
  const [nome, setNome] = useState('');
  const [url, setUrl] = useState('');
  const [escolhidos, setEscolhidos] = useState<readonly string[]>([]);
  const [estadoFiltrado, setEstadoFiltrado] = useState<'Pendente' | 'Entregue' | 'Falhada'>('Falhada');

  const assinaturas = useQuery({ queryKey: ['webhooks', 'assinaturas'], queryFn: () => listarAssinaturasDeWebhook() });

  const entregas = useQuery({
    queryKey: ['webhooks', 'entregas', estadoFiltrado],
    queryFn: () => listarEntregasDeWebhook(estadoFiltrado),
  });

  const criacao = useMutation({
    mutationFn: () => criarAssinaturaDeWebhook(nome, url, escolhidos),
    onSuccess: async () => {
      setNome('');
      setUrl('');
      setEscolhidos([]);
      await clienteDeConsultas.invalidateQueries({ queryKey: ['webhooks', 'assinaturas'] });
    },
  });

  const revogacao = useMutation({
    mutationFn: (id: string) => revogarAssinaturaDeWebhook(id),
    onSuccess: async () => {
      await clienteDeConsultas.invalidateQueries({ queryKey: ['webhooks', 'assinaturas'] });
    },
  });

  const reenvio = useMutation({
    mutationFn: (id: string) => reenviarEntregaDeWebhook(id),
    onSuccess: async () => {
      await clienteDeConsultas.invalidateQueries({ queryKey: ['webhooks', 'entregas'] });
    },
  });

  return (
    <section>
      <h2>Webhooks</h2>
      <p className="sumario">O segredo de assinatura é mostrado uma única vez, na criação.</p>

      <form
        className="formulario"
        onSubmit={(evento) => {
          evento.preventDefault();
          criacao.mutate();
        }}
      >
        <label>
          Nome
          <input type="text" required value={nome} onChange={(evento) => setNome(evento.target.value)} />
        </label>

        <label>
          Endereço de destino
          <input
            type="url"
            required
            value={url}
            onChange={(evento) => setUrl(evento.target.value)}
            placeholder="https://erp.exemplo.com/hooks/torre"
          />
        </label>

        <fieldset>
          <legend>Eventos (nenhum marcado significa todos)</legend>
          {EVENTOS.map((evento) => (
            <label key={evento} className="opcao">
              <input
                type="checkbox"
                checked={escolhidos.includes(evento)}
                onChange={(mudanca) =>
                  setEscolhidos((atual) =>
                    mudanca.target.checked ? [...atual, evento] : atual.filter((item) => item !== evento),
                  )
                }
              />
              {evento}
            </label>
          ))}
        </fieldset>

        <div className="acoes">
          <button type="submit" disabled={criacao.isPending}>
            {criacao.isPending ? 'Criando…' : 'Criar assinatura'}
          </button>
        </div>
      </form>

      {criacao.data !== undefined && (
        <p className="destaque">
          Segredo de <strong>{criacao.data.nome}</strong>: <code>{criacao.data.segredo}</code>
        </p>
      )}

      <h3>Assinaturas</h3>
      <Estado
        carregando={assinaturas.isPending}
        erro={assinaturas.error}
        vazio={assinaturas.data?.itens.length === 0}
        mensagemVazio="Nenhuma assinatura cadastrada."
      >
        <table>
          <thead>
            <tr>
              <th scope="col">Nome</th>
              <th scope="col">Destino</th>
              <th scope="col">Eventos</th>
              <th scope="col">Situação</th>
              <th scope="col">Ação</th>
            </tr>
          </thead>
          <tbody>
            {assinaturas.data?.itens.map((assinatura) => (
              <tr key={assinatura.id}>
                <td>{assinatura.nome}</td>
                <td>
                  <code>{assinatura.url}</code>
                </td>
                <td>{assinatura.eventos.length === 0 ? 'todos' : assinatura.eventos.join(', ')}</td>
                <td>
                  <Selo tom={assinatura.ativa ? 'bom' : 'neutro'}>{assinatura.ativa ? 'Ativa' : 'Revogada'}</Selo>
                </td>
                <td>
                  {assinatura.ativa && (
                    <button type="button" disabled={revogacao.isPending} onClick={() => revogacao.mutate(assinatura.id)}>
                      Revogar
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Estado>

      <h3>Entregas</h3>
      <div className="filtros">
        <label>
          Estado
          <select
            value={estadoFiltrado}
            onChange={(evento) => setEstadoFiltrado(evento.target.value as 'Pendente' | 'Entregue' | 'Falhada')}
          >
            <option value="Falhada">Falhadas</option>
            <option value="Pendente">Pendentes</option>
            <option value="Entregue">Entregues</option>
          </select>
        </label>
      </div>

      <Estado
        carregando={entregas.isPending}
        erro={entregas.error}
        vazio={entregas.data?.itens.length === 0}
        mensagemVazio="Nada neste estado."
      >
        <table>
          <thead>
            <tr>
              <th scope="col">Evento</th>
              <th scope="col">Destino</th>
              <th scope="col">Tentativas</th>
              <th scope="col">Último resultado</th>
              <th scope="col">Criada em</th>
              {estadoFiltrado === 'Falhada' && <th scope="col">Ação</th>}
            </tr>
          </thead>
          <tbody>
            {entregas.data?.itens.map((entrega) => (
              <tr key={entrega.id}>
                <td>{entrega.tipo}</td>
                <td>
                  <code>{entrega.url}</code>
                </td>
                <td>{entrega.tentativas}</td>
                <td>{entrega.ultimoStatus ?? entrega.ultimoErro ?? '—'}</td>
                <td>{instante(entrega.criadaEm)}</td>
                {estadoFiltrado === 'Falhada' && (
                  <td>
                    <button type="button" disabled={reenvio.isPending} onClick={() => reenvio.mutate(entrega.id)}>
                      Reenviar
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </Estado>
    </section>
  );
}
