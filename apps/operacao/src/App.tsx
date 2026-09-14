import { Route, Routes } from 'react-router';

import { IndicadorDeConexao } from './componentes/IndicadorDeConexao';
import { Entrar } from './paginas/Entrar';
import { Painel } from './paginas/Painel';
import { RotaDesconhecida } from './paginas/RotaDesconhecida';
import { RotaProtegida } from './sessao/RotaProtegida';

/**
 * Casca do Console Operacional.
 *
 * A sessão é recuperada pelo cookie de renovação ao abrir; sem ela, toda rota operacional
 * leva ao login. As telas operacionais — mapa, entregas, rotas, alertas — nascem nas fases
 * que definem o comportamento por trás delas.
 */
export function App() {
  return (
    <main>
      <h1>Console Operacional</h1>
      <p className="sumario">Torre Logística — acompanhamento de entregas em tempo real.</p>

      <div className="cartao">
        <IndicadorDeConexao />
      </div>

      <Routes>
        <Route path="/entrar" element={<Entrar />} />
        <Route element={<RotaProtegida />}>
          <Route path="/" element={<Painel />} />
        </Route>
        <Route path="*" element={<RotaDesconhecida />} />
      </Routes>
    </main>
  );
}
