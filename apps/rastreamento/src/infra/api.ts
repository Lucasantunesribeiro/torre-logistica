import { z } from 'zod';

import { ambiente } from './ambiente';

const esquemaDeStatus = z.enum([
  'Criada',
  'Planejada',
  'Atribuida',
  'EmRota',
  'ProximaDoDestino',
  'Entregue',
  'TentativaFrustrada',
  'Reagendada',
  'Cancelada',
]);

const esquemaDeSituacao = z.enum(['Normal', 'Atencao', 'Risco', 'Atrasada']);

const esquemaDeMarco = z.object({
  tipo: z.enum([
    'Criada',
    'SaiuParaRota',
    'ProximidadeDetectada',
    'ChegadaRegistrada',
    'Entregue',
    'TentativaFrustrada',
    'Reagendada',
    'Cancelada',
  ]),
  ocorridoEm: z.string(),
});

const esquemaDeAcompanhamento = z.object({
  codigo: z.string(),
  status: esquemaDeStatus,
  janelaDe: z.string(),
  janelaAte: z.string(),
  destino: z.object({ bairro: z.string(), cidade: z.string(), uf: z.string() }),
  chegadaPrevistaEm: z.string().nullable(),
  situacao: esquemaDeSituacao.nullable(),
  posicao: z
    .object({
      latitude: z.number(),
      longitude: z.number(),
      precisaoAproximadaEmMetros: z.number().int(),
      atualizadaEm: z.string(),
    })
    .nullable(),
  comprovante: z
    .object({
      recebidoPor: z.string(),
      registradoEm: z.string(),
      arquivos: z.array(
        z.object({
          tipo: z.enum(['Foto', 'Assinatura']),
          url: z.string(),
          urlExpiraEm: z.string(),
        }),
      ),
    })
    .nullable(),
  marcos: z.array(esquemaDeMarco),
  consultadoEm: z.string(),
});

export type StatusDaEntrega = z.infer<typeof esquemaDeStatus>;
export type SituacaoDoSla = z.infer<typeof esquemaDeSituacao>;
export type Marco = z.infer<typeof esquemaDeMarco>;
export type Acompanhamento = z.infer<typeof esquemaDeAcompanhamento>;

/**
 * Resultado da consulta como união fechada, em vez de exceção com a mensagem do servidor.
 *
 * A página pública nunca repassa texto vindo da API: quem escolhe o que o destinatário lê é ela.
 * Assim, um erro qualquer do servidor não vira frase na tela de quem não faz parte da operação —
 * e `naoEncontrado` cobre link inválido, expirado e revogado sem distinção, porque a API também
 * não distingue.
 */
export type ResultadoDoAcompanhamento =
  | { readonly estado: 'ok'; readonly dados: Acompanhamento }
  | { readonly estado: 'naoEncontrado' }
  | { readonly estado: 'falha' };

/** Consulta o acompanhamento pelo token do link. */
export async function consultarAcompanhamento(
  token: string,
  sinal?: AbortSignal,
): Promise<ResultadoDoAcompanhamento> {
  let resposta: Response;

  try {
    resposta = await fetch(
      `${ambiente.urlDaApi}/api/publico/rastreamento/${encodeURIComponent(token)}`,
      { headers: { Accept: 'application/json' }, ...(sinal ? { signal: sinal } : {}) },
    );
  } catch {
    return { estado: 'falha' };
  }

  if (resposta.status === 404) {
    return { estado: 'naoEncontrado' };
  }

  if (!resposta.ok) {
    return { estado: 'falha' };
  }

  const analisado = esquemaDeAcompanhamento.safeParse(await resposta.json());

  return analisado.success ? { estado: 'ok', dados: analisado.data } : { estado: 'falha' };
}
