import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';

import { Aviso, Carregando } from '../componentes/Aviso';
import { AvisoDeCopia, MENSAGEM_DE_FALHA_AO_GUARDAR } from '../componentes/EstadoDaSincronizacao';
import { apiDoMotorista, mensagemDeErro, TIPOS_DE_IMAGEM_ACEITOS } from '../infra/api';
import type { Entrega } from '../infra/api';
import {
  enderecoEmLinha,
  formatarHora,
  formatarJanela,
  linkDeNavegacao,
  ROTULOS_DA_ENTREGA,
  ROTULOS_DO_MOTIVO,
} from '../infra/formatos';
import { useEntrega, useRegistrarAcao, useSincronizacao } from '../offline/ProvedorDeSincronizacao';

/**
 * Tudo o que o motorista precisa para fazer a entrega, e as ações do momento — grandes, uma de cada vez.
 *
 * Concluir pede confirmação: é a ação que não se desfaz. Com conexão, a conclusão vai com a prova — quem
 * recebeu e, quando o motorista tira, a foto, que sobe direto ao storage por URL assinada. Sem conexão, a
 * conclusão entra na fila do aparelho como qualquer outra ação, e a prova fica para quando houver rede.
 */
export function DetalheDaEntrega() {
  const { entregaId = '' } = useParams();
  const navegar = useNavigate();
  const [confirmando, setConfirmando] = useState(false);

  const entrega = useEntrega(entregaId);
  const acao = useRegistrarAcao();

  if (entrega.consulta.isError) {
    return (
      <>
        <Aviso
          erro={entrega.consulta.error}
          aoTentarDeNovo={() => {
            void entrega.consulta.refetch();
          }}
        />
        <Link className="link" to="/">
          Voltar para a rota do dia
        </Link>
      </>
    );
  }

  if (!entrega.dados) {
    return <Carregando texto="Carregando a entrega…" />;
  }

  const dados = entrega.dados;
  const emExecucao = dados.status === 'EmRota' || dados.status === 'ProximaDoDestino';

  return (
    <section>
      <h2>{dados.destinatario.nome}</h2>
      <p className="sumario">
        {dados.codigo}
        {dados.sequencia !== null ? ` · parada ${dados.sequencia}` : ''} ·{' '}
        <span className={`etiqueta etiqueta--${dados.status}`}>{ROTULOS_DA_ENTREGA[dados.status]}</span>
      </p>
      <AvisoDeCopia leitura={entrega.leitura} />

      <div className="cartao">
        <dl className="dados">
          <dt>Endereço</dt>
          <dd>{enderecoEmLinha(dados.endereco)}</dd>
          <dt>Janela prometida</dt>
          <dd>{formatarJanela(dados.janelaPrometida.de, dados.janelaPrometida.ate)}</dd>
          {dados.destinatario.instrucoesDeEntrega ? (
            <>
              <dt>Instruções</dt>
              <dd>{dados.destinatario.instrucoesDeEntrega}</dd>
            </>
          ) : null}
          {dados.observacoes ? (
            <>
              <dt>Observações da operação</dt>
              <dd>{dados.observacoes}</dd>
            </>
          ) : null}
        </dl>

        <a className="acao acao--secundaria" href={linkDeNavegacao(dados)} target="_blank" rel="noreferrer">
          Abrir no mapa
        </a>
        {dados.destinatario.telefone ? (
          <a className="acao acao--secundaria" href={`tel:${dados.destinatario.telefone.replace(/[^\d+]/g, '')}`}>
            Ligar para {dados.destinatario.nome}
          </a>
        ) : null}
      </div>

      <div className="cartao">
        {emExecucao ? (
          confirmando ? (
            <ConclusaoComProva
              entrega={dados}
              aoVoltar={() => {
                setConfirmando(false);
              }}
              aoConcluir={() => {
                void navegar('/', { replace: true });
              }}
            />
          ) : (
            <AcoesDaEntrega
              entrega={dados}
              registrando={acao.registrando}
              aoRegistrarChegada={() => {
                void acao.executar({ tipo: 'RegistrarChegada', alvoId: dados.id, descricao: `Chegada à entrega de ${dados.destinatario.nome}` });
              }}
              aoPedirConfirmacao={() => {
                setConfirmando(true);
              }}
            />
          )
        ) : (
          <p>
            {dados.status === 'Atribuida'
              ? 'Inicie a rota para registrar esta entrega.'
              : `Resultado registrado: ${ROTULOS_DA_ENTREGA[dados.status]}.`}
          </p>
        )}

        {acao.falhou ? (
          <p className="erro" role="alert">
            {MENSAGEM_DE_FALHA_AO_GUARDAR}
          </p>
        ) : null}
      </div>

      <div className="cartao">
        <h3>Ocorrências</h3>
        {dados.execucao.tentativasFrustradas === 0 ? (
          <p className="sumario">Nenhuma ocorrência registrada.</p>
        ) : (
          <p>
            {dados.execucao.tentativasFrustradas} tentativa(s) sem sucesso
            {dados.execucao.motivoDaUltimaTentativa ? ` — último motivo: ${ROTULOS_DO_MOTIVO[dados.execucao.motivoDaUltimaTentativa]}` : ''}
            {dados.execucao.ultimaTentativaFrustradaEm ? `, às ${formatarHora(dados.execucao.ultimaTentativaFrustradaEm)}` : ''}.
          </p>
        )}
      </div>

      <Link className="link" to="/">
        Voltar para a rota do dia
      </Link>
    </section>
  );
}

