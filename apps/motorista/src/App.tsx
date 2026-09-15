import { Route, Routes } from 'react-router';

import { IndicadorDeConexao } from './componentes/IndicadorDeConexao';
import { AreaDoMotorista } from './paginas/AreaDoMotorista';
import { DetalheDaEntrega } from './paginas/DetalheDaEntrega';
import { Entrar } from './paginas/Entrar';
import { ListaDeParadas } from './paginas/ListaDeParadas';
import { RegistrarOcorrencia } from './paginas/RegistrarOcorrencia';
import { RotaDesconhecida } from './paginas/RotaDesconhecida';
import { RotaDoDia } from './paginas/RotaDoDia';
import { RotaProtegida } from './sessao/RotaProtegida';

/**
 * PWA do motorista.
 *
 * Aplicação separada do console de propósito (ADR 0003): a interface é orientada a executar a próxima
 * parada, com ações grandes, e não a administrar a operação num celular.
 */
export function App() {
  return (
    <div className="app">
      <header className="topo">
        <span className="topo__marca">Torre Logística</span>
        <IndicadorDeConexao />
      </header>

      <main>
        <Routes>
          <Route path="/entrar" element={<Entrar />} />
          <Route element={<RotaProtegida />}>
            <Route element={<AreaDoMotorista />}>
              <Route path="/" element={<RotaDoDia />} />
              <Route path="/rotas/:rotaId/paradas" element={<ListaDeParadas />} />
              <Route path="/entregas/:entregaId" element={<DetalheDaEntrega />} />
              <Route path="/entregas/:entregaId/ocorrencia" element={<RegistrarOcorrencia />} />
            </Route>
          </Route>
          <Route path="*" element={<RotaDesconhecida />} />
        </Routes>
      </main>
    </div>
  );
}
