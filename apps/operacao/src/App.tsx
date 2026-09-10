import { Route, Routes } from 'react-router';

import { IndicadorDeConexao } from './componentes/IndicadorDeConexao';
import { Painel } from './paginas/Painel';
import { RotaDesconhecida } from './paginas/RotaDesconhecida';

/**
 * Casca do Console Operacional.
 *
 * Na Fase 0 existe apenas a estrutura: roteamento, estado de servidor e a ligação
 * real com a API. As telas operacionais — mapa, entregas, rotas, alertas — nascem
 * nas fases que definem o comportamento por trás delas.
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
        <Route path="/" element={<Painel />} />
        <Route path="*" element={<RotaDesconhecida />} />
      </Routes>
    </main>
  );
}
