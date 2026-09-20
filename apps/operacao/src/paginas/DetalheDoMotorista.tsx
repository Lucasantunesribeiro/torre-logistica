import { useQuery } from '@tanstack/react-query';
import { useParams } from 'react-router';

import { Estado, Selo, instante } from '../componentes/Estado';
import { ErroDaApi, obterMotorista, obterPosicaoAtual } from '../infra/api';

/**
 * Detalhe do motorista, com a última posição conhecida.
 *
 * "Ainda não há posição" não é erro: motorista recém-cadastrado, ou que não saiu hoje, simplesmente não
 * tem GPS enviado. A tela diz isso em vez de mostrar falha.
 */
export function DetalheDoMotorista() {
  const { id = '' } = useParams<{ id: string }>();

  const motorista = useQuery({
    queryKey: ['motoristas', id],
    queryFn: () => obterMotorista(id),
  });

  const posicao = useQuery({
    queryKey: ['posicao', id],
    queryFn: () => obterPosicaoAtual(id),
    retry: false,
  });

  const semPosicao = posicao.error instanceof ErroDaApi && posicao.error.status === 404;

  return (
    <section>
      <Estado carregando={motorista.isPending} erro={motorista.error}>
        <h2>{motorista.data?.nome}</h2>
        <p className="sumario">
          {motorista.data?.telefone ?? 'sem telefone'} ·{' '}
          <Selo tom={motorista.data?.ativo === true ? 'bom' : 'neutro'}>
            {motorista.data?.ativo === true ? 'Ativo' : 'Inativo'}
          </Selo>
        </p>

        <h3>Última posição</h3>

        {semPosicao ? (
          <p className="estado">Este motorista ainda não enviou posição.</p>
        ) : (
          <Estado carregando={posicao.isPending} erro={posicao.error}>
            <dl className="dados">
              <dt>Coordenada</dt>
              <dd>
                {posicao.data?.latitude.toFixed(5)}, {posicao.data?.longitude.toFixed(5)}
              </dd>

              <dt>Precisão</dt>
              <dd>{posicao.data?.precisaoEmMetros.toFixed(0)} m</dd>

              <dt>Capturada em</dt>
              <dd>{instante(posicao.data?.capturadaEm ?? null)}</dd>

              <dt>Recebida em</dt>
              <dd>{instante(posicao.data?.recebidaEm ?? null)}</dd>
            </dl>
          </Estado>
        )}
      </Estado>
    </section>
  );
}
