using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace TorreLogistica.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Comprovantes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "comprovantes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entrega_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rota_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motorista_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recebido_por = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    observacao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    localizacao = table.Column<Point>(type: "geography (point, 4326)", nullable: true),
                    registrado_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    autor_usuario_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comprovantes", x => x.id);
                    table.ForeignKey(
                        name: "fk_comprovantes_entregas_entrega_id",
                        column: x => x.entrega_id,
                        principalTable: "entregas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_comprovantes_motoristas_motorista_id",
                        column: x => x.motorista_id,
                        principalTable: "motoristas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_comprovantes_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_comprovantes_rotas_rota_id",
                        column: x => x.rota_id,
                        principalTable: "rotas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_comprovantes_usuarios_autor_usuario_id",
                        column: x => x.autor_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "arquivos_do_comprovante",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comprovante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    chave = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tipo_de_conteudo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    tamanho_em_bytes = table.Column<long>(type: "bigint", nullable: false),
                    hash_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    enviado_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arquivos_do_comprovante", x => x.id);
                    table.CheckConstraint("ck_arquivos_do_comprovante_tamanho_maximo", "tamanho_em_bytes <= 5242880");
                    table.CheckConstraint("ck_arquivos_do_comprovante_tamanho_positivo", "tamanho_em_bytes > 0");
                    table.ForeignKey(
                        name: "fk_arquivos_do_comprovante_comprovantes_comprovante_id",
                        column: x => x.comprovante_id,
                        principalTable: "comprovantes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_arquivos_do_comprovante_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_arquivos_do_comprovante_comprovante_id",
                table: "arquivos_do_comprovante",
                column: "comprovante_id");

            migrationBuilder.CreateIndex(
                name: "ix_arquivos_do_comprovante_organizacao_id",
                table: "arquivos_do_comprovante",
                column: "organizacao_id");

            migrationBuilder.CreateIndex(
                name: "ux_arquivos_do_comprovante_chave",
                table: "arquivos_do_comprovante",
                column: "chave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_comprovantes_autor_usuario_id",
                table: "comprovantes",
                column: "autor_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_comprovantes_motorista_id",
                table: "comprovantes",
                column: "motorista_id");

            migrationBuilder.CreateIndex(
                name: "ix_comprovantes_organizacao_id_registrado_em",
                table: "comprovantes",
                columns: new[] { "organizacao_id", "registrado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_comprovantes_rota_id",
                table: "comprovantes",
                column: "rota_id");

            migrationBuilder.CreateIndex(
                name: "ux_comprovantes_entrega_id",
                table: "comprovantes",
                column: "entrega_id",
                unique: true);

            // A prova de entrega não se reescreve: alterar metadado de comprovante seria alterar a prova.
            // Somente-inserção, como a timeline da entrega e as ocorrências.
            migrationBuilder.Sql("""
                CREATE FUNCTION impedir_alteracao_de_comprovante() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'O registro de comprovantes é somente-inserção.'
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trg_comprovantes_sem_update_ou_delete
                    BEFORE UPDATE OR DELETE ON comprovantes
                    FOR EACH ROW EXECUTE FUNCTION impedir_alteracao_de_comprovante();

                CREATE TRIGGER trg_comprovantes_sem_truncate
                    BEFORE TRUNCATE ON comprovantes
                    FOR EACH STATEMENT EXECUTE FUNCTION impedir_alteracao_de_comprovante();

                CREATE TRIGGER trg_arquivos_do_comprovante_sem_update_ou_delete
                    BEFORE UPDATE OR DELETE ON arquivos_do_comprovante
                    FOR EACH ROW EXECUTE FUNCTION impedir_alteracao_de_comprovante();

                CREATE TRIGGER trg_arquivos_do_comprovante_sem_truncate
                    BEFORE TRUNCATE ON arquivos_do_comprovante
                    FOR EACH STATEMENT EXECUTE FUNCTION impedir_alteracao_de_comprovante();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_arquivos_do_comprovante_sem_truncate ON arquivos_do_comprovante;
                DROP TRIGGER IF EXISTS trg_arquivos_do_comprovante_sem_update_ou_delete ON arquivos_do_comprovante;
                DROP TRIGGER IF EXISTS trg_comprovantes_sem_truncate ON comprovantes;
                DROP TRIGGER IF EXISTS trg_comprovantes_sem_update_ou_delete ON comprovantes;
                DROP FUNCTION IF EXISTS impedir_alteracao_de_comprovante();
                """);

            migrationBuilder.DropTable(
                name: "arquivos_do_comprovante");

            migrationBuilder.DropTable(
                name: "comprovantes");
        }
    }
}
