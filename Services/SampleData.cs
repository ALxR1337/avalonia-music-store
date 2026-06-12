using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MusicApp.Models;

namespace MusicApp.Services;

internal static class SampleData
{
    // Catalog is constrained to the canonical 8 genres so the UI stays scannable.
    // Albums can sit in multiple of these via the AlbumGenre join.
    private static readonly string[] BasicGenres =
    {
        "Rock", "Jazz", "Hip-Hop", "Electronic", "Classical", "Folk", "Experimental", "Indie"
    };

    // Real albums scanned from ~/Downloads/Music. Each entry is a curated catalog row:
    // descriptions are short bios, genre tags drive the many-to-many AlbumGenre rows.
    private static readonly AlbumSpec[] Specs = new[]
    {
        new AlbumSpec(
            ArtistName: "Kanye West",
            ArtistCountry: "США",
            ArtistBio: "Чиказький продюсер і репер, який десятиліттями переписував мову мейнстріму — від бенкетного соулу до індустріального ризику.",
            Title: "Yeezus",
            Year: 2013,
            DirName: "2013 - Yeezus (Web)",
            Description: "Гострий, мінімалістичний поворот: дисторшн, індастріал-кров і провокація замість оркестрових арок.",
            PrimaryGenre: "Hip-Hop",
            ExtraGenres: new[] { "Experimental" }),

        new AlbumSpec(
            ArtistName: "Neutral Milk Hotel",
            ArtistCountry: "США",
            ArtistBio: "Inде-фолк-проєкт Джеффа Маннґума — обмежена дискографія, що стала культовим стандартом «надмірно щирого» американського інді.",
            Title: "In The Aeroplane Over The Sea",
            Year: 1998,
            DirName: "[1998] In The Aeroplane Over The Sea",
            Description: "Громіздкі духові, фуззові гітари і голос Маннґума, що тримає альбом на межі евфорії та катарсису.",
            PrimaryGenre: "Indie",
            ExtraGenres: new[] { "Folk" }),

        new AlbumSpec(
            ArtistName: "Ludwig van Beethoven",
            ArtistCountry: "Німеччина",
            ArtistBio: "Композитор віденської класики, який міст між класицизмом і романтизмом — дев'ять симфоній є канонічним каркасом усієї оркестрової музики.",
            Title: "9 Симфоній (диригент Леонард Бернстайн)",
            Year: 1979,
            DirName: "All 9 Beethoven Symphonies - Cond. by Leonard Bernstein",
            Description: "Запис Нью-Йоркського філармонічного оркестру під керівництвом Леонарда Бернстайна — еталонний цикл XX століття.",
            PrimaryGenre: "Classical",
            ExtraGenres: Array.Empty<string>()),

        new AlbumSpec(
            ArtistName: "Bob Dylan",
            ArtistCountry: "США",
            ArtistBio: "Лауреат Нобелівської премії з літератури, що переписав канон американського пісенництва наприкінці 60-х.",
            Title: "The Freewheelin' Bob Dylan",
            Year: 1963,
            DirName: "Bob Dylan - The Freewheelin Bob Dylan",
            Description: "Другий студійний альбом Ділана — фолк, протестна пісня і перші великі балади з акустичною гітарою та губною гармонікою.",
            PrimaryGenre: "Folk",
            ExtraGenres: Array.Empty<string>()),

        new AlbumSpec(
            ArtistName: "Charles Mingus",
            ArtistCountry: "США",
            ArtistBio: "Контрабасист і композитор, бунтівний голос пост-бопу: великі ансамблі, гордий ґруву і політичний нерв.",
            Title: "Mingus Ah Um",
            Year: 1959,
            DirName: "Charles Mingus - Mingus Ah Um (1959, 2019, MFSL)",
            Description: "Підсумок Мінґуса 50-х: оммажі легендам джазу, госпел і блюз, що перетікають один в одного.",
            PrimaryGenre: "Jazz",
            ExtraGenres: Array.Empty<string>()),

        new AlbumSpec(
            ArtistName: "Daft Punk",
            ArtistCountry: "Франція",
            ArtistBio: "Французький дует у шоломах, що зробив філтер-хаус мейнстрімом і визначив звук танцпола 2000-х.",
            Title: "Discovery",
            Year: 2001,
            DirName: "Daft Punk - 2001 - Discovery [Virgin, 8496061 V2940, vinyl rip by Guardian]",
            Description: "Глянцеві синти, диско-семпли і вокодери — альбом-маніфест поп-електроніки нової ери.",
            PrimaryGenre: "Electronic",
            ExtraGenres: Array.Empty<string>()),

        new AlbumSpec(
            ArtistName: "Death Grips",
            ArtistCountry: "США",
            ArtistBio: "Сакраментський тріо, що загорнуло хіп-хоп у панк, нойз і пранкову естетику.",
            Title: "Exmilitary",
            Year: 2011,
            DirName: "Death Grips - Exmilitary",
            Description: "Дебютний мікстейп, виданий безкоштовно: гранжеві біти, агресивний MC Ride і колажі з рок-семплів.",
            PrimaryGenre: "Experimental",
            ExtraGenres: new[] { "Hip-Hop" }),

        new AlbumSpec(
            ArtistName: "Earl Sweatshirt",
            ArtistCountry: "США",
            ArtistBio: "Лос-анджелеський репер, що пройшов шлях від підлітка-вундеркінда Odd Future до меланхолійного абстрактного хіп-хопу.",
            Title: "Some Rap Songs",
            Year: 2018,
            DirName: "Earl Sweatshirt - Some Rap Songs (2018)",
            Description: "Дрібно нарізані семпли, тіні джазу і думські куплети — мініатюрний альбом про горе і пам'ять.",
            PrimaryGenre: "Hip-Hop",
            ExtraGenres: new[] { "Experimental" }),

        new AlbumSpec(
            ArtistName: "Frank Ocean",
            ArtistCountry: "США",
            ArtistBio: "Невловимий поет нового R&B, що поєднує сповідальну лірику з прозорою продакшн-палітрою.",
            Title: "Blonde",
            Year: 2016,
            DirName: "Frank Ocean - Blonde [2016] [320]",
            Description: "Тихий, амбієнтний альбом про пам'ять і ідентичність — мінімум барабанів, максимум простору.",
            PrimaryGenre: "Indie",
            ExtraGenres: new[] { "Experimental" }),

        new AlbumSpec(
            ArtistName: "John Coltrane",
            ArtistCountry: "США",
            ArtistBio: "Тенор-саксофоніст, чиї пізні роботи перетворили джаз на акт духовного пошуку.",
            Title: "A Love Supreme",
            Year: 1964,
            DirName: "John Coltrane - A Love Supreme (1964, 2013, Impulse!-Japan)",
            Description: "Сюіта з чотирьох частин, записана класичним квартетом — молитва в формі модального джазу.",
            PrimaryGenre: "Jazz",
            ExtraGenres: Array.Empty<string>()),

        new AlbumSpec(
            ArtistName: "Joni Mitchell",
            ArtistCountry: "Канада",
            ArtistBio: "Канадська авторка-виконавиця, що розширила межі фолку гармонічною винахідливістю джазу.",
            Title: "Blue",
            Year: 1971,
            DirName: "Joni Mitchell - Blue (1971) [MP3 320] 88",
            Description: "Сповідальний фолк-альбом, у якому Джоні залишається сама на сцені з фортепіано, гітарою та цимбалами.",
            PrimaryGenre: "Folk",
            ExtraGenres: Array.Empty<string>()),

        new AlbumSpec(
            ArtistName: "JPEGMAFIA",
            ArtistCountry: "США",
            ArtistBio: "Балтиморський продюсер-репер, що збирає індустріальний коллаж із інтернет-культури і агресивних семплів.",
            Title: "Veteran",
            Year: 2018,
            DirName: "JPEGMAFIA - Veteran (2018)",
            Description: "Гранична продакшн-естетика і саркастичний голос Peggy у 19 коротких треках.",
            PrimaryGenre: "Hip-Hop",
            ExtraGenres: new[] { "Experimental" }),

        new AlbumSpec(
            ArtistName: "Kendrick Lamar",
            ArtistCountry: "США",
            ArtistBio: "Репер з Комптона, лауреат Пулітцерівської премії — голос нового політичного хіп-хопу.",
            Title: "To Pimp A Butterfly",
            Year: 2015,
            DirName: "Kendrick Lamar - To Pimp A Butterfly (2015) [FLAC 24bit]",
            Description: "Сплав G-funk, фрі-джазу і ніяковості — альбом-есе про чорну ідентичність у сучасній Америці.",
            PrimaryGenre: "Hip-Hop",
            ExtraGenres: new[] { "Jazz" }),

        new AlbumSpec(
            ArtistName: "Led Zeppelin",
            ArtistCountry: "Велика Британія",
            ArtistBio: "Британський квартет, що задав словник важкого року 70-х — між блюзом, фолком і прог-рок-епосом.",
            Title: "Led Zeppelin IV (Deluxe)",
            Year: 1971,
            DirName: "Led Zeppelin - Led Zeppelin IV (2014 Deluxe) [FLAC] 88",
            Description: "Без назви, але з рунами — альбом, що містить «Stairway to Heaven» і фактично сформував канон класичного року.",
            PrimaryGenre: "Rock",
            ExtraGenres: new[] { "Folk" }),

        new AlbumSpec(
            ArtistName: "Miles Davis",
            ArtistCountry: "США",
            ArtistBio: "Трубач, що десятиліттями перевертав джаз — від кулу і модала до фьюжна та хіп-хоп-епохи.",
            Title: "Kind of Blue",
            Year: 1959,
            DirName: "Miles Davis - Kind of Blue (1959, 2015, MFSL)",
            Description: "Найвпливовіший джазовий альбом усіх часів — мінімалістичні модальні форми та ансамбль мрії.",
            PrimaryGenre: "Jazz",
            ExtraGenres: Array.Empty<string>()),

        new AlbumSpec(
            ArtistName: "Nas",
            ArtistCountry: "США",
            ArtistBio: "Поет з Куінсбріджа, чий дебютний альбом досі вважають вершиною східнопобережного хіп-хопу.",
            Title: "Illmatic",
            Year: 1994,
            DirName: "Nas - 1994 - Illmatic [Vinyl]",
            Description: "Десять треків, кожен сам по собі канон — продюсерська збірка від DJ Premier, Pete Rock, Q-Tip і Large Professor.",
            PrimaryGenre: "Hip-Hop",
            ExtraGenres: Array.Empty<string>()),

        new AlbumSpec(
            ArtistName: "Pink Floyd",
            ArtistCountry: "Велика Британія",
            ArtistBio: "Прогресив-рок-четвірка з Кембриджа, що тримала концептуальний альбом як основний формат своєї творчості.",
            Title: "The Dark Side of the Moon (50th Anniversary)",
            Year: 1973,
            DirName: "Pink Floyd - The Dark Side Of The Moon (50th Anniversary Remaster) - 1973 (2023)",
            Description: "Альбом-маніфест прогу: концепція про час, гроші, божевілля і смерть, склеєна синтезаторами і саундскейпами.",
            PrimaryGenre: "Rock",
            ExtraGenres: new[] { "Experimental" }),

        new AlbumSpec(
            ArtistName: "The Rolling Stones",
            ArtistCountry: "Велика Британія",
            ArtistBio: "Британська рок-н-рол-група з пагорбів Лондона, що зробила брудний блюзовий звук фірмовим знаком 60-х і далі.",
            Title: "Exile on Main St.",
            Year: 1972,
            DirName: "Rolling Stones - 1972 - Exile on Main St. {42DP-601 1986 Japan 1st CD Issue}",
            Description: "Подвійний альбом, записаний у французькій віллі: блюз, кантрі, госпел і ніч у форматі рок-н-ролу.",
            PrimaryGenre: "Rock",
            ExtraGenres: Array.Empty<string>()),

        new AlbumSpec(
            ArtistName: "The Beatles",
            ArtistCountry: "Велика Британія",
            ArtistBio: "Ліверпульський квартет, що сформував поп-музику другої половини XX століття.",
            Title: "Abbey Road",
            Year: 1969,
            DirName: "The Beatles - Abbey Road (1969) (2012 180g Vinyl 24bit-96kHz) [FLAC] vtwin88cube",
            Description: "Останній спільно записаний альбом гурту — друга сторона — суцільна медлі-сюїта.",
            PrimaryGenre: "Rock",
            ExtraGenres: Array.Empty<string>()),

        new AlbumSpec(
            ArtistName: "The Chemical Brothers",
            ArtistCountry: "Велика Британія",
            ArtistBio: "Манчестерський дует, що поставив біг-біт у клуби і на стадіони — танець з гітарною агресією.",
            Title: "Dig Your Own Hole",
            Year: 1997,
            DirName: "The Chemical Brothers – Dig Your Own Hole -1997",
            Description: "Кислота, фанк-брейки і Ноель Галлахер як гість — епохальний альбом біг-біту.",
            PrimaryGenre: "Electronic",
            ExtraGenres: new[] { "Experimental" }),

        new AlbumSpec(
            ArtistName: "The Dave Brubeck Quartet",
            ArtistCountry: "США",
            ArtistBio: "Каліфорнійський квартет, що першим зробив незвичні ритмічні розміри (5/4, 9/8) головною темою альбому.",
            Title: "Time Out",
            Year: 1959,
            DirName: "The Dave Brubeck Quartet - Time Out (K2HD) {2011} [8-8697883532]",
            Description: "Експерименти з тактовою сіткою джазу — «Take Five» зробила альбом і саксофон Пола Десмонда легендою.",
            PrimaryGenre: "Jazz",
            ExtraGenres: Array.Empty<string>()),

        new AlbumSpec(
            ArtistName: "The Smiths",
            ArtistCountry: "Велика Британія",
            ArtistBio: "Манчестерський квартет 80-х: гітарний джанґл Джонні Марра і театральний голос Моррісі.",
            Title: "The Queen Is Dead",
            Year: 1986,
            DirName: "The Smiths - The Queen Is Dead (1986) FLAC 88",
            Description: "Вершина творчості гурту: іронія, мелодрамa і нескінченні гітарні гачки.",
            PrimaryGenre: "Indie",
            ExtraGenres: new[] { "Rock" }),

        new AlbumSpec(
            ArtistName: "Travis Scott",
            ArtistCountry: "США",
            ArtistBio: "Х'юстонський репер і продюсер, що зробив авто-тюн і психоделічний треп фірмовим звуком кінця 2010-х.",
            Title: "UTOPIA",
            Year: 2023,
            DirName: "Travis Scott - UTOPIA - 2023",
            Description: "Багаторічний майбутній альбом La Flame — фіти, гучна продукція і утопічна меланхолія.",
            PrimaryGenre: "Hip-Hop",
            ExtraGenres: new[] { "Experimental" }),
    };

