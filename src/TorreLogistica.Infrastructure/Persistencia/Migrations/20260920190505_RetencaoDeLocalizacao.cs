using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreLogistica.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class RetencaoDeLocalizacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_posicoes_recebida_em",
                table: "posicoes",
                column: "recebida_em");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_posicoes_recebida_em",
                table: "posicoes");
        }
    }
}
