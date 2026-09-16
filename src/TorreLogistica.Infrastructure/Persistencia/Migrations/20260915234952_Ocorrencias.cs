using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace TorreLogistica.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Ocorrencias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "observacao",
                table: "operacoes_do_cliente",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ocorrencias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entrega_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rota_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motorista_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    severidade = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    motivo_da_tentativa = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    observacao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    localizacao = table.Column<Point>(type: "geography (point, 4326)", nullable: true),
                    ocorrida_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    registrada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    autor_usuario_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ocorrencias", x => x.id);
                    table.CheckConstraint("ck_ocorrencias_motivo_coerente_com_tipo", "(tipo = 'TentativaDeEntrega' AND motivo_da_tentativa IS NOT NULL) OR (tipo <> 'TentativaDeEntrega' AND motivo_da_tentativa IS NULL)");
                    table.CheckConstraint("ck_ocorrencias_observacao_quando_outro", "(tipo <> 'Outro' AND motivo_da_tentativa IS DISTINCT FROM 'Outro') OR observacao IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_ocorrencias_entregas_entrega_id",
                        column: x => x.entrega_id,
                        principalTable: "entregas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ocorrencias_motoristas_motorista_id",
                        column: x => x.motorista_id,
                        principalTable: "motoristas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ocorrencias_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ocorrencias_rotas_rota_id",
                        column: x => x.rota_id,
                        principalTable: "rotas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ocorrencias_usuarios_autor_usuario_id",
                        column: x => x.autor_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ocorrencias_autor_usuario_id",
                table: "ocorrencias",
                column: "autor_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_ocorrencias_entrega_id_critica",
                table: "ocorrencias",
                column: "entrega_id",
                filter: "severidade = 'Critica'");

            migrationBuilder.CreateIndex(
                name: "ix_ocorrencias_entrega_id_ocorrida_em",
                table: "ocorrencias",
                columns: new[] { "entrega_id", "ocorrida_em" });

            migrationBuilder.CreateIndex(
                name: "ix_ocorrencias_motorista_id",
                table: "ocorrencias",
                column: "motorista_id");

            migrationBuilder.CreateIndex(
                name: "ix_ocorrencias_organizacao_id_ocorrida_em",
                table: "ocorrencias",
                columns: new[] { "organizacao_id", "ocorrida_em" });

            migrationBuilder.CreateIndex(
                name: "ix_ocorrencias_rota_id",
                table: "ocorrencias",
                column: "rota_id");

            // Ocorrência é fato: corrigir é registrar outra, e as duas ficam. Somente-inserção.
            migrationBuilder.Sql("""
                CREATE FUNCTION impedir_alteracao_de_ocorrencia() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'O registro de ocorrências é somente-inserção.'
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trg_ocorrencias_sem_update_ou_delete
                    BEFORE UPDATE OR DELETE ON ocorrencias
                    FOR EACH ROW EXECUTE FUNCTION impedir_alteracao_de_ocorrencia();

                CREATE TRIGGER trg_ocorrencias_sem_truncate
                    BEFORE TRUNCATE ON ocorrencias
                    FOR EACH STATEMENT EXECUTE FUNCTION impedir_alteracao_de_ocorrencia();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_ocorrencias_sem_truncate ON ocorrencias;
                DROP TRIGGER IF EXISTS trg_ocorrencias_sem_update_ou_delete ON ocorrencias;
                DROP FUNCTION IF EXISTS impedir_alteracao_de_ocorrencia();
                """);

            migrationBuilder.DropTable(
                name: "ocorrencias");

            migrationBuilder.DropColumn(
                name: "observacao",
                table: "operacoes_do_cliente");
        }
    }
}
