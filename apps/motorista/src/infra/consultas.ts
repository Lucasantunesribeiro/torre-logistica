import { QueryClient } from '@tanstack/react-query';

import { ErroDeApi } from './sessao';

/**
 * Cliente de estado de servidor compartilhado pela aplicação.
 *
 * Até duas novas tentativas com espera crescente, para o cold start da infraestrutura de demonstração e
 * para a rede instável da rua. Resposta 4xx não é repetida: entrega passada a outro motorista ou item
 * inexistente não mudam por insistência, e o motorista precisa ver a explicação na hora.
 */
export function criarClienteDeConsultas(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: (falhas, erro) => !(erro instanceof ErroDeApi && erro.status < 500) && falhas < 2,
        retryDelay: (tentativa) => Math.min(1000 * 2 ** tentativa, 8000),
        staleTime: 15_000,
        refetchOnWindowFocus: true,
      },
    },
  });
}
