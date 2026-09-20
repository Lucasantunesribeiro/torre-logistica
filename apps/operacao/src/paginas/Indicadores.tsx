import { useQuery } from '@tanstack/react-query';
import { useMemo, useState } from 'react';

import { Estado } from '../componentes/Estado';
import {
  obterIndicadores,
  type Indicador,
  type IndicadoresDaOperacao,
  type LinhaDoIndicador,
} from '../infra/api';

const PERIODOS = [
  { dias: 7, rotulo: 'Últimos 7 dias' },
  { dias: 30, rotulo: 'Últimos 30 dias' },
  { dias: 90, rotulo: 'Últimos 90 dias' },
] as const;

/**
 * Cada número do painel existe para responder uma pergunta que muda uma decisão da operação.
 *
 * A pergunta fica escrita acima do número, e a definição do cálculo logo abaixo: indicador sem
 * definição vira palpite com aparência de fato, e dois supervisores passam a discutir números que
 * nem significam a mesma coisa.
 */
const CARTOES = [
  {
    campo: 'pontualidadeEmPercentual',
    titulo: 'Pontualidade',
    pergunta: 'A operação está cumprindo a janela que prometeu?',
    unidade: '%',
  },
  {
    campo: 'sucessoNaPrimeiraTentativaEmPercentual',
    titulo: 'Sucesso na primeira tentativa',
    pergunta: 'Quanto da rota está sendo refeito por entrega que não deu certo na primeira ida?',
    unidade: '%',
  },
  {
    campo: 'atrasoMedioEmMinutos',
    titulo: 'Atraso médio',
    pergunta: 'Quando atrasa, atrasa por quanto?',
    unidade: 'min',
  },
  {
    campo: 'tempoMedioPorParadaEmMinutos',
    titulo: 'Tempo médio por parada',
    pergunta: 'Quanto tempo o motorista fica parado em cada destino?',
    unidade: 'min',
  },
  {
    campo: 'tempoMedioEmRotaEmMinutos',
    titulo: 'Tempo médio em rota',
    pergunta: 'Da saída à conclusão, quanto demora cada entrega?',
    unidade: 'min',
  },
] as const satisfies readonly {
  campo: keyof IndicadoresDaOperacao;
  titulo: string;
  pergunta: string;
  unidade: string;
}[];

const RECORTES = [
  {
    campo: 'entregasPorMotorista',
    titulo: 'Entregas por motorista',
    pergunta: 'Quem está carregando a operação, e com que pontualidade?',
    coluna: 'Motorista',
    valor: 'Pontualidade',
  },
  {
    campo: 'pontualidadePorCliente',
    titulo: 'SLA por cliente',
    pergunta: 'Para qual cliente a promessa está sendo quebrada?',
    coluna: 'Cliente',
    valor: 'Pontualidade',
  },
  {
    campo: 'entregasPorRota',
    titulo: 'Entregas por rota',
    pergunta: 'Qual rota concentra o atraso?',
    coluna: 'Rota',
    valor: 'Pontualidade',
  },
  {
    campo: 'ocorrenciasPorMotivo',
    titulo: 'Ocorrências por motivo',
    pergunta: 'Por que as entregas falham?',
    coluna: 'Motivo',
    valor: null,
  },
] as const satisfies readonly {
  campo: keyof IndicadoresDaOperacao;
  titulo: string;
  pergunta: string;
  coluna: string;
  valor: string | null;
}[];

