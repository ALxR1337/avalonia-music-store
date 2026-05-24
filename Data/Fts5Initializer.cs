using System;
using Microsoft.EntityFrameworkCore;

namespace MusicApp.Data;

public static class Fts5Initializer
{
    public static void Ensure(MusicStoreDbContext db)
    {
        var sql = $@"
CREATE VIRTUAL TABLE IF NOT EXISTS SearchIndex USING fts5(
    content_type UNINDEXED,
    content_id UNINDEXED,
    title,
    body,
    tokenize = 'unicode61 remove_diacritics 2'
);

CREATE TRIGGER IF NOT EXISTS trg_artist_ai AFTER INSERT ON Artists BEGIN
    INSERT INTO SearchIndex(content_type, content_id, title, body)
    VALUES('artist', new.Id, new.Name, COALESCE(new.Aliases, '') || ' ' || COALESCE(new.Description, ''));
END;
CREATE TRIGGER IF NOT EXISTS trg_artist_au AFTER UPDATE ON Artists BEGIN
    DELETE FROM SearchIndex WHERE content_type='artist' AND content_id = old.Id;
    INSERT INTO SearchIndex(content_type, content_id, title, body)
    VALUES('artist', new.Id, new.Name, COALESCE(new.Aliases, '') || ' ' || COALESCE(new.Description, ''));
END;
CREATE TRIGGER IF NOT EXISTS trg_artist_ad AFTER DELETE ON Artists BEGIN
    DELETE FROM SearchIndex WHERE content_type='artist' AND content_id = old.Id;
END;

CREATE TRIGGER IF NOT EXISTS trg_album_ai AFTER INSERT ON Albums BEGIN
    INSERT INTO SearchIndex(content_type, content_id, title, body)
    VALUES('album', new.Id, new.Title, COALESCE(new.Description, ''));
END;
CREATE TRIGGER IF NOT EXISTS trg_album_au AFTER UPDATE ON Albums BEGIN
    DELETE FROM SearchIndex WHERE content_type='album' AND content_id = old.Id;
    INSERT INTO SearchIndex(content_type, content_id, title, body)
    VALUES('album', new.Id, new.Title, COALESCE(new.Description, ''));
END;
CREATE TRIGGER IF NOT EXISTS trg_album_ad AFTER DELETE ON Albums BEGIN
    DELETE FROM SearchIndex WHERE content_type='album' AND content_id = old.Id;
END;

CREATE TRIGGER IF NOT EXISTS trg_track_ai AFTER INSERT ON Tracks BEGIN
    INSERT INTO SearchIndex(content_type, content_id, title, body)
    VALUES('track', new.Id, new.Title, COALESCE(new.Lyrics, ''));
END;
CREATE TRIGGER IF NOT EXISTS trg_track_au AFTER UPDATE ON Tracks BEGIN
    DELETE FROM SearchIndex WHERE content_type='track' AND content_id = old.Id;
    INSERT INTO SearchIndex(content_type, content_id, title, body)
    VALUES('track', new.Id, new.Title, COALESCE(new.Lyrics, ''));
END;
CREATE TRIGGER IF NOT EXISTS trg_track_ad AFTER DELETE ON Tracks BEGIN
    DELETE FROM SearchIndex WHERE content_type='track' AND content_id = old.Id;
END;

CREATE TRIGGER IF NOT EXISTS trg_review_ai AFTER INSERT ON Reviews BEGIN
    INSERT INTO SearchIndex(content_type, content_id, title, body)
    VALUES('review', new.Id, '', new.Text);
END;
CREATE TRIGGER IF NOT EXISTS trg_review_au AFTER UPDATE ON Reviews BEGIN
    DELETE FROM SearchIndex WHERE content_type='review' AND content_id = old.Id;
    INSERT INTO SearchIndex(content_type, content_id, title, body)
    VALUES('review', new.Id, '', new.Text);
END;
CREATE TRIGGER IF NOT EXISTS trg_review_ad AFTER DELETE ON Reviews BEGIN
    DELETE FROM SearchIndex WHERE content_type='review' AND content_id = old.Id;
END;

-- Product aggregate triggers: Rating + ReviewCount + SalesCount are kept in sync
-- with their source tables (Reviews, OrderItems) by SQLite instead of services.

CREATE TRIGGER IF NOT EXISTS trg_review_aggregate_ai AFTER INSERT ON Reviews BEGIN
    UPDATE Products
    SET ReviewCount = (SELECT COUNT(*) FROM Reviews WHERE ProductId = NEW.ProductId),
        Rating      = COALESCE((SELECT AVG(Rating) FROM Reviews WHERE ProductId = NEW.ProductId), 0)
    WHERE Id = NEW.ProductId;
END;

CREATE TRIGGER IF NOT EXISTS trg_review_aggregate_au AFTER UPDATE ON Reviews BEGIN
    UPDATE Products
    SET ReviewCount = (SELECT COUNT(*) FROM Reviews WHERE ProductId = NEW.ProductId),
        Rating      = COALESCE((SELECT AVG(Rating) FROM Reviews WHERE ProductId = NEW.ProductId), 0)
    WHERE Id = NEW.ProductId;
    UPDATE Products
    SET ReviewCount = (SELECT COUNT(*) FROM Reviews WHERE ProductId = OLD.ProductId),
        Rating      = COALESCE((SELECT AVG(Rating) FROM Reviews WHERE ProductId = OLD.ProductId), 0)
    WHERE Id = OLD.ProductId AND OLD.ProductId <> NEW.ProductId;
END;

CREATE TRIGGER IF NOT EXISTS trg_review_aggregate_ad AFTER DELETE ON Reviews BEGIN
    UPDATE Products
    SET ReviewCount = (SELECT COUNT(*) FROM Reviews WHERE ProductId = OLD.ProductId),
        Rating      = COALESCE((SELECT AVG(Rating) FROM Reviews WHERE ProductId = OLD.ProductId), 0)
    WHERE Id = OLD.ProductId;
END;

CREATE TRIGGER IF NOT EXISTS trg_orderitem_aggregate_ai AFTER INSERT ON OrderItems BEGIN
    UPDATE Products SET SalesCount = SalesCount + NEW.Quantity WHERE Id = NEW.ProductId;
END;

CREATE TRIGGER IF NOT EXISTS trg_orderitem_aggregate_au AFTER UPDATE ON OrderItems BEGIN
    UPDATE Products SET SalesCount = SalesCount - OLD.Quantity WHERE Id = OLD.ProductId;
    UPDATE Products SET SalesCount = SalesCount + NEW.Quantity WHERE Id = NEW.ProductId;
END;

CREATE TRIGGER IF NOT EXISTS trg_orderitem_aggregate_ad AFTER DELETE ON OrderItems BEGIN
    UPDATE Products SET SalesCount = SalesCount - OLD.Quantity WHERE Id = OLD.ProductId;
END;
";
        db.Database.ExecuteSqlRaw(sql);

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();
        using (var probe = conn.CreateCommand())
        {
            probe.CommandText = "SELECT COUNT(*) FROM SearchIndex";
            if (Convert.ToInt32(probe.ExecuteScalar()) > 0) return;
        }

        db.Database.ExecuteSqlRaw(@"
INSERT INTO SearchIndex(content_type, content_id, title, body)
SELECT 'artist', Id, Name, COALESCE(Aliases, '') || ' ' || COALESCE(Description, '') FROM Artists;

INSERT INTO SearchIndex(content_type, content_id, title, body)
SELECT 'album', Id, Title, COALESCE(Description, '') FROM Albums;

INSERT INTO SearchIndex(content_type, content_id, title, body)
SELECT 'track', Id, Title, COALESCE(Lyrics, '') FROM Tracks;

INSERT INTO SearchIndex(content_type, content_id, title, body)
SELECT 'review', Id, '', Text FROM Reviews;
");
    }
}
