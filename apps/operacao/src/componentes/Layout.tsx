import { NavLink, Outlet, useNavigate } from 'react-router';
import { useState } from 'react';

import { useSessao } from '../sessao/ProvedorDeSessao';
import { IndicadorDeConexao } from './IndicadorDeConexao';

const grupos = [
  {
    titulo: 'Operação',
    itens: [
      { para: '/', rotulo: 'Painel', fim: true },
      { para: '/mapa', rotulo: 'Mapa da operação', fim: false },
      { para: '/entregas', rotulo: 'Entregas', fim: false },
      { para: '/rotas', rotulo: 'Rotas', fim: false },
      { para: '/motoristas', rotulo: 'Motoristas', fim: false },
    ],
  },
  {
    titulo: 'Monitoramento',
    itens: [
      { para: '/alertas', rotulo: 'Alertas', fim: false },
      { para: '/ocorrencias', rotulo: 'Ocorrências', fim: false },
      { para: '/indicadores', rotulo: 'Indicadores', fim: false },
    ],
  },
  {
    titulo: 'Integração',
    // Integrações e Webhooks são administração de credenciais: o backend as protege com a política
    // que só o Administrador satisfaz. O menu respeita isso — não oferece porta que dá 403.
    perfis: ['Administrador'] as const,
    itens: [
      { para: '/integracoes', rotulo: 'Integrações', fim: false },
      { para: '/webhooks', rotulo: 'Webhooks', fim: false },
    ],
  },
] as const;

/**
 * Moldura do console: navegação fixa à esquerda, conteúdo à direita.
 *
 * A navegação é uma lista curta e sempre visível, sem menus que escondem seções: quem opera precisa
 * saltar entre mapa, entregas e alertas em um clique, não em três.
 */
export function Layout() {
  const { estado, sair } = useSessao();
  const navegar = useNavigate();
  const [saindo, setSaindo] = useState(false);

  const usuario = estado.situacao === 'autenticada' ? estado.usuario : null;

  async function encerrar() {
    setSaindo(true);
    try {
      await sair();
    } finally {
      void navegar('/entrar', { replace: true });
    }
  }

  return (
    <div className="console">
      <nav className="console__navegacao" aria-label="Seções do console">
        <p className="console__marca">Torre Logística</p>

        <div className="console__grupos">
          {grupos
            .filter(
              (grupo) =>
                !('perfis' in grupo) ||
                (usuario !== null && (grupo.perfis as readonly string[]).includes(usuario.perfil)),
            )
            .map((grupo) => (
              <div className="console__grupo" key={grupo.titulo}>
                <p className="console__grupo-titulo">{grupo.titulo}</p>
                <ul>
                  {grupo.itens.map((secao) => (
                    <li key={secao.para}>
                      <NavLink to={secao.para} end={secao.fim}>
                        {secao.rotulo}
                      </NavLink>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
        </div>

        <IndicadorDeConexao />
      </nav>

      <div className="console__conteudo">
        <header className="console__cabecalho">
          {usuario === null ? null : (
            <p className="console__usuario">
              <strong>{usuario.nome}</strong>
              <span>
                {usuario.perfil} · {usuario.organizacaoNome}
              </span>
            </p>
          )}

          <button
            type="button"
            disabled={saindo}
            onClick={() => {
              void encerrar();
            }}
          >
            {saindo ? 'Saindo…' : 'Sair'}
          </button>
        </header>

        <main>
          <Outlet />
        </main>
      </div>
    </div>
  );
}
