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

-- SalesCount counts only COMPLETED orders (spec §2/§8.7: a sale is a fulfilled order,
-- not a freshly-placed cart checkout). Orders are created with Status='New' and only an
-- admin moves them to 'Completed', so the aggregate is driven by the order's status
-- transition — NOT by OrderItems insertion (which happens at checkout, before fulfilment).
-- The legacy per-OrderItem triggers are dropped so existing databases pick up the new rule.
DROP TRIGGER IF EXISTS trg_orderitem_aggregate_ai;
DROP TRIGGER IF EXISTS trg_orderitem_aggregate_au;
DROP TRIGGER IF EXISTS trg_orderitem_aggregate_ad;

CREATE TRIGGER IF NOT EXISTS trg_order_completed AFTER UPDATE OF Status ON Orders
WHEN NEW.Status = 'Completed' AND OLD.Status <> 'Completed' BEGIN
    UPDATE Products SET SalesCount = SalesCount + (
        SELECT COALESCE(SUM(oi.Quantity), 0) FROM OrderItems oi
        WHERE oi.OrderId = NEW.Id AND oi.ProductId = Products.Id)
    WHERE Id IN (SELECT ProductId FROM OrderItems WHERE OrderId = NEW.Id);
END;

CREATE TRIGGER IF NOT EXISTS trg_order_uncompleted AFTER UPDATE OF Status ON Orders
WHEN OLD.Status = 'Completed' AND NEW.Status <> 'Completed' BEGIN
    UPDATE Products SET SalesCount = SalesCount - (
        SELECT COALESCE(SUM(oi.Quantity), 0) FROM OrderItems oi
        WHERE oi.OrderId = OLD.Id AND oi.ProductId = Products.Id)
    WHERE Id IN (SELECT ProductId FROM OrderItems WHERE OrderId = OLD.Id);
END;
";
        db.Database.ExecuteSqlRaw(sql);

        // Backfill SalesCount from the source of truth (completed orders). This is idempotent
        // and also repairs databases polluted by the old per-OrderItem trigger, which counted
        // every checkout — including New/Processing/Cancelled — as a sale.
        db.Database.ExecuteSqlRaw(@"
UPDATE Products SET SalesCount = (
    SELECT COALESCE(SUM(oi.Quantity), 0)
    FROM OrderItems oi
    JOIN Orders o ON o.Id = oi.OrderId
    WHERE oi.ProductId = Products.Id AND o.Status = 'Completed');
");

        // Same story for Rating/ReviewCount: the review triggers above only fire on
        // post-trigger writes, so reviews inserted by DbSeeder (which runs before this
        // initializer) never reach the Product aggregates — the product card showed
        // «★ 0.0 · 0 відгуків» right above a non-empty review list.
        db.Database.ExecuteSqlRaw(@"
UPDATE Products SET
    ReviewCount = (SELECT COUNT(*) FROM Reviews r WHERE r.ProductId = Products.Id),
    Rating      = COALESCE((SELECT AVG(r.Rating) FROM Reviews r WHERE r.ProductId = Products.Id), 0);
");

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
