import { useQuery } from '@tanstack/react-query';

import { consultarProntidao } from '../infra/clienteDaApi';

type Estado = 'carregando' | 'pronta' | 'indisponivel' | 'inalcancavel';

const rotulos: Record<Estado, string> = {
  carregando: 'Verificando a conexão com a operação…',
  pronta: 'Operação conectada',
  indisponivel: 'Operação respondendo, dependências ainda subindo',
  inalcancavel: 'Não foi possível falar com a operação',
};

/**
 * Mostra o estado da ligação com a API.
 *
 * Três estados e não dois, de propósito. "Respondeu que não está pronta" é
 * diferente de "não respondeu": o primeiro acontece durante o cold start da
 * infraestrutura de demonstração e passa sozinho; o segundo é problema de verdade.
 * Tratar os dois como "fora do ar" faria a demo parecer quebrada ao ser aberta.
 */
export function IndicadorDeConexao() {
  const consulta = useQuery({
    queryKey: ['prontidao'],
    queryFn: ({ signal }) => consultarProntidao(signal),
  });

  let estado: Estado = 'carregando';
  if (consulta.isError) {
    estado = 'inalcancavel';
  } else if (consulta.data) {
    estado = consulta.data.disponivel ? 'pronta' : 'indisponivel';
  }

  return (
    <p className={`conexao conexao--${estado}`} role="status" aria-live="polite">
      <span className="conexao__marca" aria-hidden="true" />
      {rotulos[estado]}
    </p>
  );
}
