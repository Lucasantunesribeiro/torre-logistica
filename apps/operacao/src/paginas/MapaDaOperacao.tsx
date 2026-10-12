import { useQueries, useQuery } from '@tanstack/react-query';
import { Map as MapaGl, addProtocol, setWorkerUrl, type GeoJSONSource } from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';
// O MapLibre v6 é ESM e, sob um bundler, não resolve sozinho a URL do próprio Web Worker. O worker do
// MapLibre importa de um chunk compartilhado, então é preciso o Vite EMPACOTÁ-LO (`?worker&url`, não `?url`):
// o `?url` cru entregaria um arquivo com um import que não resolve, e o worker morreria ao subir, sem erro.
import maplibreWorkerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url';
import { Protocol } from 'pmtiles';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link } from 'react-router';

import { Estado, Selo, hora } from '../componentes/Estado';
import { listarEntregas, obterPosicaoAtual, type Entrega } from '../infra/api';
import { ambiente } from '../infra/ambiente';
import { useTempoRealDaOperacao, type PosicaoAoVivo } from '../infra/tempoReal';

/**
 * Estilo usado quando não há provedor de tiles configurado.
 *
 * O mapa continua útil sem mapa de fundo: o que a operação precisa ver são os pontos e a relação entre
 * eles. Assim o console não depende de contratar fornecedor para funcionar — e quem contratar um só
 * precisa apontar a variável de ambiente.
 */
const estiloSemProvedor = {
  version: 8 as const,
  sources: {},
  layers: [{ id: 'fundo', type: 'background' as const, paint: { 'background-color': '#eef1f5' } }],
};

/**
 * Prepara a infraestrutura global do MapLibre uma única vez: a URL do Web Worker e o protocolo `pmtiles://`.
 *
 * Duas coisas globais, feitas juntas antes de qualquer mapa:
 * - <b>Worker.</b> O MapLibre v6 é ESM e, sob o Vite, não descobre sozinho a URL do próprio worker — e é o
 *   worker que busca e decodifica os tiles vetoriais. Sem isto ele não sobe, nenhum tile é pedido e o mapa
 *   pinta só o fundo do estilo, <i>sem erro no console</i>. O bug ficava escondido enquanto o mapa usava só
 *   o fundo neutro (sem fonte, sem worker); apareceu ao ligar a fonte PMTiles.
 * - <b>Protocolo.</b> O basemap é um `.pmtiles` self-host (Protomaps), lido por requisições Range direto do
 *   arquivo, sem servidor de tiles. Registrar o protocolo duas vezes lança erro, daí a trava.
 */
let infraDoMapaPronta = false;
function garantirInfraDoMapa(): void {
  if (!infraDoMapaPronta) {
    setWorkerUrl(maplibreWorkerUrl);
    addProtocol('pmtiles', new Protocol().tile);
    infraDoMapaPronta = true;
  }
}

interface PontoDoMotorista {
  readonly motoristaId: string;
  readonly latitude: number;
  readonly longitude: number;
  readonly capturadaEm: string;
}

/**
 * Mapa da Operação — a tela símbolo.
 *
 * Mostra quem está a caminho e para onde, com a lista sincronizada ao lado: clicar na entrega leva o mapa
 * até ela. As posições chegam pelo canal de tempo real e são aplicadas direto, sem ida ao servidor —
 * posição muda muitas vezes por minuto, e recarregar a lista a cada uma seria desperdício puro.
 */
