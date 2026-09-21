import { ambiente } from './ambiente';

export interface UsuarioAutenticado {
  readonly id: string;
  readonly nome: string;
  readonly email: string;
  readonly perfil: 'Administrador' | 'Supervisor' | 'Operador';
  readonly organizacaoId: string;
  readonly organizacaoNome: string;
  readonly organizacaoSlug: string;
}

export interface SessaoAtiva {
  readonly tokenDeAcesso: string;
  readonly expiraEm: string;
  readonly usuario: UsuarioAutenticado;
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
 * O token de acesso vive só na memória deste módulo — nunca em localStorage nem em
 * sessionStorage, que qualquer script da página consegue ler. O token de renovação nem
 * passa por aqui: fica no cookie HttpOnly que o navegador manda sozinho.
 *
 * Recarregar a página apaga a memória; a sessão é recuperada pela renovação, usando o
 * cookie. Ver docs/adr/0009-autenticacao-e-sessao.md.
 */
let sessaoAtual: SessaoAtiva | null = null;
let renovacaoEmCurso: Promise<SessaoAtiva | null> | null = null;
const ouvintes = new Set<Ouvinte>();

const CAMINHO_DE_AUTENTICACAO = '/api/autenticacao';

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

/** O que o servidor diz sobre a demonstração deste ambiente. */
export interface OfertaDeDemonstracao {
  readonly habilitada: boolean;
  readonly convite: string | null;
}

/**
 * Pergunta ao servidor se existe demonstração aqui.
 *
 * Quem decide é a API, não uma variável do pacote: o console publicado é o mesmo em todo lugar, e um
 * botão que aparece por configuração de build apareceria também onde a porta não existe.
 */
export async function consultarDemonstracao(): Promise<OfertaDeDemonstracao> {
  try {
    const resposta = await fetch(`${ambiente.urlDaApi}/api/demonstracao`, {
      headers: { Accept: 'application/json' },
    });

    if (!resposta.ok) {
      return { habilitada: false, convite: null };
    }

    return (await resposta.json()) as OfertaDeDemonstracao;
  } catch {
    // Sem resposta, sem botão. A tela de login continua funcionando.
    return { habilitada: false, convite: null };
  }
}

/** Abre a sessão de demonstração: o servidor faz o login pelo visitante. */
export async function entrarNaDemonstracao(): Promise<SessaoAtiva> {
  const resposta = await fetch(`${ambiente.urlDaApi}/api/demonstracao/sessao`, {
    method: 'POST',
    credentials: 'include',
    headers: { Accept: 'application/json' },
  });

  if (!resposta.ok) {
    throw await erroDaResposta(resposta);
  }

  const sessao = (await resposta.json()) as SessaoAtiva;
  definirSessao(sessao);
  return sessao;
}

/**
 * Troca o cookie de renovação por uma sessão nova.
 *
 * Só existe uma renovação em curso por vez. O servidor detecta reuso de token de
 * renovação: se duas chamadas simultâneas mandassem o mesmo cookie, a segunda poderia
 * derrubar a sessão. Quem pedir enquanto uma renovação está em andamento recebe a
 * mesma promessa.
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

export async function sair(): Promise<void> {
  try {
    await fetch(`${ambiente.urlDaApi}${CAMINHO_DE_AUTENTICACAO}/sair`, {
      method: 'POST',
      credentials: 'include',
    });
  } finally {
    // Mesmo sem resposta do servidor, a sessão local termina: o usuário pediu para sair.
    definirSessao(null);
  }
}

/**
 * Requisição com o token de acesso. Um 401 dispara uma única renovação e uma única nova
 * tentativa; se a renovação falhar, a sessão local é encerrada.
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

async function erroDaResposta(resposta: Response): Promise<ErroDeApi> {
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
