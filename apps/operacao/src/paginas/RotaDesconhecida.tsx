import { Link } from 'react-router';

export function RotaDesconhecida() {
  return (
    <section>
      <h2>Tela não encontrada</h2>
      <p>O endereço pedido não existe no console.</p>
      <Link to="/">Voltar ao painel</Link>
    </section>
  );
}
