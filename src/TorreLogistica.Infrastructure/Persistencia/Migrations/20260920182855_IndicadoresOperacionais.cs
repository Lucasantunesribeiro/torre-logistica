using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreLogistica.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class IndicadoresOperacionais : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_paradas_entrega_adicionada_em",
                table: "paradas",
                columns: new[] { "entrega_id", "adicionada_em" });

            migrationBuilder.CreateIndex(
                name: "ix_entregas_organizacao_cancelada_em",
                table: "entregas",
                columns: new[] { "organizacao_id", "cancelada_em" },
                filter: "cancelada_em IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_entregas_organizacao_entregue_em",
                table: "entregas",
                columns: new[] { "organizacao_id", "entregue_em" },
                filter: "entregue_em IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_paradas_entrega_adicionada_em",
                table: "paradas");

            migrationBuilder.DropIndex(
                name: "ix_entregas_organizacao_cancelada_em",
                table: "entregas");

            migrationBuilder.DropIndex(
                name: "ix_entregas_organizacao_entregue_em",
                table: "entregas");
        }
    }
}
