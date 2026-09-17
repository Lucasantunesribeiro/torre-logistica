import { Route, Routes } from 'react-router';

import { Acompanhamento } from './paginas/Acompanhamento';
import { RotaDesconhecida } from './paginas/RotaDesconhecida';
import { SemLink } from './paginas/SemLink';

/**
 * Casca do rastreamento público.
 *
 * Não há indicador de conexão com a operação aqui: esta página é para o
 * destinatário da entrega e não deve expor estado interno do sistema.
 */
export function App() {
  return (
    <main>
      <h1>Acompanhe sua entrega</h1>
      <p className="sumario">Torre Logística.</p>

      <Routes>
        <Route path="/" element={<SemLink />} />
        <Route path="/e/:token" element={<Acompanhamento />} />
        <Route path="*" element={<RotaDesconhecida />} />
      </Routes>
    </main>
  );
}
