/**
 * Painel inicial do console.
 *
 * O conteúdo operacional chega nas fases seguintes; esta página existe para que a
 * casca tenha uma rota real e o roteamento seja exercitado por teste desde já.
 */
export function Painel() {
  return (
    <section>
      <h2>Fundação técnica</h2>
      <p>
        A base do console está de pé: roteamento, estado de servidor, validação de
        configuração e ligação com a API.
      </p>
      <ul className="fases">
        <li>Identidade e multi-tenancy — Fase 1</li>
        <li>Frota e estrutura operacional — Fase 2</li>
        <li>Núcleo de entregas — Fase 3</li>
        <li>Mapa da operação — Fase 18</li>
      </ul>
    </section>
  );
}
