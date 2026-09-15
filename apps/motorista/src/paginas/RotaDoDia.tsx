import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router';

import { Aviso, Carregando } from '../componentes/Aviso';
import { apiDoMotorista, mensagemDeErro } from '../infra/api';
import type { Parada } from '../infra/api';
import { enderecoEmLinha, formatarHora, formatarJanela, ROTULOS_DA_ENTREGA, ROTULOS_DA_ROTA } from '../infra/formatos';

/**
 * A tela que o motorista mais usa: a rota de agora e a próxima entrega, com a ação seguinte em destaque.
 * Nada de painel: uma coisa por vez.
 */
export function RotaDoDia() {
  const rotas = useQuery({ queryKey: ['rotas'], queryFn: apiDoMotorista.listarRotas });

  if (rotas.isPending) {
    return <Carregando texto="Carregando sua rota…" />;
  }

  if (rotas.isError) {
    return (
      <Aviso
        erro={rotas.error}
        aoTentarDeNovo={() => {
          void rotas.refetch();
        }}
      />
    );
  }

  const [atual, ...proximas] = rotas.data;

  if (!atual) {
    return (
      <section>
        <h2>Rota do dia</h2>
        <div className="cartao">
          <p>Nenhuma rota para você agora.</p>
          <p className="sumario">Quando a operação liberar sua rota, ela aparece aqui.</p>
          <button
            type="button"
            className="acao acao--secundaria"
            onClick={() => {
              void rotas.refetch();
            }}
          >
            Atualizar
          </button>
        </div>
      </section>
    );
  }

  return (
    <section>
      <h2>Rota do dia</h2>
      <PainelDaRota rotaId={atual.id} />

      {proximas.length > 0 ? (
        <div className="cartao">
          <h3>Próximas rotas</h3>
          <ul className="lista-simples">
            {proximas.map((rota) => (
              <li key={rota.id}>
                {rota.codigo} · {ROTULOS_DA_ROTA[rota.status]} · {rota.totalDeParadas} parada(s)
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </section>
  );
}

function PainelDaRota({ rotaId }: { readonly rotaId: string }) {
  const cliente = useQueryClient();
  const rota = useQuery({ queryKey: ['rota', rotaId], queryFn: () => apiDoMotorista.obterRota(rotaId) });

  const atualizar = () => Promise.all([
    cliente.invalidateQueries({ queryKey: ['rotas'] }),
    cliente.invalidateQueries({ queryKey: ['rota', rotaId] }),
  ]);

  const iniciar = useMutation({ mutationFn: () => apiDoMotorista.iniciarRota(rotaId), onSettled: atualizar });
  const encerrar = useMutation({ mutationFn: () => apiDoMotorista.concluirRota(rotaId), onSettled: atualizar });

  if (rota.isPending) {
    return <Carregando texto="Carregando as paradas…" />;
  }

  if (rota.isError) {
    return (
      <Aviso
        erro={rota.error}
        aoTentarDeNovo={() => {
          void rota.refetch();
        }}
      />
    );
  }

  const dados = rota.data;
  const pendentes = dados.paradas.filter((parada) => parada.status === 'EmRota' || parada.status === 'ProximaDoDestino' || parada.status === 'Atribuida');
  const proxima = dados.paradas.find((parada) => parada.status === 'EmRota' || parada.status === 'ProximaDoDestino');
  const erro = iniciar.error ?? encerrar.error;

  return (
    <>
      <div className="cartao">
        <div className="cabecalho-do-cartao">
          <strong>{dados.codigo}</strong>
          <span className={`etiqueta etiqueta--${dados.status}`}>{ROTULOS_DA_ROTA[dados.status]}</span>
        </div>
        <p className="sumario">
          {dados.veiculo ? `${dados.veiculo.identificacao} · ${dados.veiculo.placa}` : 'Veículo não definido'}
          {dados.saidaPlanejada ? ` · saída às ${formatarHora(dados.saidaPlanejada)}` : ''}
        </p>
        <p>
          {pendentes.length} de {dados.paradas.length} entrega(s) pendente(s)
        </p>

        {dados.status === 'Planejada' ? (
          <button
            type="button"
            className="acao acao--primaria"
            disabled={iniciar.isPending}
            onClick={() => {
              iniciar.mutate();
            }}
          >
            {iniciar.isPending ? 'Iniciando…' : 'Iniciar rota'}
          </button>
        ) : null}

        <Link className="acao acao--secundaria" to={`/rotas/${dados.id}/paradas`}>
          Ver todas as paradas ({dados.paradas.length})
        </Link>

        {erro ? (
          <p className="erro" role="alert">
            {mensagemDeErro(erro)}
          </p>
        ) : null}
      </div>

      {dados.status === 'EmAndamento' && proxima ? <ProximaEntrega parada={proxima} /> : null}

      {dados.status === 'EmAndamento' && !proxima ? (
        <div className="cartao">
          <p>Todas as entregas desta rota têm resultado.</p>
          <button
            type="button"
            className="acao acao--primaria"
            disabled={encerrar.isPending}
            onClick={() => {
              encerrar.mutate();
            }}
          >
            {encerrar.isPending ? 'Encerrando…' : 'Encerrar rota'}
          </button>
        </div>
      ) : null}
    </>
  );
}

function ProximaEntrega({ parada }: { readonly parada: Parada }) {
  return (
    <div className="cartao cartao--destaque">
      <h3>Próxima entrega</h3>
      <div className="cabecalho-do-cartao">
        <strong>{parada.destinatarioNome}</strong>
        <span className={`etiqueta etiqueta--${parada.status}`}>{ROTULOS_DA_ENTREGA[parada.status]}</span>
      </div>
      <p>{enderecoEmLinha(parada.endereco)}</p>
      <p className="sumario">
        Parada {parada.sequencia} · janela {formatarJanela(parada.janelaPrometida.de, parada.janelaPrometida.ate)}
      </p>
      <Link className="acao acao--primaria" to={`/entregas/${parada.entregaId}`}>
        Abrir entrega
      </Link>
    </div>
  );
}
