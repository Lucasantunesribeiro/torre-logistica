import { Route, Routes } from 'react-router';

import { Layout } from './componentes/Layout';
import { Alertas } from './paginas/Alertas';
import { DetalheDaEntrega } from './paginas/DetalheDaEntrega';
import { DetalheDoMotorista } from './paginas/DetalheDoMotorista';
import { Entrar } from './paginas/Entrar';
import { Entregas } from './paginas/Entregas';
import { Indicadores } from './paginas/Indicadores';
import { Integracoes } from './paginas/Integracoes';
import { MapaDaOperacao } from './paginas/MapaDaOperacao';
import { Motoristas } from './paginas/Motoristas';
import { Ocorrencias } from './paginas/Ocorrencias';
import { PainelOperacional } from './paginas/PainelOperacional';
import { RotaDesconhecida } from './paginas/RotaDesconhecida';
import { Rotas } from './paginas/Rotas';
import { Webhooks } from './paginas/Webhooks';
import { RotaProtegida } from './sessao/RotaProtegida';

/**
 * Casca do Console Operacional.
 *
 * A sessão é recuperada pelo cookie de renovação ao abrir; sem ela, toda rota operacional leva ao login.
 * O login fica fora da moldura de navegação — quem não entrou não tem o que navegar.
 */
export function App() {
  return (
    <Routes>
      <Route path="/entrar" element={<Entrar />} />

      <Route element={<RotaProtegida />}>
        <Route element={<Layout />}>
          <Route path="/" element={<PainelOperacional />} />
          <Route path="/mapa" element={<MapaDaOperacao />} />
          <Route path="/entregas" element={<Entregas />} />
          <Route path="/entregas/:id" element={<DetalheDaEntrega />} />
          <Route path="/rotas" element={<Rotas />} />
          <Route path="/motoristas" element={<Motoristas />} />
          <Route path="/motoristas/:id" element={<DetalheDoMotorista />} />
          <Route path="/alertas" element={<Alertas />} />
          <Route path="/ocorrencias" element={<Ocorrencias />} />
          <Route path="/indicadores" element={<Indicadores />} />
          <Route path="/integracoes" element={<Integracoes />} />
          <Route path="/webhooks" element={<Webhooks />} />
        </Route>
      </Route>

      <Route path="*" element={<RotaDesconhecida />} />
    </Routes>
  );
}
