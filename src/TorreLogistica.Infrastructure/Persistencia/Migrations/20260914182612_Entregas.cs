using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace TorreLogistica.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Entregas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entregas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destinatario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    localizacao = table.Column<Point>(type: "geography (point, 4326)", nullable: true),
                    observacoes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    motivo_do_cancelamento = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    descricao_do_cancelamento = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: true),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    atualizada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    cancelada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ultima_sequencia_de_evento = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    endereco_bairro = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    endereco_cep = table.Column<string>(type: "character(8)", fixedLength: true, maxLength: 8, nullable: false),
                    endereco_cidade = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    endereco_complemento = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    endereco_logradouro = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    endereco_numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    endereco_uf = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    prometida_ate = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    prometida_de = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entregas", x => x.id);
                    table.CheckConstraint("ck_entregas_janela_prometida_ordenada", "prometida_ate > prometida_de");
                    table.CheckConstraint("ck_entregas_ultima_sequencia_de_evento_positiva", "ultima_sequencia_de_evento > 0");
                    table.ForeignKey(
                        name: "fk_entregas_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_entregas_destinatarios_destinatario_id",
                        column: x => x.destinatario_id,
                        principalTable: "destinatarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_entregas_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sequencias_de_codigo",
                columns: table => new
                {
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serie = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ano = table.Column<int>(type: "integer", nullable: false),
                    ultimo_numero = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sequencias_de_codigo", x => new { x.organizacao_id, x.serie, x.ano });
                    table.CheckConstraint("ck_sequencias_de_codigo_ultimo_numero_positivo", "ultimo_numero > 0");
                    table.ForeignKey(
                        name: "fk_sequencias_de_codigo_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "eventos_da_entrega",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entrega_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequencia = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status_resultante = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    autor_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dados = table.Column<string>(type: "jsonb", nullable: false),
                    ocorrido_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos_da_entrega", x => x.id);
                    table.CheckConstraint("ck_eventos_da_entrega_sequencia_positiva", "sequencia > 0");
                    table.ForeignKey(
                        name: "fk_eventos_da_entrega_entregas_entrega_id",
                        column: x => x.entrega_id,
                        principalTable: "entregas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_eventos_da_entrega_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_eventos_da_entrega_usuarios_autor_usuario_id",
                        column: x => x.autor_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entregas_cliente_id",
                table: "entregas",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_entregas_destinatario_id",
                table: "entregas",
                column: "destinatario_id");

            migrationBuilder.CreateIndex(
                name: "ix_entregas_localizacao",
                table: "entregas",
                column: "localizacao")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ux_entregas_organizacao_id_codigo",
                table: "entregas",
                columns: new[] { "organizacao_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_eventos_da_entrega_autor_usuario_id",
                table: "eventos_da_entrega",
                column: "autor_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_da_entrega_organizacao_id",
                table: "eventos_da_entrega",
                column: "organizacao_id");

            migrationBuilder.CreateIndex(
                name: "ux_eventos_da_entrega_entrega_id_sequencia",
                table: "eventos_da_entrega",
                columns: new[] { "entrega_id", "sequencia" },
                unique: true);

            // A consulta da lista operacional: por organização, status e fim da janela. Fica em
            // SQL porque coluna de tipo complexo do EF Core não entra em HasIndex.
            migrationBuilder.Sql("""
                CREATE INDEX ix_entregas_organizacao_id_status_prometida_ate
                    ON entregas (organizacao_id, status, prometida_ate);
                """);

            // Timeline somente-inserção, garantida pelo banco — mesmo desenho da trilha de
            // auditoria da Fase 1: UPDATE, DELETE e TRUNCATE falham, venham de onde vierem.
            migrationBuilder.Sql("""
                CREATE FUNCTION impedir_alteracao_da_timeline_da_entrega() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'A timeline da entrega é somente-inserção.'
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trg_eventos_da_entrega_sem_update_ou_delete
                    BEFORE UPDATE OR DELETE ON eventos_da_entrega
                    FOR EACH ROW EXECUTE FUNCTION impedir_alteracao_da_timeline_da_entrega();

                CREATE TRIGGER trg_eventos_da_entrega_sem_truncate
                    BEFORE TRUNCATE ON eventos_da_entrega
                    FOR EACH STATEMENT EXECUTE FUNCTION impedir_alteracao_da_timeline_da_entrega();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_eventos_da_entrega_sem_truncate ON eventos_da_entrega;
                DROP TRIGGER IF EXISTS trg_eventos_da_entrega_sem_update_ou_delete ON eventos_da_entrega;
                DROP FUNCTION IF EXISTS impedir_alteracao_da_timeline_da_entrega();
                DROP INDEX IF EXISTS ix_entregas_organizacao_id_status_prometida_ate;
                """);

            migrationBuilder.DropTable(
                name: "eventos_da_entrega");

            migrationBuilder.DropTable(
                name: "sequencias_de_codigo");

            migrationBuilder.DropTable(
                name: "entregas");
        }
    }
}
