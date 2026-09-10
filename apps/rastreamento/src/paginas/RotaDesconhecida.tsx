import { Link } from 'react-router';

export function RotaDesconhecida() {
  return (
    <section>
      <h2>Página não encontrada</h2>
      <p>Confira o link recebido do remetente.</p>
      <Link to="/">Ir para o início</Link>
    </section>
  );
}
