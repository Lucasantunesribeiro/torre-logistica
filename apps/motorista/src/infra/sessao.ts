import { ambiente } from './ambiente';

export interface MotoristaAutenticado {
  readonly id: string;
  readonly nome: string;
  readonly email: string;
  readonly perfil: 'Motorista';
  readonly organizacaoId: string;
  readonly organizacaoNome: string;
  readonly organizacaoSlug: string;
}

export interface SessaoAtiva {
  readonly tokenDeAcesso: string;
  readonly expiraEm: string;
  readonly usuario: MotoristaAutenticado;
}

export interface Credenciais {
  readonly organizacao: string;
  readonly email: string;
  readonly senha: string;
}

/** Falha devolvida pela API, com o código estável do ProblemDetails. */
export class ErroDeApi extends Error {
  constructor(
    readonly status: number,
    readonly codigo: string | null,
    mensagem: string,
  ) {
    super(mensagem);
    this.name = 'ErroDeApi';
  }
}

type Ouvinte = (sessao: SessaoAtiva | null) => void;

/*
 * Mesma estratégia do console (ADR 0009), no canal do motorista: o token de acesso vive só na memória
 * deste módulo — nunca em localStorage —, e o token de renovação fica no cookie HttpOnly do caminho
 * /api/motorista/autenticacao, que o navegador manda sozinho. O console e a PWA não compartilham sessão:
 * cookie, emissor e audiência são de canais diferentes.
 *
 * O código é paralelo ao do console de propósito: as aplicações são implantadas separadamente (ADR 0003),
 * e um pacote compartilhado só se paga quando houver um terceiro consumidor com sessão.
 */
let sessaoAtual: SessaoAtiva | null = null;
let renovacaoEmCurso: Promise<SessaoAtiva | null> | null = null;
const ouvintes = new Set<Ouvinte>();

export const CAMINHO_DE_AUTENTICACAO = '/api/motorista/autenticacao';

export function obterSessao(): SessaoAtiva | null {
  return sessaoAtual;
}

export function assinarSessao(ouvinte: Ouvinte): () => void {
  ouvintes.add(ouvinte);
  return () => {
    ouvintes.delete(ouvinte);
  };
}

function definirSessao(sessao: SessaoAtiva | null): void {
  sessaoAtual = sessao;
  for (const ouvinte of ouvintes) {
    ouvinte(sessao);
  }
}

export async function entrar(credenciais: Credenciais): Promise<SessaoAtiva> {
  const resposta = await fetch(`${ambiente.urlDaApi}${CAMINHO_DE_AUTENTICACAO}/login`, {
    method: 'POST',
    // Sem "include" o navegador descarta o Set-Cookie de uma resposta de outra origem.
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify(credenciais),
  });

  if (!resposta.ok) {
    throw await erroDaResposta(resposta);
  }

  const sessao = (await resposta.json()) as SessaoAtiva;
  definirSessao(sessao);
  return sessao;
}

/**
 * Troca o cookie de renovação por uma sessão nova — uma por vez. O servidor trata reapresentação de token
 * de renovação como possível roubo; duas renovações simultâneas com o mesmo cookie derrubariam a sessão.
 */
export function renovarSessao(): Promise<SessaoAtiva | null> {
  renovacaoEmCurso ??= (async () => {
    try {
      const resposta = await fetch(`${ambiente.urlDaApi}${CAMINHO_DE_AUTENTICACAO}/renovar`, {
        method: 'POST',
        credentials: 'include',
        headers: { Accept: 'application/json' },
      });

      if (!resposta.ok) {
        definirSessao(null);
        return null;
      }

      const sessao = (await resposta.json()) as SessaoAtiva;
      definirSessao(sessao);
      return sessao;
    } finally {
      renovacaoEmCurso = null;
    }
  })();

  return renovacaoEmCurso;
}

/**
 * Encerra a sessão. Devolve se o servidor confirmou: sem rede, o cookie de renovação continua válido lá, e
 * quem chama precisa encerrar de novo quando a conexão voltar.
 */
export async function sair(): Promise<boolean> {
  try {
    const resposta = await fetch(`${ambiente.urlDaApi}${CAMINHO_DE_AUTENTICACAO}/sair`, {
      method: 'POST',
      credentials: 'include',
    });
    // 401: a sessão já não valia no servidor — está encerrada do mesmo jeito.
    return resposta.ok || resposta.status === 401;
  } catch {
    return false;
  } finally {
    // Mesmo sem resposta do servidor, a sessão local termina: o motorista pediu para sair.
    definirSessao(null);
  }
}

/**
 * Requisição com o token de acesso. Um 401 dispara uma única renovação e uma única nova tentativa; se a
 * renovação falhar, a sessão local é encerrada e a interface volta ao login.
 */
export async function requisicaoAutenticada(caminho: string, opcoes: RequestInit = {}): Promise<Response> {
  const enviar = (token: string) =>
    fetch(`${ambiente.urlDaApi}${caminho}`, {
      ...opcoes,
      headers: { Accept: 'application/json', ...opcoes.headers, Authorization: `Bearer ${token}` },
    });

  const sessao = sessaoAtual ?? (await renovarSessao());
  if (!sessao) {
    throw new ErroDeApi(401, 'nao_autenticado', 'A sessão foi encerrada.');
  }

  const resposta = await enviar(sessao.tokenDeAcesso);
  if (resposta.status !== 401) {
    return resposta;
  }

  const renovada = await renovarSessao();
  if (!renovada) {
    throw new ErroDeApi(401, 'nao_autenticado', 'A sessão foi encerrada.');
  }

  return enviar(renovada.tokenDeAcesso);
}

export async function erroDaResposta(resposta: Response): Promise<ErroDeApi> {
  let codigo: string | null = null;
  let detalhe = 'Não foi possível concluir a operação.';

  try {
    const corpo = (await resposta.json()) as { codigo?: unknown; detail?: unknown };
    codigo = typeof corpo.codigo === 'string' ? corpo.codigo : null;
    detalhe = typeof corpo.detail === 'string' ? corpo.detail : detalhe;
  } catch {
    // Corpo ausente ou fora do formato: fica o status.
  }

  return new ErroDeApi(resposta.status, codigo, detalhe);
}
