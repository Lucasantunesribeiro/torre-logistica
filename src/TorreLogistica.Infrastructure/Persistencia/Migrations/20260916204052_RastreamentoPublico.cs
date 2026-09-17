using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreLogistica.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class RastreamentoPublico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tokens_de_rastreamento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entrega_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hash_do_token = table.Column<byte[]>(type: "bytea", nullable: false),
                    emitido_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    revogado_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    autor_usuario_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tokens_de_rastreamento", x => x.id);
                    table.CheckConstraint("ck_tokens_de_rastreamento_hash_tem_32_bytes", "octet_length(hash_do_token) = 32");
                    table.CheckConstraint("ck_tokens_de_rastreamento_validade", "expira_em > emitido_em");
                    table.ForeignKey(
                        name: "fk_tokens_de_rastreamento_entregas_entrega_id",
                        column: x => x.entrega_id,
                        principalTable: "entregas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tokens_de_rastreamento_organizacao_id",
                table: "tokens_de_rastreamento",
                column: "organizacao_id");

            migrationBuilder.CreateIndex(
                name: "ux_tokens_de_rastreamento_entrega_ativo",
                table: "tokens_de_rastreamento",
                column: "entrega_id",
                unique: true,
                filter: "revogado_em IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_tokens_de_rastreamento_hash",
                table: "tokens_de_rastreamento",
                column: "hash_do_token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tokens_de_rastreamento");
        }
    }
}