function AcoesDaEntrega(props: {
  readonly entrega: Entrega;
  readonly registrando: boolean;
  readonly aoRegistrarChegada: () => void;
  readonly aoPedirConfirmacao: () => void;
}) {
  return (
    <>
      {props.entrega.status === 'EmRota' ? (
        <button type="button" className="acao acao--secundaria" disabled={props.registrando} onClick={props.aoRegistrarChegada}>
          {props.registrando ? 'Registrando…' : 'Cheguei ao destino'}
        </button>
      ) : null}

      <button type="button" className="acao acao--primaria" onClick={props.aoPedirConfirmacao}>
        Entrega concluída
      </button>

      <Link className="acao acao--perigo" to={`/entregas/${props.entrega.id}/ocorrencia`}>
        Não foi possível entregar
      </Link>
    </>
  );
}

/**
 * Confirmação da entrega com a prova.
 *
 * Com conexão, o comprovante é registrado na hora: quem recebeu, a foto (quando houver) e a posição. Sem
 * conexão, a conclusão entra na fila do aparelho — a prova exige rede, porque o arquivo sobe direto ao
 * storage (ADR 0023).
 */
function ConclusaoComProva(props: {
  readonly entrega: Entrega;
  readonly aoVoltar: () => void;
  readonly aoConcluir: () => void;
}) {
  const { registrar } = useSincronizacao();
  const [recebidoPor, setRecebidoPor] = useState('');
  const [foto, setFoto] = useState<File | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [erro, setErro] = useState<unknown>(null);

  const online = typeof navigator === 'undefined' || navigator.onLine;
  const nomePreenchido = recebidoPor.trim().length > 0;
  const podeConcluir = !enviando && (!online || nomePreenchido);

  async function concluir() {
    setEnviando(true);
    setErro(null);

    try {
      if (!online) {
        // Sem rede: a conclusão vai pela fila, e a prova fica para a operação registrar depois.
        await registrar({
          tipo: 'ConcluirEntrega',
          alvoId: props.entrega.id,
          descricao: `Conclusão da entrega de ${props.entrega.destinatario.nome}`,
        });
        props.aoConcluir();
        return;
      }

      const arquivos: { tipo: 'Foto'; chave: string }[] = [];

      if (foto) {
        const autorizacao = await apiDoMotorista.autorizarArquivoDoComprovante(props.entrega.id, 'Foto', foto.type);
        await apiDoMotorista.enviarArquivo(autorizacao, foto);
        arquivos.push({ tipo: 'Foto', chave: autorizacao.chave });
      }

      await apiDoMotorista.registrarComprovante(props.entrega.id, {
        recebidoPor: recebidoPor.trim(),
        observacao: null,
        latitude: props.entrega.localizacao?.latitude ?? null,
        longitude: props.entrega.localizacao?.longitude ?? null,
        arquivos,
      });

      props.aoConcluir();
    } catch (falha) {
      setErro(falha);
    } finally {
      setEnviando(false);
    }
  }

  return (
    <>
      <p>Confirma que a entrega para {props.entrega.destinatario.nome} foi feita?</p>

      {online ? (
        <>
          <label className="campo" htmlFor="recebidoPor">
            Quem recebeu
          </label>
          <input
            id="recebidoPor"
            name="recebidoPor"
            type="text"
            autoComplete="off"
            maxLength={120}
            value={recebidoPor}
            onChange={(evento) => {
              setRecebidoPor(evento.target.value);
            }}
          />

          <label className="campo" htmlFor="foto">
            Foto da entrega (opcional)
          </label>
          <input
            id="foto"
            name="foto"
            type="file"
            accept={TIPOS_DE_IMAGEM_ACEITOS.join(',')}
            capture="environment"
            onChange={(evento) => {
              setFoto(evento.target.files?.[0] ?? null);
            }}
          />
        </>
      ) : (
        <p className="sumario">
          Sem internet: a conclusão fica guardada no aparelho e segue quando a conexão voltar. A foto precisa de
          conexão.
        </p>
      )}

      <button type="button" className="acao acao--primaria" disabled={!podeConcluir} onClick={() => void concluir()}>
        {enviando ? 'Registrando…' : 'Confirmar entrega concluída'}
      </button>
      <button type="button" className="acao acao--secundaria" disabled={enviando} onClick={props.aoVoltar}>
        Voltar
      </button>

      {erro !== null ? (
        <p className="erro" role="alert">
          {mensagemDeErro(erro)}
        </p>
      ) : null}
    </>
  );
}
