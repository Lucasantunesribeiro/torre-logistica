import { ambiente } from './ambiente';

/** Cabeçalho que amarra a chamada do navegador ao processamento no servidor. */
export const CABECALHO_DE_CORRELACAO = 'X-Correlation-Id';

export interface Prontidao {
  readonly status: string;
  readonly duracaoEmMs: number;
}

export interface ResultadoDaProntidao {
  readonly disponivel: boolean;
  readonly status: string;
  readonly idDeCorrelacao: string | null;
}

/**
 * Consulta o endpoint de prontidão da API.
 *
 * Um 503 aqui é resposta legítima: a API está de pé e avisando que não está pronta.
 * Isso é diferente de não conseguir falar com ela, e a interface precisa distinguir
 * os dois casos para não mostrar "fora do ar" quando o banco só está acordando.
 */
export async function consultarProntidao(sinal?: AbortSignal): Promise<ResultadoDaProntidao> {
  const resposta = await fetch(`${ambiente.urlDaApi}/health/ready`, {
    headers: { Accept: 'application/json' },
    ...(sinal ? { signal: sinal } : {}),
  });

  const idDeCorrelacao = resposta.headers.get(CABECALHO_DE_CORRELACAO);

  return {
    disponivel: resposta.ok,
    status: (await lerCorpo(resposta))?.status ?? 'Desconhecido',
    idDeCorrelacao,
  };
}

/**
 * Lê o corpo como JSON tolerando resposta sem corpo ou com corpo inesperado.
 *
 * Um proxy ou gateway no caminho pode responder com HTML de erro; deixar isso virar
 * exceção transformaria "a API respondeu algo estranho" em "a rede caiu".
 */
async function lerCorpo(resposta: Response): Promise<Prontidao | null> {
  try {
    return (await resposta.json()) as Prontidao;
  } catch {
    return null;
  }
}
