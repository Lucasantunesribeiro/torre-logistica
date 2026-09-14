import { Navigate, Outlet, useLocation } from 'react-router';

import { useSessao } from './ProvedorDeSessao';

/**
 * Só deixa passar com sessão.
 *
 * Isto é conveniência de navegação, não controle de acesso: esconder a tela não protege
 * dado nenhum. Quem autoriza é a API, em cada requisição.
 */
export function RotaProtegida() {
  const { estado } = useSessao();
  const local = useLocation();

  if (estado.situacao === 'verificando') {
    return (
      <p role="status" aria-live="polite">
        Verificando a sessão…
      </p>
    );
  }

  if (estado.situacao === 'anonima') {
    return <Navigate to="/entrar" replace state={{ de: local.pathname }} />;
  }

  return <Outlet />;
}
