using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class SchemaHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Pre-flight: clean orphan rows that existed before referential integrity
            // was enforced. Without this, adding the new FKs fails with SQLite error 19.
            // PlayerSettings.LastTrackId can be 0 from old PlayFile() calls (synthetic
            // Track Id=0); the other tables can theoretically hold rows for deleted users.
            migrationBuilder.Sql(@"
                UPDATE PlayerSettings SET LastTrackId = NULL
                    WHERE LastTrackId IS NOT NULL
                      AND LastTrackId NOT IN (SELECT Id FROM Tracks);
                DELETE FROM PlayerSettings WHERE UserId NOT IN (SELECT Id FROM Users);
                DELETE FROM CartItems      WHERE UserId NOT IN (SELECT Id FROM Users);
                DELETE FROM Orders         WHERE UserId NOT IN (SELECT Id FROM Users);
                DELETE FROM Reviews        WHERE UserId NOT IN (SELECT Id FROM Users)
                                              OR ProductId NOT IN (SELECT Id FROM Products);
                DELETE FROM Wishlists      WHERE UserId NOT IN (SELECT Id FROM Users);
                DELETE FROM Playlists      WHERE UserId NOT IN (SELECT Id FROM Users);
                DELETE FROM SearchHistory  WHERE UserId NOT IN (SELECT Id FROM Users);
                DELETE FROM SavedSearches  WHERE UserId NOT IN (SELECT Id FROM Users);
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_Albums_Genres_GenreId",
                table: "Albums");

            migrationBuilder.DropTable(
                name: "PlaylistTrack");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Tracks_AlbumId",
                table: "Tracks");

            migrationBuilder.DropIndex(
                name: "IX_Albums_GenreId",
                table: "Albums");

            // --- Data-preserving column conversions: add new columns first, copy
            // values, then drop the old ones. Order is deliberate.

            migrationBuilder.AddColumn<long>(
                name: "PriceCents",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
            migrationBuilder.Sql(
                "UPDATE Products SET PriceCents = CAST(ROUND(Price * 100.0) AS INTEGER);");
            migrationBuilder.DropColumn(name: "Price", table: "Products");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Orders",
                type: "TEXT",
                maxLength: 8,
                nullable: false,
                defaultValue: "UAH");

            migrationBuilder.AddColumn<string>(
                name: "ShippingAddress",
                table: "Orders",
                type: "TEXT",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TotalAmountCents",
                table: "Orders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
            migrationBuilder.Sql(
                "UPDATE Orders SET TotalAmountCents = CAST(ROUND(TotalAmount * 100.0) AS INTEGER);");
            migrationBuilder.DropColumn(name: "TotalAmount", table: "Orders");

            migrationBuilder.AddColumn<string>(
                name: "UserEmail",
                table: "Orders",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AlbumTitle",
                table: "OrderItems",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ArtistName",
                table: "OrderItems",
                type: "TEXT",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FormatLabel",
                table: "OrderItems",
                type: "TEXT",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProductTitle",
                table: "OrderItems",
                type: "TEXT",
                maxLength: 240,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "UnitPriceCents",
                table: "OrderItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
            migrationBuilder.Sql(
                "UPDATE OrderItems SET UnitPriceCents = CAST(ROUND(UnitPrice * 100.0) AS INTEGER);");
            migrationBuilder.DropColumn(name: "UnitPrice", table: "OrderItems");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Genres",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 64);

            migrationBuilder.AddColumn<bool>(
                name: "IsPrimary",
                table: "AlbumGenres",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
            // Promote each album's existing single GenreId to a primary AlbumGenres row.
            // Only inserts where the (AlbumId, GenreId) pair isn't already present.
            migrationBuilder.Sql(@"
                INSERT INTO AlbumGenres(AlbumId, GenreId, IsPrimary)
                SELECT a.Id, a.GenreId, 1
                FROM Albums a
                WHERE NOT EXISTS (
                    SELECT 1 FROM AlbumGenres ag
                    WHERE ag.AlbumId = a.Id AND ag.GenreId = a.GenreId
                );
                UPDATE AlbumGenres SET IsPrimary = 1
                WHERE (AlbumId, GenreId) IN (SELECT Id, GenreId FROM Albums);
            ");
            migrationBuilder.DropColumn(name: "GenreId", table: "Albums");

            migrationBuilder.CreateTable(
                name: "PlaylistTracks",
                columns: table => new
                {
                    PlaylistId = table.Column<int>(type: "INTEGER", nullable: false),
                    TrackId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistTracks", x => new { x.PlaylistId, x.TrackId });
                    table.ForeignKey(
                        name: "FK_PlaylistTracks_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "Playlists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaylistTracks_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_AlbumId_Position",
                table: "Tracks",
                columns: new[] { "AlbumId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSettings_LastTrackId",
                table: "PlayerSettings",
                column: "LastTrackId");

            migrationBuilder.CreateIndex(
                name: "IX_AlbumGenres_AlbumId",
                table: "AlbumGenres",
                column: "AlbumId",
                unique: true,
                filter: "\"IsPrimary\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistTracks_PlaylistId_Position",
                table: "PlaylistTracks",
                columns: new[] { "PlaylistId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistTracks_TrackId",
                table: "PlaylistTracks",
                column: "TrackId");

            migrationBuilder.AddForeignKey(
                name: "FK_CartItems_Users_UserId",
                table: "CartItems",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Users_UserId",
                table: "Orders",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerSettings_Tracks_LastTrackId",
                table: "PlayerSettings",
                column: "LastTrackId",
                principalTable: "Tracks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerSettings_Users_UserId",
                table: "PlayerSettings",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Playlists_Users_UserId",
                table: "Playlists",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Reviews_Products_ProductId",
                table: "Reviews",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Reviews_Users_UserId",
                table: "Reviews",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SavedSearches_Users_UserId",
                table: "SavedSearches",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SearchHistory_Users_UserId",
                table: "SearchHistory",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Wishlists_Users_UserId",
                table: "Wishlists",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // One-time backfill of Product aggregates that the new triggers maintain
            // going forward. Without this, existing reviews/orders would never be
            // reflected because triggers only fire on subsequent INSERTs/UPDATEs.
            migrationBuilder.Sql(@"
                UPDATE Products
                SET ReviewCount = COALESCE((SELECT COUNT(*) FROM Reviews r WHERE r.ProductId = Products.Id), 0),
                    Rating      = COALESCE((SELECT AVG(Rating) FROM Reviews r WHERE r.ProductId = Products.Id), 0),
                    SalesCount  = COALESCE((SELECT SUM(Quantity) FROM OrderItems oi WHERE oi.ProductId = Products.Id), 0);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CartItems_Users_UserId",
                table: "CartItems");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Users_UserId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_PlayerSettings_Tracks_LastTrackId",
                table: "PlayerSettings");

            migrationBuilder.DropForeignKey(
                name: "FK_PlayerSettings_Users_UserId",
                table: "PlayerSettings");

            migrationBuilder.DropForeignKey(
                name: "FK_Playlists_Users_UserId",
                table: "Playlists");

            migrationBuilder.DropForeignKey(
                name: "FK_Reviews_Products_ProductId",
                table: "Reviews");

            migrationBuilder.DropForeignKey(
                name: "FK_Reviews_Users_UserId",
                table: "Reviews");

            migrationBuilder.DropForeignKey(
                name: "FK_SavedSearches_Users_UserId",
                table: "SavedSearches");

            migrationBuilder.DropForeignKey(
                name: "FK_SearchHistory_Users_UserId",
                table: "SearchHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_Wishlists_Users_UserId",
                table: "Wishlists");

            migrationBuilder.DropTable(
                name: "PlaylistTracks");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Tracks_AlbumId_Position",
                table: "Tracks");

            migrationBuilder.DropIndex(
                name: "IX_PlayerSettings_LastTrackId",
                table: "PlayerSettings");

            migrationBuilder.DropIndex(
                name: "IX_AlbumGenres_AlbumId",
                table: "AlbumGenres");

            migrationBuilder.DropColumn(
                name: "PriceCents",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingAddress",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TotalAmountCents",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "UserEmail",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AlbumTitle",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "ArtistName",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "FormatLabel",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "ProductTitle",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "UnitPriceCents",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "IsPrimary",
                table: "AlbumGenres");

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "Products",
                type: "DECIMAL(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalAmount",
                table: "Orders",
                type: "DECIMAL(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "OrderItems",
                type: "DECIMAL(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Genres",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 64,
                oldCollation: "NOCASE");

            migrationBuilder.AddColumn<int>(
                name: "GenreId",
                table: "Albums",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PlaylistTrack",
                columns: table => new
                {
                    PlaylistId = table.Column<int>(type: "INTEGER", nullable: false),
                    TracksId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistTrack", x => new { x.PlaylistId, x.TracksId });
                    table.ForeignKey(
                        name: "FK_PlaylistTrack_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "Playlists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaylistTrack_Tracks_TracksId",
                        column: x => x.TracksId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_AlbumId",
                table: "Tracks",
                column: "AlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_Albums_GenreId",
                table: "Albums",
                column: "GenreId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistTrack_TracksId",
                table: "PlaylistTrack",
                column: "TracksId");

            migrationBuilder.AddForeignKey(
                name: "FK_Albums_Genres_GenreId",
                table: "Albums",
                column: "GenreId",
                principalTable: "Genres",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