/** Indicadores operacionais do período, com a definição de cada número ao lado dele. */
export function Indicadores() {
  const [dias, setDias] = useState<number>(30);

  const periodo = useMemo(() => {
    const ate = new Date();
    const de = new Date(ate.getTime() - dias * 24 * 60 * 60 * 1000);
    return { de: de.toISOString(), ate: ate.toISOString() };
  }, [dias]);

  const consulta = useQuery({
    queryKey: ['indicadores', periodo.de, periodo.ate],
    queryFn: () => obterIndicadores(periodo.de, periodo.ate),
  });

  const dados = consulta.data;

  return (
    <section>
      <h2>Indicadores</h2>

      <div className="filtros">
        <label>
          Período
          <select value={dias} onChange={(evento) => setDias(Number(evento.target.value))}>
            {PERIODOS.map((opcao) => (
              <option key={opcao.dias} value={opcao.dias}>
                {opcao.rotulo}
              </option>
            ))}
          </select>
        </label>
      </div>

      <Estado carregando={consulta.isPending} erro={consulta.error}>
        {dados === undefined ? null : (
          <>
            <p className="sumario">
              {dados.entregasConcluidas} entrega(s) concluída(s) no período
              {dados.entregasCanceladas > 0 ? ` · ${dados.entregasCanceladas} cancelada(s), fora das contas` : ''}
            </p>

            <div className="indicadores indicadores--detalhados">
              {CARTOES.map((cartao) => (
                <Cartao
                  key={cartao.campo}
                  titulo={cartao.titulo}
                  pergunta={cartao.pergunta}
                  unidade={cartao.unidade}
                  indicador={dados[cartao.campo]}
                />
              ))}
            </div>

            <div className="recortes">
              {RECORTES.map((recorte) => (
                <Recorte
                  key={recorte.campo}
                  titulo={recorte.titulo}
                  pergunta={recorte.pergunta}
                  coluna={recorte.coluna}
                  rotuloDoValor={recorte.valor}
                  linhas={dados[recorte.campo] as readonly LinhaDoIndicador[]}
                />
              ))}
            </div>
          </>
        )}
      </Estado>
    </section>
  );
}

function Cartao({
  titulo,
  pergunta,
  unidade,
  indicador,
}: {
  titulo: string;
  pergunta: string;
  unidade: string;
  indicador: Indicador;
}) {
  return (
    <article className="indicador">
      <h3>{titulo}</h3>
      <p className="indicador__pergunta">{pergunta}</p>

      <p className="indicador__valor">
        {indicador.valor === null ? (
          <span className="indicador__sem-base">sem dados no período</span>
        ) : (
          <>
            {formatar(indicador.valor)}
            <span className="indicador__unidade">{unidade}</span>
          </>
        )}
      </p>

      <p className="indicador__base">
        {indicador.base} entrega(s) na base do cálculo
      </p>
      <p className="indicador__definicao">{indicador.definicao}</p>
    </article>
  );
}

function Recorte({
  titulo,
  pergunta,
  coluna,
  rotuloDoValor,
  linhas,
}: {
  titulo: string;
  pergunta: string;
  coluna: string;
  rotuloDoValor: string | null;
  linhas: readonly LinhaDoIndicador[];
}) {
  // A barra é proporcional à maior linha do recorte — serve para achar o maior de relance, não para
  // ler o número, que está escrito ao lado.
  const maior = linhas.reduce((maximo, linha) => Math.max(maximo, linha.quantidade), 0);

  return (
    <article className="recorte">
      <h3>{titulo}</h3>
      <p className="recorte__pergunta">{pergunta}</p>

      {linhas.length === 0 ? (
        <p className="sumario">Nada no período.</p>
      ) : (
        <table>
          <thead>
            <tr>
              <th scope="col">{coluna}</th>
              <th scope="col">Quantidade</th>
              {rotuloDoValor === null ? null : <th scope="col">{rotuloDoValor}</th>}
            </tr>
          </thead>
          <tbody>
            {linhas.map((linha) => (
              <tr key={linha.rotulo}>
                <th scope="row">{linha.rotulo}</th>
                <td>
                  <span className="barra" style={{ width: `${maior === 0 ? 0 : (linha.quantidade / maior) * 100}%` }} />
                  {linha.quantidade}
                </td>
                {rotuloDoValor === null ? null : (
                  <td>{linha.valor === null ? '—' : `${formatar(linha.valor)}%`}</td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </article>
  );
}

function formatar(valor: number): string {
  return valor.toLocaleString('pt-BR', { maximumFractionDigits: 1 });
}