    public static (List<Genre> Genres, List<Artist> Artists, List<Album> Albums,
                   List<Product> Products, List<Review> Reviews, List<Order> Orders,
                   List<AlbumGenre> AlbumGenres)
        Build()
    {
        var musicRoot = ResolveMusicRoot();

        // Always seed the canonical 8 genres so filters/facets stay consistent
        // regardless of which albums the user actually has on disk.
        var genres = new List<Genre>();
        int genreId = 1;
        var genreLookup = new Dictionary<string, Genre>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in BasicGenres)
        {
            var g = new Genre { Id = genreId++, Name = name };
            genres.Add(g);
            genreLookup[name] = g;
        }

        var artists = new List<Artist>();
        var artistLookup = new Dictionary<string, Artist>(StringComparer.OrdinalIgnoreCase);
        int artistId = 1;
        foreach (var spec in Specs)
        {
            if (artistLookup.ContainsKey(spec.ArtistName)) continue;
            var a = new Artist
            {
                Id = artistId++,
                Name = spec.ArtistName,
                Country = spec.ArtistCountry,
                Description = spec.ArtistBio
            };
            artists.Add(a);
            artistLookup[spec.ArtistName] = a;
        }

        var albums = new List<Album>();
        var albumGenres = new List<AlbumGenre>();
        int albumId = 1;
        foreach (var spec in Specs)
        {
            var albumDir = musicRoot is null ? null : Path.Combine(musicRoot, spec.DirName);
            var hasDir = albumDir is not null && Directory.Exists(albumDir);

            var artist = artistLookup[spec.ArtistName];
            var album = new Album
            {
                Id = albumId,
                ArtistId = artist.Id,
                Artist = artist,
                Title = spec.Title,
                Year = spec.Year,
                Description = spec.Description,
                CoverPath = hasDir ? FindCover(albumDir!) : null
            };

            // many-to-many: primary (IsPrimary=true) + extras (deduped, IsPrimary=false)
            var tagNames = new[] { spec.PrimaryGenre }
                .Concat(spec.ExtraGenres)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in tagNames)
            {
                if (!genreLookup.TryGetValue(tag, out var g)) continue;
                albumGenres.Add(new AlbumGenre
                {
                    AlbumId = album.Id,
                    GenreId = g.Id,
                    IsPrimary = string.Equals(tag, spec.PrimaryGenre, StringComparison.OrdinalIgnoreCase)
                });
            }

