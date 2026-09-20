import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { Estado, Selo, instante } from '../componentes/Estado';
import { criarIntegracao, listarIntegracoes, revogarIntegracao } from '../infra/api';

/**
 * Credenciais que sistemas externos usam para criar entregas.
 *
 * A chave aparece uma vez só, no momento da emissão — o banco guarda o hash. Por isso a tela avisa antes
 * e mostra o valor em destaque depois: quem não copiar agora terá de revogar e emitir outra.
 */
export function Integracoes() {
  const clienteDeConsultas = useQueryClient();
  const [nome, setNome] = useState('');

  const consulta = useQuery({ queryKey: ['integracoes'], queryFn: () => listarIntegracoes() });

  const criacao = useMutation({
    mutationFn: () => criarIntegracao(nome),
    onSuccess: async () => {
      setNome('');
      await clienteDeConsultas.invalidateQueries({ queryKey: ['integracoes'] });
    },
  });

  const revogacao = useMutation({
    mutationFn: (id: string) => revogarIntegracao(id),
    onSuccess: async () => {
      await clienteDeConsultas.invalidateQueries({ queryKey: ['integracoes'] });
    },
  });

  return (
    <section>
      <h2>Integrações</h2>
      <p className="sumario">
        A chave é mostrada uma única vez. Perdida, revogue esta credencial e emita outra.
      </p>

      <form
        className="formulario"
        onSubmit={(evento) => {
          evento.preventDefault();
          criacao.mutate();
        }}
      >
        <label>
          Nome de quem integra
          <input
            type="text"
            required
            value={nome}
            onChange={(evento) => setNome(evento.target.value)}
            placeholder="ERP do cliente"
          />
        </label>

        <div className="acoes">
          <button type="submit" disabled={criacao.isPending}>
            {criacao.isPending ? 'Emitindo…' : 'Emitir credencial'}
          </button>
        </div>
      </form>

      {criacao.data !== undefined && (
        <p className="destaque">
          Chave de <strong>{criacao.data.nome}</strong>: <code>{criacao.data.chave}</code>
        </p>
      )}

      <Estado
        carregando={consulta.isPending}
        erro={consulta.error}
        vazio={consulta.data?.itens.length === 0}
        mensagemVazio="Nenhuma credencial emitida."
      >
        <table>
          <thead>
            <tr>
              <th scope="col">Nome</th>
              <th scope="col">Identificador</th>
              <th scope="col">Situação</th>
              <th scope="col">Criada em</th>
              <th scope="col">Último uso</th>
              <th scope="col">Ação</th>
            </tr>
          </thead>
          <tbody>
            {consulta.data?.itens.map((integracao) => (
              <tr key={integracao.id}>
                <td>{integracao.nome}</td>
                <td>
                  <code>{integracao.identificadorPublico}</code>
                </td>
                <td>
                  <Selo tom={integracao.ativa ? 'bom' : 'neutro'}>{integracao.ativa ? 'Ativa' : 'Revogada'}</Selo>
                </td>
                <td>{instante(integracao.criadaEm)}</td>
                <td>{instante(integracao.ultimoUsoEm)}</td>
                <td>
                  {integracao.ativa && (
                    <button type="button" disabled={revogacao.isPending} onClick={() => revogacao.mutate(integracao.id)}>
                      Revogar
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Estado>
    </section>
  );
}
