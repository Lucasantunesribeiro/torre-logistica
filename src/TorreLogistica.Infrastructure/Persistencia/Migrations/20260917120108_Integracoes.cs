using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreLogistica.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Integracoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integracoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    identificador_publico = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    hash_do_segredo = table.Column<byte[]>(type: "bytea", nullable: false),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    autor_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revogada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    revogada_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ultimo_uso_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integracoes", x => x.id);
                    table.CheckConstraint("ck_integracoes_hash_tem_32_bytes", "octet_length(hash_do_segredo) = 32");
                    table.ForeignKey(
                        name: "fk_integracoes_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "referencias_externas_de_entrega",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    integracao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entrega_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identificador_externo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_referencias_externas_de_entrega", x => x.id);
                    table.ForeignKey(
                        name: "fk_referencias_externas_de_entrega_entregas_entrega_id",
                        column: x => x.entrega_id,
                        principalTable: "entregas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_referencias_externas_de_entrega_integracoes_integracao_id",
                        column: x => x.integracao_id,
                        principalTable: "integracoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "requisicoes_de_integracao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    integracao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chave = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    hash_da_requisicao = table.Column<byte[]>(type: "bytea", nullable: false),
                    recurso = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    recurso_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requisicoes_de_integracao", x => x.id);
                    table.CheckConstraint("ck_requisicoes_de_integracao_hash_tem_32_bytes", "octet_length(hash_da_requisicao) = 32");
                    table.ForeignKey(
                        name: "fk_requisicoes_de_integracao_integracoes_integracao_id",
                        column: x => x.integracao_id,
                        principalTable: "integracoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_integracoes_organizacao_id",
                table: "integracoes",
                column: "organizacao_id");

            migrationBuilder.CreateIndex(
                name: "ux_integracoes_identificador_publico",
                table: "integracoes",
                column: "identificador_publico",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_referencias_externas_de_entrega_entrega_id",
                table: "referencias_externas_de_entrega",
                column: "entrega_id");

            migrationBuilder.CreateIndex(
                name: "ix_referencias_externas_de_entrega_integracao_id",
                table: "referencias_externas_de_entrega",
                column: "integracao_id");

            migrationBuilder.CreateIndex(
                name: "ux_referencias_externas_identificador",
                table: "referencias_externas_de_entrega",
                columns: new[] { "organizacao_id", "integracao_id", "identificador_externo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_requisicoes_de_integracao_integracao_id",
                table: "requisicoes_de_integracao",
                column: "integracao_id");

            migrationBuilder.CreateIndex(
                name: "ux_requisicoes_de_integracao_chave",
                table: "requisicoes_de_integracao",
                columns: new[] { "organizacao_id", "integracao_id", "chave" },
                unique: true);

            // Registro de idempotência que pode ser alterado deixa de ser garantia: bastaria um UPDATE no
            // hash para o mesmo pedido entrar duas vezes. Somente-inserção, como a timeline e o comprovante.
            migrationBuilder.Sql("""
                CREATE FUNCTION impedir_alteracao_de_registro_de_integracao() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'O registro de integração é somente-inserção.'
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trg_requisicoes_de_integracao_sem_update_ou_delete
                    BEFORE UPDATE OR DELETE ON requisicoes_de_integracao
                    FOR EACH ROW EXECUTE FUNCTION impedir_alteracao_de_registro_de_integracao();

                CREATE TRIGGER trg_requisicoes_de_integracao_sem_truncate
                    BEFORE TRUNCATE ON requisicoes_de_integracao
                    FOR EACH STATEMENT EXECUTE FUNCTION impedir_alteracao_de_registro_de_integracao();

                CREATE TRIGGER trg_referencias_externas_sem_update_ou_delete
                    BEFORE UPDATE OR DELETE ON referencias_externas_de_entrega
                    FOR EACH ROW EXECUTE FUNCTION impedir_alteracao_de_registro_de_integracao();

                CREATE TRIGGER trg_referencias_externas_sem_truncate
                    BEFORE TRUNCATE ON referencias_externas_de_entrega
                    FOR EACH STATEMENT EXECUTE FUNCTION impedir_alteracao_de_registro_de_integracao();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_referencias_externas_sem_truncate ON referencias_externas_de_entrega;
                DROP TRIGGER IF EXISTS trg_referencias_externas_sem_update_ou_delete ON referencias_externas_de_entrega;
                DROP TRIGGER IF EXISTS trg_requisicoes_de_integracao_sem_truncate ON requisicoes_de_integracao;
                DROP TRIGGER IF EXISTS trg_requisicoes_de_integracao_sem_update_ou_delete ON requisicoes_de_integracao;
                DROP FUNCTION IF EXISTS impedir_alteracao_de_registro_de_integracao();
                """);

            migrationBuilder.DropTable(
                name: "referencias_externas_de_entrega");

            migrationBuilder.DropTable(
                name: "requisicoes_de_integracao");

            migrationBuilder.DropTable(
                name: "integracoes");
        }
    }
}
