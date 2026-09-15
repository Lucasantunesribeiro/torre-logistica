import { Navigate, Outlet, useLocation } from 'react-router';

import { useSessao } from './ProvedorDeSessao';

/**
 * Só deixa passar com sessão. Conveniência de navegação, não controle de acesso: quem autoriza é a API,
 * em cada requisição.
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
