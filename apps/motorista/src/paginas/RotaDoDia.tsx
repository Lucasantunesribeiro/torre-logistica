import { Link } from 'react-router';

import { Aviso, Carregando } from '../componentes/Aviso';
import { AvisoDeCopia, MENSAGEM_DE_FALHA_AO_GUARDAR } from '../componentes/EstadoDaSincronizacao';
import type { Parada } from '../infra/api';
import { enderecoEmLinha, formatarHora, formatarJanela, ROTULOS_DA_ENTREGA, ROTULOS_DA_ROTA } from '../infra/formatos';
import { useRegistrarAcao, useRota, useRotas } from '../offline/ProvedorDeSincronizacao';

/**
 * A tela que o motorista mais usa: a rota de agora e a próxima entrega, com a ação seguinte em destaque.
 * Nada de painel: uma coisa por vez.
 */
export function RotaDoDia() {
  const rotas = useRotas();

  if (rotas.consulta.isError) {
    return (
      <Aviso
        erro={rotas.consulta.error}
        aoTentarDeNovo={() => {
          void rotas.consulta.refetch();
        }}
      />
    );
  }

  if (!rotas.dados) {
    return <Carregando texto="Carregando sua rota…" />;
  }

  const [atual, ...proximas] = rotas.dados;

  if (!atual) {
    return (
      <section>
        <h2>Rota do dia</h2>
        <AvisoDeCopia leitura={rotas.leitura} />
        <div className="cartao">
          <p>Nenhuma rota para você agora.</p>
          <p className="sumario">Quando a operação liberar sua rota, ela aparece aqui.</p>
          <button
            type="button"
            className="acao acao--secundaria"
            onClick={() => {
              void rotas.consulta.refetch();
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
      <AvisoDeCopia leitura={rotas.leitura} />
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
  const rota = useRota(rotaId);
  const acao = useRegistrarAcao();

  if (rota.consulta.isError) {
    return (
      <Aviso
        erro={rota.consulta.error}
        aoTentarDeNovo={() => {
          void rota.consulta.refetch();
        }}
      />
    );
  }

  if (!rota.dados) {
    return <Carregando texto="Carregando as paradas…" />;
  }

  const dados = rota.dados;
  const pendentes = dados.paradas.filter((parada) => parada.status === 'EmRota' || parada.status === 'ProximaDoDestino' || parada.status === 'Atribuida');
  const proxima = dados.paradas.find((parada) => parada.status === 'EmRota' || parada.status === 'ProximaDoDestino');

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
            disabled={acao.registrando}
            onClick={() => {
              void acao.executar({ tipo: 'IniciarRota', alvoId: dados.id, descricao: `Início da rota ${dados.codigo}` });
            }}
          >
            {acao.registrando ? 'Iniciando…' : 'Iniciar rota'}
          </button>
        ) : null}

        <Link className="acao acao--secundaria" to={`/rotas/${dados.id}/paradas`}>
          Ver todas as paradas ({dados.paradas.length})
        </Link>

        {acao.falhou ? (
          <p className="erro" role="alert">
            {MENSAGEM_DE_FALHA_AO_GUARDAR}
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
            disabled={acao.registrando}
            onClick={() => {
              void acao.executar({ tipo: 'ConcluirRota', alvoId: dados.id, descricao: `Encerramento da rota ${dados.codigo}` });
            }}
          >
            {acao.registrando ? 'Encerrando…' : 'Encerrar rota'}
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
