import { useSyncExternalStore } from 'react';
import { useQuery } from '@tanstack/react-query';

import { consultarProntidao } from '../infra/clienteDaApi';

type Estado = 'offline' | 'carregando' | 'pronta' | 'indisponivel' | 'inalcancavel';

const rotulos: Record<Estado, string> = {
  offline: 'Sem internet no aparelho',
  carregando: 'Verificando a conexão com a operação…',
  pronta: 'Operação conectada',
  indisponivel: 'Operação respondendo, dependências ainda subindo',
  inalcancavel: 'Não foi possível falar com a operação',
};

function assinarConexao(aviso: () => void): () => void {
  window.addEventListener('online', aviso);
  window.addEventListener('offline', aviso);
  return () => {
    window.removeEventListener('online', aviso);
    window.removeEventListener('offline', aviso);
  };
}

/**
 * Estado da ligação com a operação, em quatro situações: sem internet no aparelho, operação pronta,
 * operação acordando e operação inalcançável. "Sem internet" vem do próprio navegador e aparece na hora,
 * antes de qualquer requisição falhar.
 */
export function IndicadorDeConexao() {
  const online = useSyncExternalStore(assinarConexao, () => navigator.onLine, () => true);

  const consulta = useQuery({
    queryKey: ['prontidao'],
    queryFn: ({ signal }) => consultarProntidao(signal),
    enabled: online,
    refetchInterval: 60_000,
  });

  let estado: Estado = 'carregando';
  if (!online) {
    estado = 'offline';
  } else if (consulta.isError) {
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
