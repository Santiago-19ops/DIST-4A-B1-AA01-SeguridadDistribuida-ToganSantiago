using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notificaciones.Api.Migrations
{
    /// <inheritdoc />
    public partial class InicialNotificaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EnviosCorreo",
                columns: table => new
                {
                    IdEnvio = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CorreoDestino = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Asunto = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FechaEnvio = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnviosCorreo", x => x.IdEnvio);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnviosCorreo");
        }
    }
}
