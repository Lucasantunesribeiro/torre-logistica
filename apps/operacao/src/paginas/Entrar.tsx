import { useState } from 'react';
import type { FormEvent } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router';

import { ErroDeApi } from '../infra/sessao';
import { useSessao } from '../sessao/ProvedorDeSessao';

/*
 * Mensagens por situação, nunca pelo motivo exato: a API responde igual para
 * organização, e-mail ou senha errados, e a tela não tenta adivinhar qual foi.
 */
function mensagemDoErro(erro: unknown): string {
  if (erro instanceof ErroDeApi) {
    switch (erro.status) {
      case 400:
        return 'Preencha organização, e-mail e senha.';
      case 401:
        return 'Organização, e-mail ou senha incorretos.';
      case 429:
        return 'Muitas tentativas seguidas. Aguarde um minuto e tente novamente.';
      default:
        return 'Não foi possível entrar agora. Tente novamente em instantes.';
    }
  }

  return 'Não foi possível falar com a operação. Verifique a conexão.';
}

export function Entrar() {
  const { estado, entrar } = useSessao();
  const navegar = useNavigate();
  const local = useLocation();
  const [organizacao, setOrganizacao] = useState('');
  const [email, setEmail] = useState('');
  const [senha, setSenha] = useState('');
  const [enviando, setEnviando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  const destino = (local.state as { de?: unknown } | null)?.de;
  const voltarPara = typeof destino === 'string' && destino.startsWith('/') ? destino : '/';

  if (estado.situacao === 'autenticada') {
    return <Navigate to={voltarPara} replace />;
  }

  async function enviar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    setEnviando(true);
    setErro(null);

    try {
      await entrar({ organizacao: organizacao.trim(), email: email.trim(), senha });
      void navegar(voltarPara, { replace: true });
    } catch (falha) {
      setErro(mensagemDoErro(falha));
      // A senha errada não fica no campo esperando a próxima tentativa.
      setSenha('');
    } finally {
      setEnviando(false);
    }
  }

  return (
    <section className="cartao formulario">
      <h2>Entrar no console</h2>

      <form
        onSubmit={(evento) => {
          void enviar(evento);
        }}
        noValidate
      >
        <label htmlFor="organizacao">Organização</label>
        <input
          id="organizacao"
          name="organizacao"
          autoComplete="organization"
          autoCapitalize="none"
          spellCheck={false}
          required
          value={organizacao}
          onChange={(evento) => {
            setOrganizacao(evento.target.value);
          }}
        />

        <label htmlFor="email">E-mail</label>
        <input
          id="email"
          name="email"
          type="email"
          autoComplete="username"
          required
          value={email}
          onChange={(evento) => {
            setEmail(evento.target.value);
          }}
        />

        <label htmlFor="senha">Senha</label>
        <input
          id="senha"
          name="senha"
          type="password"
          autoComplete="current-password"
          required
          value={senha}
          onChange={(evento) => {
            setSenha(evento.target.value);
          }}
        />

        {erro ? (
          <p className="formulario__erro" role="alert">
            {erro}
          </p>
        ) : null}

        <button type="submit" disabled={enviando}>
          {enviando ? 'Entrando…' : 'Entrar'}
        </button>
      </form>
    </section>
  );
}
