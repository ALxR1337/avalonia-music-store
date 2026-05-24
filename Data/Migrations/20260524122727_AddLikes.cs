using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLikes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlbumLikes",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    AlbumId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlbumLikes", x => new { x.UserId, x.AlbumId });
                    table.ForeignKey(
                        name: "FK_AlbumLikes_Albums_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Albums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AlbumLikes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrackLikes",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    TrackId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackLikes", x => new { x.UserId, x.TrackId });
                    table.ForeignKey(
                        name: "FK_TrackLikes_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrackLikes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlbumLikes_AlbumId",
                table: "AlbumLikes",
                column: "AlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_AlbumLikes_UserId",
                table: "AlbumLikes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackLikes_TrackId",
                table: "TrackLikes",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackLikes_UserId",
                table: "TrackLikes",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlbumLikes");

            migrationBuilder.DropTable(
                name: "TrackLikes");
        }
    }
}
