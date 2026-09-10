/**
 * Página pública de acompanhamento.
 *
 * O acesso por token forte, a timeline e a política de privacidade de localização
 * são assunto da Fase 15. Nesta fase existe apenas a casca pública, já separada das
 * aplicações autenticadas — o endpoint público nunca compartilha sessão com elas.
 */
export function Acompanhamento() {
  return (
    <section>
      <h2>Acompanhamento de entrega</h2>
      <p>
        Para acompanhar uma entrega é necessário o link recebido do remetente. O
        acesso por token chega na Fase 15.
      </p>
    </section>
  );
}
