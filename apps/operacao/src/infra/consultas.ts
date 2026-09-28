import { QueryClient } from '@tanstack/react-query';

import { ErroDaApi } from './api';

/**
 * Cliente de estado de servidor compartilhado pela aplicação.
 *
 * `retry` com espera crescente existe para o cold start da infraestrutura de demonstração: a
 * primeira chamada depois de um período ocioso pode demorar ou falhar uma vez, e a tela não deve
 * anunciar indisponibilidade por isso.
 *
 * Mas repetir só ajuda quando o problema é passageiro. Um 4xx — sem permissão, não encontrado,
 * requisição inválida — é resposta definitiva do servidor: repeti-la três vezes só prolonga o
 * "Carregando…" antes de mostrar o erro que já se conhece. Por isso o retry pula os 4xx e insiste
 * apenas no que pode melhorar sozinho (rede, 5xx, cold start).
 */
export function criarClienteDeConsultas(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: (tentativa, erro) => {
          if (erro instanceof ErroDaApi && erro.status >= 400 && erro.status < 500) {
            return false;
          }
          return tentativa < 2;
        },
        retryDelay: (tentativa) => Math.min(1000 * 2 ** tentativa, 8000),
        staleTime: 15_000,
        refetchOnWindowFocus: false,
      },
    },
  });
}