export function MapaDaOperacao() {
  const container = useRef<HTMLDivElement | null>(null);
  const mapa = useRef<MapaGl | null>(null);
  const [pronto, setPronto] = useState(false);
  const [fundoNeutro, setFundoNeutro] = useState(false);
  const [aoVivo, setAoVivo] = useState<Record<string, PontoDoMotorista>>({});
  const [selecionada, setSelecionada] = useState<string | null>(null);

  const aplicarPosicao = useCallback((posicao: PosicaoAoVivo) => {
    setAoVivo((atual) => ({
      ...atual,
      [posicao.motoristaId]: {
        motoristaId: posicao.motoristaId,
        latitude: posicao.latitude,
        longitude: posicao.longitude,
        capturadaEm: posicao.capturadaEm,
      },
    }));
  }, []);

  const conexao = useTempoRealDaOperacao(aplicarPosicao);

  const emRota = useQuery({
    queryKey: ['entregas', 'mapa'],
    queryFn: () => listarEntregas({ status: 'EmRota', tamanhoDaPagina: 100 }),
  });

  const motoristaIds = useMemo(
    () => [...new Set((emRota.data?.itens ?? []).map((entrega) => entrega.execucao.motoristaId).filter((id) => id !== null))],
    [emRota.data],
  );

  const posicoes = useQueries({
    queries: motoristaIds.map((id) => ({
      queryKey: ['posicao', id],
      queryFn: () => obterPosicaoAtual(id),
      retry: false,
    })),
  });

  const pontos = useMemo<readonly PontoDoMotorista[]>(() => {
    const doServidor = posicoes
      .map((consulta, indice) => {
        const dados = consulta.data;
        const id = motoristaIds[indice];
        return dados === undefined || id === undefined
          ? null
          : { motoristaId: id, latitude: dados.latitude, longitude: dados.longitude, capturadaEm: dados.capturadaEm };
      })
      .filter((ponto): ponto is PontoDoMotorista => ponto !== null);

    // O que chegou ao vivo tem prioridade sobre o que foi lido na abertura da tela.
    return doServidor.map((ponto) => aoVivo[ponto.motoristaId] ?? ponto).concat(
      Object.values(aoVivo).filter((ponto) => !doServidor.some((lido) => lido.motoristaId === ponto.motoristaId)),
    );
  }, [posicoes, motoristaIds, aoVivo]);

  const destinos = useMemo(
    () => (emRota.data?.itens ?? []).filter((entrega) => entrega.localizacao !== null),
    [emRota.data],
  );

  useEffect(() => {
    if (container.current === null || mapa.current !== null) {
      return;
    }

    garantirInfraDoMapa();

    const temEstiloConfigurado = ambiente.urlDoEstiloDoMapa !== null;

    const instancia = new MapaGl({
      container: container.current,
      style: ambiente.urlDoEstiloDoMapa ?? estiloSemProvedor,
      center: [-47.06, -22.91],
      zoom: 10,
      // Atribuição visível: o basemap é derivado do OpenStreetMap e servido via Protomaps, e os dois
      // exigem crédito. Compacta para não cobrir a operação.
      attributionControl: { compact: true },
    });

    let caiu = false;
    const cairParaFundoNeutro = () => {
      if (caiu) {
        return;
      }
      caiu = true;
      setFundoNeutro(true);
      instancia.setStyle(estiloSemProvedor);
    };

    // Cai para o fundo neutro só se o ESTILO não chegar a carregar dentro do prazo. O sinal de sucesso é
    // `style.load` — o estilo foi lido e é usável —, e NÃO `load`, que só dispara quando os primeiros tiles
    // também chegaram: num servidor compartilhado a primeira leva de tiles vetoriais pode demorar, e esperar
    // por ela neutralizaria um mapa que está perfeitamente bom. Erros isolados de tile ou glyph não derrubam
    // o estilo; o que derruba — arquivos ausentes, estilo inválido — se manifesta como um style.load que
    // nunca chega, e é isso que o prazo cobre.
    const prazoDoEstilo = temEstiloConfigurado ? window.setTimeout(cairParaFundoNeutro, 12000) : undefined;

    const marcarPronto = () => {
      window.clearTimeout(prazoDoEstilo);
      setPronto(true);
    };

    instancia.on('style.load', marcarPronto);
    instancia.on('load', marcarPronto);

    mapa.current = instancia;

    return () => {
      window.clearTimeout(prazoDoEstilo);
      instancia.remove();
      mapa.current = null;
      setPronto(false);
      setFundoNeutro(false);
    };
  }, []);

  useEffect(() => {
    const instancia = mapa.current;

    if (instancia === null || !pronto) {
      return;
    }

    desenhar(instancia, 'destinos', '#1c7c45', destinos.map((entrega) => ({
      lng: entrega.localizacao?.longitude ?? 0,
      lat: entrega.localizacao?.latitude ?? 0,
      id: entrega.id,
    })));

    desenhar(instancia, 'motoristas', '#a5201c', pontos.map((ponto) => ({
      lng: ponto.longitude,
      lat: ponto.latitude,
      id: ponto.motoristaId,
    })));
  }, [pronto, destinos, pontos]);

  function irPara(entrega: Entrega) {
    setSelecionada(entrega.id);

    if (mapa.current !== null && entrega.localizacao !== null) {
      mapa.current.flyTo({ center: [entrega.localizacao.longitude, entrega.localizacao.latitude], zoom: 13 });
    }
  }

  return (
    <section className="mapa">
      <div className="mapa__lateral">
        <h2>Mapa da operação</h2>

        <p className={`conexao conexao--${conexao === 'conectado' ? 'pronta' : 'indisponivel'}`} role="status">
          <span className="conexao__marca" aria-hidden="true" />
          {conexao === 'conectado' ? 'Recebendo posições ao vivo' : 'Sem posições ao vivo'}
        </p>

        <p className="sumario">
          {destinos.length} entrega(s) a caminho · {pontos.length} motorista(s) com posição
        </p>

        {ambiente.urlDoEstiloDoMapa === null && (
          <p className="sumario">
            Sem provedor de mapa configurado: os pontos aparecem sobre fundo neutro.
          </p>
        )}

        {ambiente.urlDoEstiloDoMapa !== null && fundoNeutro && (
          <p className="sumario">
            Mapa de fundo indisponível agora: os pontos aparecem sobre fundo neutro.
          </p>
        )}

        <Estado
          carregando={emRota.isPending}
          erro={emRota.error}
          vazio={emRota.data?.itens.length === 0}
          mensagemVazio="Nenhuma entrega a caminho agora."
        >
          <ul className="lista lista--selecionavel">
            {emRota.data?.itens.map((entrega) => {
              const ponto = entrega.execucao.motoristaId === null ? undefined : aoVivo[entrega.execucao.motoristaId];

              return (
                <li key={entrega.id} className={selecionada === entrega.id ? 'selecionado' : undefined}>
                  <button type="button" onClick={() => irPara(entrega)}>
                    {entrega.codigo}
                  </button>
                  <span className="sumario">
                    {' '}
                    {entrega.destinatarioNome} · {entrega.endereco.bairro}
                  </span>
                  {ponto === undefined ? null : <Selo tom="atencao">posição {hora(ponto.capturadaEm)}</Selo>}
                  {' '}
                  <Link to={`/entregas/${entrega.id}`}>abrir</Link>
                </li>
              );
            })}
          </ul>
        </Estado>
      </div>

      <div className="mapa__tela" ref={container} role="application" aria-label="Mapa da operação" />
    </section>
  );
}

/** Cria ou atualiza uma camada de pontos. */
function desenhar(
  instancia: MapaGl,
  nome: string,
  cor: string,
  pontos: readonly { lng: number; lat: number; id: string }[],
): void {
  const dados = {
    type: 'FeatureCollection' as const,
    features: pontos.map((ponto) => ({
      type: 'Feature' as const,
      geometry: { type: 'Point' as const, coordinates: [ponto.lng, ponto.lat] },
      properties: { id: ponto.id },
    })),
  };

  const fonte: GeoJSONSource | undefined = instancia.getSource(nome);

  if (fonte === undefined) {
    instancia.addSource(nome, { type: 'geojson', data: dados });
    instancia.addLayer({
      id: nome,
      type: 'circle',
      source: nome,
      paint: { 'circle-radius': 7, 'circle-color': cor, 'circle-stroke-width': 2, 'circle-stroke-color': '#ffffff' },
    });
    return;
  }

  void fonte.setData(dados);
}
