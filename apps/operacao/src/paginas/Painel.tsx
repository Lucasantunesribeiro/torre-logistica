import { useState } from 'react';
import { useNavigate } from 'react-router';

import { useSessao } from '../sessao/ProvedorDeSessao';

/**
 * Painel inicial do console.
 *
 * O conteúdo operacional chega nas fases seguintes. Nesta fase o painel mostra quem está
 * na sessão e em qual organização — a prova visível de que a identidade veio da API.
 */
export function Painel() {
  const { estado, sair } = useSessao();
  const navegar = useNavigate();
  const [saindo, setSaindo] = useState(false);

  if (estado.situacao !== 'autenticada') {
    return null;
  }

  const { usuario } = estado;

  async function encerrar() {
    setSaindo(true);
    try {
      await sair();
    } finally {
      void navegar('/entrar', { replace: true });
    }
  }

  return (
    <section>
      <div className="sessao">
        <div>
          <h2>{usuario.nome}</h2>
          <p className="sumario">
            {usuario.perfil} · {usuario.organizacaoNome}
          </p>
        </div>
        <button
          type="button"
          disabled={saindo}
          onClick={() => {
            void encerrar();
          }}
        >
          {saindo ? 'Saindo…' : 'Sair'}
        </button>
      </div>

      <ul className="fases">
        <li>Frota e estrutura operacional — Fase 2</li>
        <li>Núcleo de entregas — Fase 3</li>
        <li>Mapa da operação — Fase 18</li>
      </ul>
    </section>
  );
}
