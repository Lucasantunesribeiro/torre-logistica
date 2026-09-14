using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreLogistica.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Identidade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "organizacoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    slug = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organizacoes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "eventos_de_auditoria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ocorrido_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    tipo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    autor_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    alvo_tipo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    alvo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dados = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos_de_auditoria", x => x.id);
                    table.ForeignKey(
                        name: "fk_eventos_de_auditoria_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    email_normalizado = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    hash_da_senha = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    perfil = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    tentativas_de_login_falhas = table.Column<int>(type: "integer", nullable: false),
                    bloqueado_ate = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios", x => x.id);
                    table.CheckConstraint("ck_usuarios_tentativas_de_login_falhas_nao_negativas", "tentativas_de_login_falhas >= 0");
                    table.ForeignKey(
                        name: "fk_usuarios_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sessoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canal = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    ultima_renovacao_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    revogada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    motivo_da_revogacao = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessoes", x => x.id);
                    table.ForeignKey(
                        name: "fk_sessoes_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sessoes_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tokens_de_renovacao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sessao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hash_do_token = table.Column<byte[]>(type: "bytea", nullable: false),
                    emitido_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    usado_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    invalidado_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    substituto_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tokens_de_renovacao", x => x.id);
                    table.CheckConstraint("ck_tokens_de_renovacao_hash_tem_32_bytes", "octet_length(hash_do_token) = 32");
                    table.ForeignKey(
                        name: "fk_tokens_de_renovacao_sessoes_sessao_id",
                        column: x => x.sessao_id,
                        principalTable: "sessoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_eventos_de_auditoria_organizacao_id_ocorrido_em",
                table: "eventos_de_auditoria",
                columns: new[] { "organizacao_id", "ocorrido_em" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_organizacoes_slug",
                table: "organizacoes",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessoes_organizacao_id",
                table: "sessoes",
                column: "organizacao_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessoes_usuario_id_abertas",
                table: "sessoes",
                column: "usuario_id",
                filter: "revogada_em IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tokens_de_renovacao_sessao_id",
                table: "tokens_de_renovacao",
                column: "sessao_id");

            migrationBuilder.CreateIndex(
                name: "ux_tokens_de_renovacao_hash_do_token",
                table: "tokens_de_renovacao",
                column: "hash_do_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_usuarios_organizacao_id_email_normalizado",
                table: "usuarios",
                columns: new[] { "organizacao_id", "email_normalizado" },
                unique: true);

            // Trilha de auditoria somente-inserção, garantida pelo banco e não só pela
            // aplicação: UPDATE, DELETE e TRUNCATE falham, venham de onde vierem.
            migrationBuilder.Sql("""
                CREATE FUNCTION impedir_alteracao_de_auditoria() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'eventos_de_auditoria é somente-inserção: % recusado', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trg_eventos_de_auditoria_sem_update_ou_delete
                    BEFORE UPDATE OR DELETE ON eventos_de_auditoria
                    FOR EACH ROW EXECUTE FUNCTION impedir_alteracao_de_auditoria();

                CREATE TRIGGER trg_eventos_de_auditoria_sem_truncate
                    BEFORE TRUNCATE ON eventos_de_auditoria
                    FOR EACH STATEMENT EXECUTE FUNCTION impedir_alteracao_de_auditoria();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_eventos_de_auditoria_sem_truncate ON eventos_de_auditoria;
                DROP TRIGGER IF EXISTS trg_eventos_de_auditoria_sem_update_ou_delete ON eventos_de_auditoria;
                DROP FUNCTION IF EXISTS impedir_alteracao_de_auditoria();
                """);

            migrationBuilder.DropTable(
                name: "eventos_de_auditoria");

            migrationBuilder.DropTable(
                name: "tokens_de_renovacao");

            migrationBuilder.DropTable(
                name: "sessoes");

            migrationBuilder.DropTable(
                name: "usuarios");

            migrationBuilder.DropTable(
                name: "organizacoes");
        }
    }
}