            album.Tracks = hasDir
                ? BuildTracksFromDirectory(albumId, albumDir!)
                : new List<Track>();

            albums.Add(album);
            albumId++;
        }

        // Products: vinyl + CD per album, deterministic pseudo-random pricing seeded by AlbumId.
        var products = new List<Product>();
        int pid = 1;
        foreach (var album in albums)
        {
            var rnd = new Random(album.Id * 17);
            var (vinylPrice, cdPrice, deluxeVinyl) = PricingFor(album.Id);
            // Aggregates (Rating/ReviewCount/SalesCount) are maintained by SQLite
            // triggers — leave them at the default 0; the triggers will reconcile
            // when Reviews and OrderItems get inserted below.
            products.Add(new Product
            {
                Id = pid++,
                AlbumId = album.Id,
                Album = album,
                Format = ProductFormat.Vinyl,
                Price = vinylPrice,
                Stock = rnd.Next(0, 8),
                ReleaseYear = album.Year,
                Label = deluxeVinyl ? "Hi-Fidelity Records · Deluxe" : "Hi-Fidelity Records",
                IsActive = true
            });
            products.Add(new Product
            {
                Id = pid++,
                AlbumId = album.Id,
                Album = album,
                Format = ProductFormat.CD,
                Price = cdPrice,
                Stock = rnd.Next(0, 14),
                ReleaseYear = album.Year,
                Label = "Hi-Fidelity Records",
                IsActive = true
            });
        }

        // Catalog reviews are seeded in DbSeeder.SeedTestActivity instead: it
        // resolves products by album title and attributes each review to a real
        // seeded customer. Hardcoded ProductId/UserId pairs here drifted whenever
        // the catalog changed and leaked foreign-looking reviews into the
        // admin/demo profiles («Мої відгуки» showed other people's texts).
        var reviews = new List<Review>();

        var orders = new List<Order>
        {
            new()
            {
                Id = 1, UserId = 2, CreatedAt = new DateTime(2026, 5, 1),
                Status = OrderStatus.Completed,
                TotalAmount = products[0].Price + products[2].Price * 2,
                UserEmail = "demo@musicstore.local",
                ShippingAddress = "м. Київ, вул. Хрещатик, 1, кв. 5",
                Currency = "UAH",
                Items = new()
                {
                    BuildOrderItem(id: 1, orderId: 1, product: products[0], quantity: 1),
                    BuildOrderItem(id: 2, orderId: 1, product: products[2], quantity: 2),
                }
            },
            new()
            {
                Id = 2, UserId = 2, CreatedAt = new DateTime(2026, 5, 14),
                Status = OrderStatus.Processing,
                TotalAmount = products[18].Price,
                UserEmail = "demo@musicstore.local",
                ShippingAddress = "м. Київ, вул. Хрещатик, 1, кв. 5",
                Currency = "UAH",
                Items = new()
                {
                    BuildOrderItem(id: 3, orderId: 2, product: products[18], quantity: 1),
                }
            }
        };

        return (genres, artists, albums, products, reviews, orders, albumGenres);
    }

    // Demo price grid, deterministic by AlbumId so reseeding (and the DbSeeder
    // pricing backfill) always lands on the same numbers. Three tiers so every
    // catalog price bucket is populated: CDs are the budget shelf, standard
    // vinyl the middle one, and roughly every third album carries a deluxe
    // pressing (box set / colored LP) priced into the premium range.
    public static (decimal VinylPrice, decimal CdPrice, bool DeluxeVinyl) PricingFor(int albumId)
    {
        var rnd = new Random(albumId * 17);
        var deluxe = rnd.Next(0, 3) == 0;
        decimal vinyl = deluxe
            ? 1200 + rnd.Next(0, 27) * 50   // 1200–2500 ₴, крок 50
            : 600 + rnd.Next(0, 10) * 50;   // 600–1050 ₴, крок 50
        decimal cd = 250 + rnd.Next(0, 8) * 25; // 250–425 ₴, крок 25
        return (vinyl, cd, deluxe);
    }

    private static OrderItem BuildOrderItem(int id, int orderId, Product product, int quantity)
    {
        var album = product.Album;
        return new OrderItem
        {
            Id = id,
            OrderId = orderId,
            ProductId = product.Id,
            Product = product,
            Quantity = quantity,
            UnitPrice = product.Price,
            ProductTitle = $"{album?.Title ?? "—"} ({product.FormatBadge})",
            AlbumTitle = album?.Title ?? "—",
            ArtistName = album?.Artist?.Name ?? "—",
            FormatLabel = product.FormatBadge,
        };
    }

    // ---------- helpers ----------

    private static string? ResolveMusicRoot()
    {
        var env = Environment.GetEnvironmentVariable("MUSICAPP_MUSIC_ROOT");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) return env;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidate = Path.Combine(home, "Downloads", "Music");
        return Directory.Exists(candidate) ? candidate : null;
    }

    private static string? FindCover(string albumDir)
    {
        // Prefer typical "primary" filenames; fall back to any image at top level,
        // then a CD subfolder. Returns absolute path.
        string[] names = { "cover.jpg", "cover.jpeg", "cover.png",
                           "folder.jpg", "folder.png",
                           "front.jpg", "front.jpeg", "front.png",
                           "album.jpg", "album.png" };
        foreach (var n in names)
        {
            var p = Directory.EnumerateFiles(albumDir).FirstOrDefault(f =>
                string.Equals(Path.GetFileName(f), n, StringComparison.OrdinalIgnoreCase));
            if (p is not null) return p;
        }
        // Any *.jpg/.jpeg/.png at top level that isn't a known sidecar.
        var fallback = Directory.EnumerateFiles(albumDir)
            .Where(f =>
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext is not (".jpg" or ".jpeg" or ".png")) return false;
                var name = Path.GetFileNameWithoutExtension(f);
                return !name.Contains("Spectrogram", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("Jolly", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("DR", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("spek", StringComparison.OrdinalIgnoreCase);
            })
            .FirstOrDefault();
        if (fallback is not null) return fallback;

        // Try first subdirectory's folder.jpg (e.g. "CD 1/folder.jpg").
        foreach (var sub in Directory.EnumerateDirectories(albumDir))
        {
            var nested = Path.Combine(sub, "folder.jpg");
            if (File.Exists(nested)) return nested;
        }
        return null;
    }

    private static readonly Regex LeadNumberRegex = new(
        @"^\s*(?:[A-D]?\d{1,3})[\s\.\-_]*", RegexOptions.Compiled);

    private static List<Track> BuildTracksFromDirectory(int albumId, string albumDir)
    {
        string[] audioExt = { ".mp3", ".opus", ".flac", ".m4a", ".wav", ".ogg" };
        var files = Directory.EnumerateFiles(albumDir, "*", SearchOption.AllDirectories)
            .Where(f => audioExt.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tracks = new List<Track>();
        int pos = 1;
        foreach (var path in files)
        {
            var raw = Path.GetFileNameWithoutExtension(path);
            var title = LeadNumberRegex.Replace(raw, string.Empty).Trim();
            if (title.Length == 0) title = raw;

            // Use the embedded tag's title and duration when present — falls back to the
            // filename heuristic and zero-length for files TagLib can't read.
            var duration = TimeSpan.Zero;
            try
            {
                using var tagFile = TagLib.File.Create(path);
                if (!string.IsNullOrWhiteSpace(tagFile.Tag.Title))
                    title = tagFile.Tag.Title;
                if (tagFile.Properties?.Duration is { TotalSeconds: > 0 } d)
                    duration = d;
            }
            catch { /* unreadable file — keep title from filename, duration = 0 */ }

            tracks.Add(new Track
            {
                Id = albumId * 1000 + pos,
                AlbumId = albumId,
                Position = pos,
                Title = title,
                FullPath = path,
                SamplePath = path,
                SampleStartSeconds = 30,
                Duration = duration
            });
            pos++;
        }
        return tracks;
    }

    private sealed record AlbumSpec(
        string ArtistName,
        string ArtistCountry,
        string ArtistBio,
        string Title,
        int Year,
        string DirName,
        string Description,
        string PrimaryGenre,
        string[] ExtraGenres);
}
