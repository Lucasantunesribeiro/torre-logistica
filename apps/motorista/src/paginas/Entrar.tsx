import { useState } from 'react';
import type { FormEvent } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router';

import { ErroDeApi } from '../infra/sessao';
import { useSessao } from '../sessao/ProvedorDeSessao';

// Mensagem pela situação, nunca pelo motivo exato: a API responde igual para organização, e-mail ou senha errados.
function mensagemDoErro(erro: unknown): string {
  if (erro instanceof ErroDeApi) {
    switch (erro.status) {
      case 400:
        return 'Preencha organização, e-mail e senha.';
      case 401:
        return 'Organização, e-mail ou senha incorretos.';
      case 429:
        return 'Muitas tentativas seguidas. Aguarde um minuto e tente de novo.';
      default:
        return 'Não foi possível entrar agora. Tente de novo em instantes.';
    }
  }

  return 'Sem conexão com a operação. Verifique a internet.';
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
      setSenha('');
    } finally {
      setEnviando(false);
    }
  }

  return (
    <section className="formulario">
      <h2>Entrar no aplicativo</h2>
      <p className="sumario">Use a conta de motorista que a operação cadastrou para você.</p>

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
          inputMode="email"
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
          <p className="erro" role="alert">
            {erro}
          </p>
        ) : null}

        <button type="submit" className="acao acao--primaria" disabled={enviando}>
          {enviando ? 'Entrando…' : 'Entrar'}
        </button>
      </form>
    </section>
  );
}
