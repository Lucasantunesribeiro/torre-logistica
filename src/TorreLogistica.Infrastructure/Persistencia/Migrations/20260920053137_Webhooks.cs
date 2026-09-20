using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreLogistica.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Webhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assinaturas_de_webhook",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    segredo_cifrado = table.Column<byte[]>(type: "bytea", nullable: false),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    autor_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revogada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    eventos = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assinaturas_de_webhook", x => x.id);
                    table.ForeignKey(
                        name: "fk_assinaturas_de_webhook_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    entrega_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conteudo = table.Column<string>(type: "jsonb", nullable: false),
                    ocorrido_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    despachada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    tentativas_de_despacho = table.Column<int>(type: "integer", nullable: false),
                    disponivel_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    ultimo_erro = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox", x => x.id);
                    table.ForeignKey(
                        name: "fk_outbox_entregas_entrega_id",
                        column: x => x.entrega_id,
                        principalTable: "entregas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "entregas_de_webhook",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assinatura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mensagem_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    conteudo = table.Column<string>(type: "jsonb", nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    disponivel_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    concluida_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ultimo_status = table.Column<int>(type: "integer", nullable: true),
                    ultimo_erro = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entregas_de_webhook", x => x.id);
                    table.ForeignKey(
                        name: "fk_entregas_de_webhook_assinaturas_de_webhook_assinatura_id",
                        column: x => x.assinatura_id,
                        principalTable: "assinaturas_de_webhook",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_entregas_de_webhook_outbox_mensagem_id",
                        column: x => x.mensagem_id,
                        principalTable: "outbox",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tentativas_de_webhook",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entrega_de_webhook_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: true),
                    duracao_em_milissegundos = table.Column<int>(type: "integer", nullable: false),
                    erro = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tentada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tentativas_de_webhook", x => x.id);
                    table.ForeignKey(
                        name: "fk_tentativas_de_webhook_entregas_de_webhook_entrega_de_webhoo",
                        column: x => x.entrega_de_webhook_id,
                        principalTable: "entregas_de_webhook",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assinaturas_de_webhook_organizacao_id",
                table: "assinaturas_de_webhook",
                column: "organizacao_id");

            migrationBuilder.CreateIndex(
                name: "ix_entregas_de_webhook_mensagem_id",
                table: "entregas_de_webhook",
                column: "mensagem_id");

            migrationBuilder.CreateIndex(
                name: "ix_entregas_de_webhook_organizacao_id_estado",
                table: "entregas_de_webhook",
                columns: new[] { "organizacao_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_entregas_de_webhook_pendentes",
                table: "entregas_de_webhook",
                column: "disponivel_em",
                filter: "estado = 'Pendente'");

            migrationBuilder.CreateIndex(
                name: "ux_entregas_de_webhook_assinatura_mensagem",
                table: "entregas_de_webhook",
                columns: new[] { "assinatura_id", "mensagem_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_entrega_id",
                table: "outbox",
                column: "entrega_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pendentes",
                table: "outbox",
                column: "disponivel_em",
                filter: "despachada_em IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tentativas_de_webhook_entrega_numero",
                table: "tentativas_de_webhook",
                columns: new[] { "entrega_de_webhook_id", "numero" });

            // O histórico de tentativas é a resposta a "vocês tentaram me avisar?". Histórico que pode ser
            // reescrito não responde nada: somente-inserção, como a timeline e o comprovante.
            migrationBuilder.Sql("""
                CREATE FUNCTION impedir_alteracao_de_tentativa_de_webhook() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'O histórico de tentativas de webhook é somente-inserção.'
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trg_tentativas_de_webhook_sem_update_ou_delete
                    BEFORE UPDATE OR DELETE ON tentativas_de_webhook
                    FOR EACH ROW EXECUTE FUNCTION impedir_alteracao_de_tentativa_de_webhook();

                CREATE TRIGGER trg_tentativas_de_webhook_sem_truncate
                    BEFORE TRUNCATE ON tentativas_de_webhook
                    FOR EACH STATEMENT EXECUTE FUNCTION impedir_alteracao_de_tentativa_de_webhook();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_tentativas_de_webhook_sem_truncate ON tentativas_de_webhook;
                DROP TRIGGER IF EXISTS trg_tentativas_de_webhook_sem_update_ou_delete ON tentativas_de_webhook;
                DROP FUNCTION IF EXISTS impedir_alteracao_de_tentativa_de_webhook();
                """);

            migrationBuilder.DropTable(
                name: "tentativas_de_webhook");

            migrationBuilder.DropTable(
                name: "entregas_de_webhook");

            migrationBuilder.DropTable(
                name: "assinaturas_de_webhook");

            migrationBuilder.DropTable(
                name: "outbox");
        }
    }
}
