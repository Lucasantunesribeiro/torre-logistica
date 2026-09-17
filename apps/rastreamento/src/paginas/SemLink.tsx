/**
 * Raiz do rastreamento público, aberta sem link.
 *
 * Não há campo para procurar entrega por código: o código é identificação, não credencial, e um
 * campo de busca aqui transformaria esta página num balcão para descobrir entregas alheias.
 */
export function SemLink() {
  return (
    <section>
      <h2>Acompanhamento de entrega</h2>
      <p>
        Use o link que você recebeu do remetente. Ele abre o acompanhamento da sua entrega, sem
        precisar de cadastro ou senha.
      </p>
    </section>
  );
}
