import { z } from 'zod';

/**
 * Aceita apenas endereço HTTP(S) absoluto.
 *
 * Checar só "é uma URL" não basta: `new URL('localhost:5080')` é válido — o
 * analisador entende `localhost:` como esquema e `5080` como caminho. O valor
 * passaria na validação e produziria requisições para um endereço inexistente,
 * com erro de rede no navegador e nenhuma pista da causa.
 */
function ehEnderecoHttpAbsoluto(valor: string): boolean {
  let endereco: URL;

  try {
    endereco = new URL(valor);
  } catch {
    return false;
  }

  return (
    (endereco.protocol === 'http:' || endereco.protocol === 'https:') &&
    endereco.hostname.length > 0
  );
}

/**
 * Configuração que a aplicação recebe no momento da build.
 *
 * Só variáveis com o prefixo `VITE_` chegam ao navegador — e tudo o que chega fica
 * embutido no pacote publicado, legível por qualquer visitante. Por isso aqui só
 * entra endereço público; nenhum segredo.
 */
const esquemaDoAmbiente = z.object({
  VITE_URL_DA_API: z
    .string()
    .refine(
      ehEnderecoHttpAbsoluto,
      'precisa ser um endereço http(s) absoluto, por exemplo http://localhost:5080',
    ),

  /**
   * Estilo do mapa, do provedor que a implantação escolher.
   *
   * Opcional de propósito: sem ele o Mapa da Operação desenha os pontos sobre fundo neutro, e o console
   * funciona sem depender de contratar fornecedor de tiles.
   */
  VITE_URL_DO_ESTILO_DO_MAPA: z.string().optional(),
});

export interface Ambiente {
  readonly urlDaApi: string;
  readonly urlDoEstiloDoMapa: string | null;
}

/**
 * Valida a configuração recebida e devolve a forma usada pela aplicação.
 *
 * @throws {Error} quando a configuração é inválida — de propósito, na importação do
 * módulo: uma build mal configurada falha de imediato, em vez de quebrar depois numa
 * tela qualquer, longe da causa.
 */
export function lerAmbiente(bruto: unknown): Ambiente {
  const resultado = esquemaDoAmbiente.safeParse(bruto);

  if (!resultado.success) {
    const problemas = resultado.error.issues
      .map((problema) => `${problema.path.join('.')}: ${problema.message}`)
      .join('; ');

    throw new Error(`Configuração de ambiente inválida — ${problemas}`);
  }

  const estilo = resultado.data.VITE_URL_DO_ESTILO_DO_MAPA?.trim();

  return {
    // A barra final é removida para que a junção com o caminho nunca produza "//".
    urlDaApi: resultado.data.VITE_URL_DA_API.replace(/\/+$/, ''),
    urlDoEstiloDoMapa: estilo === undefined || estilo === '' ? null : estilo,
  };
}

export const ambiente = lerAmbiente(import.meta.env);
