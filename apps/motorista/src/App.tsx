import { Route, Routes } from 'react-router';

import { IndicadorDeConexao } from './componentes/IndicadorDeConexao';
import { RotaDesconhecida } from './paginas/RotaDesconhecida';
import { RotaDoDia } from './paginas/RotaDoDia';

/**
 * Casca da PWA do motorista.
 *
 * Aplicação separada do console de propósito: a interface do motorista é orientada
 * a executar a próxima parada, não a administrar a operação. Compartilhar a casca
 * com o console levaria, na prática, a um painel administrativo comprimido no celular.
 */
export function App() {
  return (
    <main>
      <h1>Torre Logística</h1>
      <p className="sumario">Aplicativo do motorista.</p>

      <div className="cartao">
        <IndicadorDeConexao />
      </div>

      <Routes>
        <Route path="/" element={<RotaDoDia />} />
        <Route path="*" element={<RotaDesconhecida />} />
      </Routes>
    </main>
  );
}
