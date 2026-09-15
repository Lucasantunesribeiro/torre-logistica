using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreLogistica.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class OperacoesDoCliente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operacoes_do_cliente",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motorista_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operacao_do_cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    alvo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motivo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    criada_no_aparelho_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    recebida_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    resultado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    mensagem = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operacoes_do_cliente", x => x.id);
                    table.CheckConstraint("ck_operacoes_do_cliente_codigo_do_desfecho", "(resultado = 'Aplicada' AND codigo IS NULL) OR (resultado <> 'Aplicada' AND codigo IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_operacoes_do_cliente_motoristas_motorista_id",
                        column: x => x.motorista_id,
                        principalTable: "motoristas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_operacoes_do_cliente_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_operacoes_do_cliente_alvo_id_recebida_em",
                table: "operacoes_do_cliente",
                columns: new[] { "alvo_id", "recebida_em" });

            migrationBuilder.CreateIndex(
                name: "ix_operacoes_do_cliente_motorista_id",
                table: "operacoes_do_cliente",
                column: "motorista_id");

            migrationBuilder.CreateIndex(
                name: "ux_operacoes_do_cliente_operacao",
                table: "operacoes_do_cliente",
                columns: new[] { "organizacao_id", "motorista_id", "operacao_do_cliente_id" },
                unique: true);

            // O registro é o que responde a uma repetição: alterá-lo mudaria o desfecho de uma operação já
            // processada. Somente-inserção.
            migrationBuilder.Sql("""
                CREATE FUNCTION impedir_alteracao_de_operacao_do_cliente() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'O registro de operações do aparelho é somente-inserção.'
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trg_operacoes_do_cliente_sem_update_ou_delete
                    BEFORE UPDATE OR DELETE ON operacoes_do_cliente
                    FOR EACH ROW EXECUTE FUNCTION impedir_alteracao_de_operacao_do_cliente();

                CREATE TRIGGER trg_operacoes_do_cliente_sem_truncate
                    BEFORE TRUNCATE ON operacoes_do_cliente
                    FOR EACH STATEMENT EXECUTE FUNCTION impedir_alteracao_de_operacao_do_cliente();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_operacoes_do_cliente_sem_truncate ON operacoes_do_cliente;
                DROP TRIGGER IF EXISTS trg_operacoes_do_cliente_sem_update_ou_delete ON operacoes_do_cliente;
                DROP FUNCTION IF EXISTS impedir_alteracao_de_operacao_do_cliente();
                """);

            migrationBuilder.DropTable(
                name: "operacoes_do_cliente");
        }
    }
}
