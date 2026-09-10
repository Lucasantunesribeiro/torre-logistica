import { Link } from 'react-router';

export function RotaDesconhecida() {
  return (
    <section>
      <h2>Tela não encontrada</h2>
      <p>Este endereço não existe no aplicativo do motorista.</p>
      <Link to="/">Voltar para a rota do dia</Link>
    </section>
  );
}
