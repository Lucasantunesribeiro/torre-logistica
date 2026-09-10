import { QueryClient } from '@tanstack/react-query';

/**
 * Cliente de estado de servidor compartilhado pela aplicação.
 *
 * `retry: 2` com espera crescente existe para o cold start da infraestrutura de
 * demonstração: a primeira chamada depois de um período ocioso pode demorar ou
 * falhar uma vez, e a tela não deve anunciar indisponibilidade por isso.
 */
export function criarClienteDeConsultas(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: 2,
        retryDelay: (tentativa) => Math.min(1000 * 2 ** tentativa, 8000),
        staleTime: 15_000,
        refetchOnWindowFocus: false,
      },
    },
  });
}
