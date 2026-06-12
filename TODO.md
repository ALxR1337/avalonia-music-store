# TODO «Музичний магазин»

Детальний роздроблений список задач відносно `maket_programy.md`.
Звірено з кодом (повний аудит робочого дерева) — 2026-06-12.

## Легенда статусів

- `[ ]` — не зроблено
- `[~]` — в процесі
- `[?]` — зроблено в коді, але не затверджено / не верифіковано користувачем
- `[x]` — зроблено і затверджено
- `[!]` — реалізовано, але **розходиться зі специфікацією** (потребує рішення: міняти код чи специфікацію)
- `[-]` — свідомо відкинуто (поза scope, з мотивацією)

---

## 1. Інфраструктура та стек

### 1.1. Платформа і пакети

- [x] .NET ціль (`net10.0`) — специфікацію оновлено під код
- [x] Avalonia версія (`12.0.3`) — специфікацію оновлено під код
- [x] Avalonia UI підключено (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`)
- [x] `CommunityToolkit.Mvvm` 8.4.2 підключено
- [-] `Semi.Avalonia` — видалено з csproj: ніде не використовувався
- [x] `Projektanker.Icons.Avalonia.FontAwesome` підключено; основні іконки — власні Lucide-style геометрії в `Themes/Icons.axaml`
- [x] **LibVLCSharp 3.9.3** + native libvlc (`VideoLAN.LibVLC.Windows` / `.Mac` за умовою ОС; Linux — системний vlc)
- [x] **Entity Framework Core 10 (Sqlite)** — `Microsoft.EntityFrameworkCore.Sqlite` 10.0.8
- [x] **BCrypt.Net-Next 4.x** — додано
- [x] **TagLibSharp 2.x** — додано
- [x] **ClosedXML 0.105** — додано в Wave 6
- [-] (опціонально) **ScottPlot.Avalonia** — свідомо відкладено, KPI + Топ-10 поки що достатньо

### 1.2. Кросплатформенні нюанси

- [x] Логіка масштабування під Linux/XWayland (`Program.ConfigureLinuxScaling`) — GDK/Qt/Niri/GNOME/KDE детект + `MUSICAPP_SCALE` override
- [x] Перевірка libvlc на Linux (Arch: `pacman -Qi vlc` — `libvlc.so.5` присутній)
- [ ] Перевірка native libvlc під Windows і macOS — потребує реального запуску на цих ОС (NuGet-пакети підключені умовно по ОС; інструкція в README)

### 1.3. Файлова структура та шляхи

- [x] Шлях до БД через `Environment.GetFolderPath(SpecialFolder.ApplicationData)` → `MusicStore/store.db` (з override `MUSICAPP_DB_PATH` для тестів)
- [x] Створення директорії при першому запуску (`DbContext.OnConfiguring`)
- [-] Структура папок для медіа (`samples/`, `full/`, `covers/`) — вирішено інакше: шляхи треків скануються з файлової системи при сіді, обкладинки/фото — бандлені `Assets/covers/` + `Assets/artists/`, адмін задає шляхи через `OpenFileDialog`

---

## 2. Модель даних і БД

### 2.1. POCO-моделі

- [x] `Models/Artist.cs`, `Genre.cs`, `Album.cs`, `Track.cs`, `Product.cs`, `User.cs`, `CartItem.cs`, `Order.cs`+`OrderItem`, `Review.cs`, `Playlist.cs`+`PlaylistTrack.cs`, `Enums.cs`, `NewArrivalAlbum.cs`
- [x] `Models/PlayerSettings.cs` (UserId, Volume, RepeatMode, ShuffleMode, LastTrackId) — Wave 1
- [x] `Models/SearchHistory.cs`, `Models/SavedSearch.cs` — Wave 1
- [x] `Models/Wishlist.cs` (UserId, ProductId, AddedAt) — Wave 5
- [x] `Models/AlbumGenre.cs` — багато-жанрові теги альбомів (з primary-флагом)
- [x] `Models/TrackLike.cs` + `Models/AlbumLike.cs` — лайки (Wave Likes)
- [x] `Track.Lyrics`, `Track.SamplePath`, `Track.FullPath`, `Track.SampleStartSeconds` — присутні

### 2.2. EF Core DbContext

- [x] `Data/MusicStoreDbContext.cs` (`DbSet<>` для всіх таблиць)
- [x] Конфіги через Fluent API (`OnModelCreating`): ключі, зв'язки, індекси
- [x] Конвертер `ProductFormat` / `OrderStatus` / `UserRole` / `RepeatMode` ↔ string
- [x] Міграції: Initial, AddArtistPhotoAndSampleStart, AddPlayerSettingsSearchHistorySavedSearches, AddWishlist, AddAlbumGenres, SchemaHardening, AddLikes
- [x] `Database.Migrate()` при старті (`DbSeeder.EnsureSeeded`)
- [x] Seed-метод з BCrypt-демо-користувачами (`admin/admin`, `demo/demo`) + `SeedTestActivity` (демо-активність: замовлення, відгуки, лайки, плейлисти)

### 2.3. FTS5 пошуковий індекс

- [x] Virtual table `SearchIndex` (FTS5, tokenize: `unicode61 remove_diacritics 2`) — Wave 4
- [x] Тригери `INSERT/UPDATE/DELETE` на `Artists`, `Albums`, `Tracks`, `Reviews` для синхронізації індексу — Wave 4
- [x] Тригери агрегатів `Product.Rating`/`ReviewCount`/`SalesCount` (SalesCount рахує лише Completed-замовлення)
- [x] Бекфіл `Rating`/`ReviewCount` у `Fts5Initializer` — сидовані відгуки вставляються до створення тригерів і не потрапляли в агрегати — Wave UI-Audit
- [x] Початкове заповнення індексу з існуючих рядків (`Fts5Initializer.Ensure`) — Wave 4
- [x] Утиліти для виконання `MATCH`-запитів — `SearchService.ExecuteFtsHits` через `SqliteConnection`

---

## 3. Сервісний шар

### 3.1. Авторизація `IAuthService`

- [x] Інтерфейс `IAuthService` + реалізація `AuthService` (TryLogin/TryRegister/LoginAsGuest/Logout)
- [x] Справжній логін через БД + BCrypt-перевірку — Wave 1
- [x] Реєстрація: валідація унікальності username/email, хешування BCrypt — Wave 1
- [x] Зміна пароля (`TryChangePassword`) — Wave 1
- [x] Сесія між запусками: `SessionStore` (session.json поряд із БД) + `TryRestoreSession` + чекбокс «Запам'ятати мене»

### 3.2. Каталог `ICatalogService`

- [x] Інтерфейс і реалізація `CatalogService` на основі EF Core
- [x] `GetProduct`, `GetAlbum`, `GetNewArrivals`, `GetNewArrivalAlbums`, `GetPopular`, `GetReviewsFor` — реалізовано
- [x] `SearchProducts(query)` — заміщено повним `SearchService` через FTS5 (Wave 4); старий метод залишився для зворотної сумісності
- [x] Каталог працює з EF Core (Albums/Products/etc. з БД, seed залишився як bootstrap для порожньої БД)
- [x] Адмін-методи: `AddProduct`, `UpdateProduct`, `SetProductActive` — Wave 6
- [x] `GetPurchasedAlbums(userId)` + `IsAlbumPurchased(albumId, userId)` — Wave 3
- [x] Відгуки користувача: `GetReviewsByUser` / `UpdateReview` / `DeleteReview` — Wave 7
- [x] `GetAlbumsByArtist`, `GetAlbumRating`, `GetReviewsForAlbum`, `GetPrimaryProductId` — Wave Player-Redesign
- [x] `GetWishlistProducts(userId)` (новіші перші, через in-memory кеш) + подія `WishlistChanged` — Wave Wishlist-UI; верифіковано регресійними тестами `WishlistUiTests` (2026-06-12)

### 3.3. Кошик `ICartService`

- [x] Інтерфейс і `CartService`; Add / Remove / UpdateQuantity / Clear / Checkout; подія `CartChanged`
- [x] Персистимо кошик у БД (`CartItems` таблиця) — Wave 3
- [x] При логіні з гостьового стану — мерджимо гостьовий кошик з кошиком користувача — Wave 3
- [x] Перевірка `Stock` при додаванні / збільшенні кількості — Wave 3
- [x] Фікс: товар зі `Stock == 0` більше не можна додати в кошик (`CartService.Add` ранній вихід; кнопка на картці товару деактивується через `CanExecute`) — Wave Wishlist-UI; верифіковано `CatalogCriticalFixTests.Out_of_stock_add_shows_refusal_toast_and_keeps_cart_intact` + `CartServiceTests`

### 3.4. Плеєр `IPlayerService`

- [x] Інтерфейс і `PlayerService`; події `MediaOpened`, `MediaEnded`, `PositionChanged`, `PlaybackStateChanged`, `ShuffleModeChanged`, `RepeatModeChanged`
- [x] `PlayAlbum`, `PlaySample`, `PlayFile`, `TogglePlayPause`, `Next`, `Previous`, `Seek`, `Stop`
- [x] Інтегровано LibVLCSharp: `LibVLC`, `MediaPlayer`, передача шляхів `SamplePath` / `FullPath` — Wave 2
- [x] Контроль гучності через `MediaPlayer.Volume` (мап 0..1 → 0..100) — Wave 2
- [x] Реальний `Seek` через `MediaPlayer.Time` — Wave 2
- [x] Обмеження 30 секунд для семплів від `SampleStartSeconds` — Wave 2
- [x] Перевірка прав на повне відтворення — Wave 3 (`IsAlbumPlayable` gate)
- [x] Збереження `PlayerSettings` (Volume, RepeatMode, ShuffleMode, LastTrackId) — Wave 2
- [x] Shuffle реально перемішує (random walk по альбому, якір на поточному треку; `Next`/`Previous` ходять по перестановці) — Wave Player-Critical-Fixes
- [x] `RepeatMode.Off` зупиняється в кінці альбому (раніше поводився як `All` і крутив альбом нескінченно) — Wave Player-Critical-Fixes
- [x] `Stop()` синхронно репортить `IsPlaying=false` + `PlaybackStateChanged` (LibVLC шле `Stopped` асинхронно) — Wave Player-Critical-Fixes
- [x] Подія `VolumeChanged`; `LoadSettingsForCurrentUser` шле Volume/Shuffle/Repeat-події, щоб UI не показував стан попереднього користувача — Wave Player-Important-Fixes
- [x] `LastTrackId` відновлюється при логіні: останній трек з'являється у міні-плеєрі на паузі («продовжити, де зупинився»); режим семпл/повний перевиводиться з власності; перший play холодного стану вантажить медіа через `StartTrack` — Wave Player-Cosmetic-Fixes
- [x] Persist гучності з debounce 500 мс (раніше — синхронний SQLite-запис на кожен тік драгу слайдера); flush у `Dispose` — Wave Player-Important-Fixes
- [x] `StartTrack` з відсутнім файлом зупиняє попереднє аудіо (раніше старий трек грав під назвою нового) — Wave Player-Important-Fixes
- [x] Повторний play після кінця семпла перезапускає семпл у 30-сек вікні через `StartTrack` (raw `Play()` стартував з 0:00 поза вікном) — Wave Player-Important-Fixes

### 3.5. Навігація `INavigationService`

- [x] Інтерфейс і реалізація з реєстрацією VM-фабрик; `NavigateTo(NavTarget, param?, section?)`
- [x] Стек історії «← Назад» + «Вперед →» (`GoBack`/`GoForward`) — Wave 5 / Tab-Redesign
- [x] Збереження scroll-offset сторінки і відновлення при назад/вперед (`SaveScroll`)
- [x] `CurrentSection`: детальні сторінки успадковують активну вкладку сайдбара

### 3.6. Пошук — окремий сервіс (АІПС-ядро)

- [x] `ISearchService` з методами `Search(string query, filters)`, `Autocomplete(string prefix)` — Wave 4
- [x] `SearchQueryParser` (рекурсивний спуск): вільний текст, `поле:значення`, `поле:від..до`, `поле:<X`, `-виключення`, `"точна фраза"` — Wave 4
- [x] AST → побудова комбінованого `MATCH` + `WHERE` запиту — Wave 4 (`SearchService.BuildMatchClause`)
- [x] Виконання FTS5 `MATCH` із BM25-ранжуванням (`-bm25(SearchIndex)`) — Wave 4
- [x] Обчислення фінального `score = BM25*0.6 + log(1+sales)*0.2 + (rating/5)*0.1 + recency*0.1` — Wave 4 (пояснення: `docs/RANKING.md`)
- [x] Альбомо-центрична видача: збіги треків/текстів/виконавців згортаються до альбомів (`RollupToAlbums`) — Wave Search-Redesign
- [x] Фасетна навігація: жанри (multi-select, any/all), виконавці, формат, наявність — лічильники поверх поточного результату
- [x] Динамічний перерахунок лічильників при зміні фільтрів — Wave 4; Wave Search-UX-Audit: лічильники чесні по всіх вимірах (кожен враховує всі фільтри, КРІМ власного виміру — ціна/рейтинг/наявність більше не «обіцяють» альбоми, яких видача не покаже), buckets не зникають при занулених фільтрах, кап топ-12 виконавців знято («Показати всі (N)» = справді всі)
- [x] Автодоповнення: префіксний `MATCH 'deat*'` + сортування по `-bm25` + обкладинки/фото — Wave 4; Wave Search-UX-Audit: багатослівний ввід (`december 2` → `december 2*`; раніше пробіли з'їдалися і підказки вмирали на другому слові), локалізований підзаголовок (трек каже «трек · з альбому «…»» замість брехливого «album»), дедуп альбом/однойменний трек, історія запитів у порожньому полі (kind `history`)
- [x] Debounce 200мс у UI (`MainWindowViewModel.OnSearchQueryChanged` → `DispatcherTimer`) — Wave 4; Wave Search-UX-Audit: таймер гаситься при Enter/виборі підказки/програмних записах (`_suppressSuggestions`) — попап більше не випадає поверх сторінки результатів через 200мс
- [x] Нечіткий пошук («Чи мали ви на увазі») — Wave 4; Wave Search-UX-Audit: порівнюється ВЕСЬ вільний текст (багатослівні опечатки типу «Bobx Dylan» працюють), distance 0 відсікається (не «пропонує» те саме слово), показ лише при 0 кандидатів від тексту, корекція перевіряється на наявність хітів
- [x] Запис `SearchHistory` — Wave 4; Wave Search-UX-Audit: один рядок на текст запиту (тогл фасета більше не спамить), `RecentQueries(userId)` віддає історію в підказки
- [x] `SavedSearches`: CRUD + флаг `NotifyOnNew` + поточна к-ть матчів у профілі — Wave 4/7; Wave Search-UX-Audit: дедуп ідентичних запитів, ім'я = людський `HeaderLabel` (DSL лишається в QueryJson), гостям кнопка disabled з тултипом, фідбек «Збережено ✓ / уже збережено» під кнопкою
- [x] Хибні запити (`++`, `(((`) не валять застосунок: `EscapeTerm` відкидає порожні терми, `MATCH` обгорнуто catch `DbException` → порожня видача — Wave Search-UX-Audit

### 3.7. Лайки `ILikesService`

- [x] `LikesService`: лайки треків і альбомів (ідемпотентні, подія `Changed`), таблиці `TrackLikes`/`AlbumLikes` — Wave Likes

---

## 4. UI: вікно та глобальна оболонка

### 4.1. `MainWindow`

- [x] Кастомний title bar (`ExtendClientAreaToDecorationsHint`, `WindowDecorations="BorderOnly"`)
- [x] Поле пошуку по центру title bar, `Text="{Binding SearchQuery}"`, Enter → `SubmitSearch`
- [x] Drag вікна через `PointerPressed` → `BeginMoveDrag`; кнопки Min / Max / Close
- [x] Sidebar (Каталог, Пошук, Кошик, Профіль, Плеєр, Адмін) — окрему вкладку «Замовлення» прибрано у Wave Orders-Merge: вона дублювала багатшу підвкладку профілю; історія замовлень тепер лише у Профілі (свідоме відхилення від макета §3, який малює обидва місця)
- [x] Видимість секції «Адмін» по `IsAdmin`
- [x] `ContentControl` для CurrentView
- [x] Mini-player у нижній рядці
- [x] Розміри: мін. 1024×640, default 1280×800 — відповідає специфікації
- [x] Колапсація sidebar до іконок 72px при ширині < 1100px — Wave 8 (`BoolToWidthConverter`)
- [x] Підсвічування активного пункту в sidebar по `CurrentSection` — Wave 8 / Tab-Redesign
- [x] Бейдж кількості товарів у кошику біля «Кошик» — Wave 8 (`CartCount` + `HasCartItems`)
- [x] Меню користувача (профіль/налаштування/вихід) у title bar — Wave UserMenu
- [x] Кнопки навігації назад/вперед з історією — Wave Tab-Redesign

### 4.2. Mini-player

- [x] `MiniPlayerView` + `MiniPlayerViewModel`; поява при `MediaOpened`
- [x] Кнопка [✕] → `CloseMiniPlayer`: зупиняє відтворення і ховає бар; [⛶] → `ExpandMiniPlayer`: відкриває сторінку плеєра, бар лишається видимим (єдиний transport-UI застосунку). Інваріант: якщо грає звук — бар на екрані (`PlaybackStateChanged` → show) — Wave Player-Critical-Fixes
- [x] Плавна анімація появи/зникнення (translateY + Opacity) — Wave 8
- [x] Мала обкладинка (cover/gradient/FirstChar fallback) — Wave 8
- [x] Регулятор гучності та seek-слайдер у міні-плеєрі — Wave 8 / MiniPlayer-BottomBar
- [x] Слайдер гучності слухає `VolumeChanged` (після логіну показував стале 70% замість збереженого рівня) — Wave Player-Important-Fixes
- [x] Seek з клавіатури: стрілки на слайдері комітять перемотку (раніше значення відскакувало) — Wave Player-Important-Fixes
- [x] Тултіпи на всіх icon-кнопках бара (prev/play/next/гучність/⛶/✕); play-тултіп згадує Пробіл — Wave Player-Important-Fixes
- [x] Next/Prev задизейблені без черги (локальний файл через `PlayFile`) — `CanExecute(HasQueue)` — Wave Player-Important-Fixes
- [x] Медіа-клавіші `MediaPlayPause`/`MediaNextTrack`/`MediaPreviousTrack` у `OnGlobalKeyDown`; Space і медіа-клавіші діють лише коли трек завантажено (інакше пропускаються до сфокусованого контрола). Глобальний Space над сфокусованою кнопкою — свідомий Spotify-стиль (див. PlayerHotkeyAndCoverTests) — Wave Player-Important-Fixes
- [x] Shuffle/Repeat-індикатори у барі (обабіч prev/next, accent при увімкненому, тултіпи зі станом) — Wave Player-Cosmetic-Fixes
- [x] Іконка гучності → mute-кнопка (перекреслена іконка при 0, відновлення останнього рівня) — Wave Player-Cosmetic-Fixes
- [x] Обкладинка-заглушка для безальбомного відтворення (іконка навушників замість сірого квадрата); текст треку обрізається по колонці, а не по 220px; тайм-коди >1 год — `h:mm:ss` — Wave Player-Cosmetic-Fixes

---

## 5. UI: екрани

### 5.1. Каталог (`CatalogView` + `CatalogViewModel`)

- [x] Hero-блок з привітанням
- [x] Секція жанрів (плитки з градієнтами) — Wave Catalog-Redesign
- [x] Секція виконавців (круглі аватари з фото/fallback) — Wave Catalog-Redesign
- [x] Швидкі фільтри за ціною/рейтингом (структуровані запити) — Wave Catalog-Redesign
- [x] Секція «Нові надходження» — картки з обкладинкою, LP/CD цінами
- [x] ContextFlyout на картках: «Додати в кошик», прослухати семпл
- [x] Кнопка ▶ на обкладинці → `QuickPreview`; клік по картці → `OpenProduct`; клік по жанру → пошук `жанр:X`
- [x] Реальні обкладинки — seed з 26 альбомами + бандлені Assets
- [x] «Нові надходження» фільтрують `IsActive` (деактивовані видання не потрапляють на вітрину; альбом без активних видань випадає цілком; `PrimaryProduct` тепер nullable) — Wave Catalog-UX-Audit; верифіковано `CatalogCriticalFixTests` (deactivated-edition / all-editions-deactivated)
- [x] Тост-фідбек додавання в кошик з вітрини («додано» / «немає в наявності», viewport-pinned за паттерном AdminView) + пункти ContextFlyout деактивуються при нульовому залишку — Wave Catalog-UX-Audit; верифіковано `CatalogCriticalFixTests` (confirmation/refusal toast)
- [x] LP/CD-рядки на картках = «додати в кошик» (іконка кошика, дизейбл + «немає» при нульовому залишку); сторінку альбому відкриває клік по картці — Wave Catalog-UX-Audit; верифіковано `CatalogImportantFixTests.Out_of_stock_edition_row_is_disabled_and_says_so`
- [x] Hero-CTA називає альбом («Слухати: …»), `CanExecute` вимикає кнопку без треків; секцію перейменовано «Нові релізи» (сортування за роком — чесна назва), лінк «Увесь каталог →» — Wave Catalog-UX-Audit; верифіковано `CatalogImportantFixTests.Hero_cta_names_the_album_it_will_play`
- [x] `жанр:"…"` квотується (мультислівні жанри не розвалюють запит); рейтинг-чипи з живими лічильниками і culling порожніх (як цінові); chevron-кнопки полиць не фокусуються з клавіатури — Wave Catalog-UX-Audit; верифіковано `CatalogImportantFixTests` (quoted genre ×2, chevron tab stops)
- [x] Косметика (Wave Catalog-UX-Audit): hero-заголовок одним TextBlock з Run-ами (вміє переноситись), підзаголовок без «сотні»; плитки жанрів — скрим + ellipsis + лічильник альбомів (`CountLabel` нарешті відображається); виньєтки/hero на ресурсі `BgBaseTransparent` замість хардкоду #121212; крок прокрутки полиць = ціле число тайлів конкретної полиці; chevron полиці новинок відцентровано по обкладинці (62px); AutomationProperties.Name на плитках/кнопках/chevron-ах; порожні секції ховаються (`Has*` гейти) — верифіковано `CatalogCosmeticTests` + артефакти `CatalogUxAuditTests`
- [-] Лічильники цінових чипів: межі діапазонів навмисно інклюзивні з обох боків і «будь-яке видання в діапазоні» — кожен чип точно збігається зі своєю пошуковою видачею; «виправлення» перекриття розсинхронізувало б чип і видачу

### 5.2. Сторінка результатів пошуку (`SearchResultsView` + VM)

- [x] Контейнер з фасетами зліва (280px) + результатами справа
- [x] Альбомо-центрична видача (`AlbumHit` з приміткою «знайдено в треку…») — Wave Search-Redesign
- [x] Фасети: Жанр (multi-select + any/all), Виконавці, Формат, Наявність — з живими лічильниками
- [x] Live-update: чекбокс/спін → миттєвий перезапит — Wave 4
- [-] Chip-tabs «Усі/Альбоми/Виконавці/Треки/Відгуки» — свідомо знято при альбомо-центричному редизайні (диверсія від макета §4.1.3 затверджена)
- [x] Sticky sidebar при скролі — Wave Search-UX-Audit: сторінка фіксується на висоту viewport (code-behind підписується на `Viewport` зовнішнього ScrollViewer), скрол живе у колонці результатів (`ResultsScroll`), позиція скролу зберігається у VM і відновлюється на back/forward
- [x] Активні фільтри як видаляні chips над результатами — Wave 8
- [x] Адаптивна сітка результатів: картки розтягуються на всю ширину колонки (`Controls/UniformWrapPanel` + `Square`) — Wave UI-Audit
- [x] Колапс довгих фасет-списків (виконавці): топ-8 + «Показати всі (N)» (`FacetGroupViewModel`) — Wave UI-Audit
- [x] Топ-результат показує обкладинку альбому (зірка — лише fallback) — Wave UI-Audit
- [x] Фасети «Рік», «Ціна» (NumericUpDown від/до), «Рейтинг ★ від N» — Wave 4
- [x] Чекбокс «Тільки в наявності» — facet bucket
- [x] Кнопки «Скинути все», «💾 Зберегти запит» — Wave 4
- [-] Колапс фасет-сайдбара в `[≡ Фільтри]` при <1100px — лишаємо як є
- [x] Топ-результат (окремий блок) — Wave 8; Wave Search-UX-Audit: лише при текстовому запиті та ≥2 результатів (у browse-режимі та при єдиному результаті блок дублював першу картку)
- [x] «Чи мали ви на увазі: …» — Wave 4 (умови показу див. 3.6 / Wave Search-UX-Audit)
- [x] Автодоповнення під полем пошуку в title bar (Popup, з обкладинками) — Wave 4
- [x] Wave Search-UX-Audit (сторінка): empty-state з діями «Скинути фільтри» / «Показати всі альбоми» (`ShowAllCommand`); клік по «Пошук» у сайдбарі НЕ пересоздає сторінку (guard у `MainWindowViewModel.Navigate`); тогл активного формат-фасета знімає фільтр першим кліком (порівняння через `ParseFormat`, не label); рядок пошуку в title bar синхронізований зі сторінкою (genre-tile очищає, did-you-mean оновлює)
- [x] Wave Search-UX-Audit (search box): клавіатура у підказках (↑/↓ + Enter, Esc закриває/очищає, `SuggestionItemViewModel.IsHighlighted`), кнопка ✕ очищення, фокус у порожньому полі показує нещодавні запити користувача

### 5.3. Картка товару (`ProductView` + `ProductViewModel`)

- [x] Hero: обкладинка + назва, виконавець, чіпси Жанр/Рік; ціна, залишок, рейтинг
- [x] Кнопки «🛒 Додати в кошик», «♡ Зберегти» (через `Wishlist`) — Wave 5
- [x] Треклист з кнопкою ▶ на кожному треку (семпл) — Wave 5
- [x] Опис альбому + біографія виконавця
- [x] Секція «Відгуки»: перші 3 + «Показати всі» — Wave 5
- [x] Кнопка «← Назад» (`NavigationService.GoBack`) — Wave 5
- [x] Перемикач формату «⚫ Вініл LP | ○ CD» (`GetSiblingProduct`) — Wave 5
- [x] Форма «Залишити відгук» для авторизованих покупців (`CanLeaveReview` gate) — Wave 5

### 5.4. Кошик (`CartView` + `CartViewModel`)

- [x] Список товарів з мініатюрою, [-] кількість [+], кнопкою «Видалити»; підрахунок суми
- [x] «Оформити замовлення» → `Checkout` (зберігає в `Orders`, декрементить stock)
- [x] Двокроковий checkout: inline-форма (спосіб отримання, коментар, контакт з профілю, підсумок) → «Підтвердити» → екран успіху з кнопками «До замовлень» / «Продовжити покупки» — Wave Checkout-Flow
- [x] Способи отримання: самовивіз з 4 магазинів (ComboBox, реальні київські адреси, дефолт) або Нова Пошта (місто + відділення, обидва обов'язкові; цифрове відділення нормалізується в «відділення №N») — Wave Checkout-Flow
- [x] `Order.Comment` + заповнення `ShippingAddress` (міграція `AddOrderComment`); адреса префілиться з останнього замовлення; деталі замовлення (Профіль → «Замовлення») показують адресу/коментар — Wave Checkout-Flow, перенесено у профіль у Wave Orders-Merge
- [x] `FlashMessage` після оформлення → замінено екраном успіху (flash лишився для guest-блокування) — Wave Checkout-Flow
- [x] Перевірка авторизації перед `Checkout` — Wave 8 (guest banner + блокування)
- [x] Секція «Купіть також» (той самий виконавець → жанр → бестселери; для порожнього кошика — «Можливо, вас зацікавить») — Wave UI-Audit
- [x] Мініатюри товарів 72px; блок «Разом до сплати» прихований при порожньому кошику — Wave UI-Audit
- [x] Секція «Збережене» під кошиком (saved-for-later): рядки з обкладинкою/ціною, «До кошика» переносить товар у кошик і знімає лайк, серце прибирає зі збереженого; для відсутніх на складі — «Немає в наявності» — Wave Wishlist-UI; верифіковано `WishlistUiTests` (move-to-cart, out-of-stock guard)
- [-] Підтвердження «видалити товар?» — поза scope MVP
- [-] Промокоди / знижки — поза scope

### 5.5. Замовлення

- [-] Окрема сторінка `OrdersView` + `OrdersViewModel` видалена у Wave Orders-Merge: дублювала підвкладку «Замовлення» профілю, але без фільтра статусу. Історія замовлень покупця — Профіль → «Замовлення» (§5.6), керування всіма замовленнями для адміна — секція «Замовлення» адмінки (§5.8). Кнопка «До замовлень» після checkout і пункт меню в title bar ведуть у Профіль (пункт «Мої замовлення» прибрано як дубль «Профіль»)

### 5.6. Профіль (`ProfileView` + `ProfileViewModel`)

- [x] Дані користувача (ім'я, email, роль)
- [x] Підвкладка «Замовлення» — таблиця з фільтром статусу + inline-expand деталі — Wave 7; Wave Orders-Merge: успадкувала від видаленої `OrdersView` колонку «позиції» та адресу/коментар у деталях, додано підказку «Виконано → повне прослуховування у Плеєрі» і empty-state
- [x] Підвкладка «Мої відгуки» — список з редагуванням/видаленням — Wave 7
- [x] Зміна пароля — inline-панель `ChangePasswordView` (модальне вікно прибрано у Wave Inline-Panels)
- [x] Список збережених запитів з к-тю поточних матчів (`SavedSearchSummary.CurrentCount`) — Wave 7; Wave Search-UX-Audit-2: чекбокс «Сповіщати» прибрано — механізму сповіщень не існує, мертва обіцянка (поле `NotifyOnNew` лишається у схемі під майбутній notifier)
- [x] Підвкладка «Збережене» — сітка карток wishlist-альбомів: клік відкриває товар, серце на обкладинці прибирає зі збереженого — Wave Wishlist-UI; верифіковано `WishlistUiTests.Profile_saved_tab_lists_album_and_heart_removes_it`
- [-] Справжній дельта-індикатор «нових товарів» по збереженому запиту — без CreatedAt на Product/Album неточно; показуємо поточну к-ть матчів

### 5.7. Плеєр (`PlayerView` + `PlayerViewModel`)

- [x] Каркас VM з біндингами (TrackTitle, ArtistName, Progress, Volume…)
- [x] Список «куплених альбомів» (`GetPurchasedAlbums`, Status == Completed) — Wave 3
- [x] Drag-slider прогресу (Seek, `IsScrubbing` + `CommitSeek`) — Wave 8
- [x] Кнопка «+ Додати файли» (`OpenFileDialog`) → `IPlayerService.PlayFile` — Wave 8
- [x] Редизайн сторінки альбому: хедер, треклист, метадані, fullscreen-обкладинка, хоткеї — Wave Player-Redesign
- [x] Секція «Відгуки» на сторінці альбому (Player-Redesign §3): агрегат по всіх виданнях альбому, заголовок «★ N,N · N відгуків», топ-3 найновіших + «Показати всі», форма «Залишити відгук» лише для власників (постить у primary product); редагування/видалення власних відгуків — у Профіль → «Мої відгуки» (не дублюємо) — Wave Player-Reviews (2026-06-12); тести `PlayerReviewsTests`
- [x] Лайки треків у треклисті (`ILikesService`) — Wave Likes; лайк альбому (♥ у хедері, гість → login-оверлей) — Wave Player-Important-Fixes
- [x] Чесний стан некупленого альбому: ▶ і клік по треку грають 30-сек семпли (`PlaySample`) замість тихого no-op за purchase-гейтом; підказка «відтворюються семпли» + CTA «Купити альбом» → сторінка товару — Wave Player-Critical-Fixes
- [x] ▶ у хедері — toggle play/pause для поточного альбому (раніше рестартував з 1-го треку); іконка/тултіп відображають стан — Wave Player-Important-Fixes
- [x] Pause/track-advance → легкий апдейт (маркери поточного треку) замість повного `Refresh()`: розгорнутий опис не схлопується, треклист не перебудовується, без зайвих DB-запитів — Wave Player-Important-Fixes
- [x] Вся строка треку клікабельна (номер/тривалість більше не мертві зони); лайк вкладено і ковтає свої кліки — Wave Player-Important-Fixes
- [x] Гість: клік по ♥ (трек/альбом) відкриває login-оверлей замість тихого no-op — Wave Player-Important-Fixes
- [x] «Більше від артиста»: клік по плитці відкриває сам альбом (куплений → плеєр, у каталозі → сторінка товару, fallback → пошук за артистом) — Wave Player-Important-Fixes
- [x] Тайл «+» перейменовано на чесне «Відтворити локальний аудіофайл» (діалог відкриває один файл і лише програє його; плейлисти/папки — поза scope) — Wave Player-Important-Fixes
- [x] Тайл «+» тече в одній WrapPanel з альбомами (`LibraryItems` + DataTemplates за типом), картки не вилазять за слот 180px; empty-state вирівняно вліво — Wave Player-Cosmetic-Fixes
- [x] «Показати повністю» лише коли 3-рядковий кламп справді ховає текст (нульова вимірювальна копія у PlayerView); одна кнопка-toggle без невидимого таб-стопа — Wave Player-Cosmetic-Fixes
- [x] Українська плюралізація «N трек/треки/треків»; сумарна тривалість <1 хв — «N с» замість «0 хв»; колонка тривалості — MinWidth (вміщує h:mm:ss) — Wave Player-Cosmetic-Fixes
- [x] Тултіпи Shuffle/Repeat показують поточний стан («Повтор: весь альбом/один трек/вимкнено») — Wave Player-Cosmetic-Fixes
- [x] Видима кнопка ✕ (тултіп «Закрити (Esc)») на fullscreen-обкладинці — Wave Player-Cosmetic-Fixes
- [-] Reuse `Themes/DarkTheme.axaml` — заміщено `Themes/Colors.axaml` + `ControlStyles.axaml`
- [-] Drag&drop файлів у плейлист — поза scope MVP
- [-] Перенесення плейлистів з M3U в БД — поза scope MVP

### 5.8. Адмінка (`AdminView` + `AdminViewModel`)

> Редизайн (Wave Admin-Redesign): замість TabControl — chip-tab секції
> «Огляд / Товари / Замовлення / Користувачі»; KPI та «Статистика» об'єднані
> в секцію «Огляд». Свідомо відрізняється від макета §4.6 — не «відновлювати»
> TabControl.

- [x] KPI-картки (Товарів / Замовлень / Нові замовлення / Виручка) — у секції «Огляд»; Wave Admin-UX-2: картки клікабельні (відкривають відповідну секцію/фільтр), акцент на «Нові замовлення» лише при >0, підпис «Виручка (виконані)» чесно описує метрику, заголовок сторінки — «Керування магазином» (узгоджено з sidebar)
- [x] Chip-tab секції: «Огляд», «Товари», «Замовлення», «Користувачі» (Wave Admin-Redesign)
- [x] Секція «Товари»: пошук (альбом/виконавець/лейбл), заголовки колонок, бейджі LP/CD, «немає» для нульового залишку, ✎ / 🗑 (soft-delete `SetProductActive(false)`) — Wave 6, redesign
- [x] Wave Admin-UX: деактивація у два кліки (інлайн «Деактивувати? Так/Ні»), колонка «Стан» + затемнення неактивних, фільтр «Всі/Активні/Неактивні», реактивація одним кліком, нові товари зверху списку; Wave Admin-UX-3: ✕ у пошуку, empty-state з кнопкою «Скинути пошук і фільтр»
- [x] «+ Додати товар» / ✎ → inline-панель `ProductEditView` (двоколонкова форма; Album/Artist/Genre + «новий», file pickers; модальне вікно прибрано у Wave Inline-Panels); Wave Admin-UX-2: альбом обирається через AutoCompleteBox (пошук за назвою/виконавцем, у списку «Назва — Виконавець»), при виборі існуючого альбому селектори виконавця/жанру ховаються (підставляються з альбому, сводка показує що успадковано); Wave Admin-UX: підзаголовок ідентифікує товар («Альбом» — виконавець · формат · #id), guard незбережених змін на «Назад/Скасувати», зміна обкладинки працює і в режимі редагування (`UpdateProduct` приймає `coverPath`); Wave Admin-UX-3: тултипи з повним шляхом медіафайлів, ✕ для скидання вибраного файлу (лише add-режим — в edit сервіс не вміє стирати шляхи, тож кнопка брехала б), форма відцентрована
- [x] Wave Admin-UX: статус-тост — плавучий оверлей (без зсуву layout, прикріплений до viewport через `AdminView.axaml.cs`); помилки червоні і не зникають самі, успіхи ховаються за 5 с
- [x] CRUD для виконавців/альбомів/жанрів — inline через форму товару (окремі форми не потрібні)
- [x] Секція «Замовлення»: фільтр-чіпи за статусом, українські статуси, деталі розкриваються під рядком — Wave 6, redesign; Wave Admin-UX: статус зберігається одразу при виборі в ComboBox (тост підтверджує, окремої кнопки збереження немає) + пошук за № або покупцем; Wave Admin-UX-2: у колонці лише дата (час — у розгорнутих деталях «Створено: …»), звільнена ширина віддана колонці покупця; Wave Admin-UX-3: деталі замовлень розкриваються незалежно (можна порівняти два), дубль «Разом» прибрано (сума вже в рядку), «Отримання:» замість «Доставка:» (коректно і для самовивозу), пошук має кнопку ✕, empty-state пропонує «Показати всі замовлення». Телефон покупця не показується, бо модель Order його не зберігає (checkout не збирає) — окрема задача, якщо знадобиться
- [x] Секція «Огляд»: Топ-10 за продажами (з рангом) + «Виручка за період» з вибором дат — Wave 6, redesign; Wave Admin-UX: топ-10 агрегується по альбомах (LP+CD разом) з колонками «Продажі»/«Виручка»; Wave Admin-UX-2: періоди календарні («Цей тиждень» = з понеділка, «Цей місяць» = з 1-го числа), чип «Власний» підсвічується при ручних датах, інвертований діапазон Від>До показує попередження
- [x] Секція «Користувачі»: таблиця + ComboBox ролі (укр. назви), «Зберегти»/«Скасувати» лише при зміні — Wave 6, redesign; Wave Admin-UX: guard від зняття адмін-ролі з власного акаунта, роль «Гість» не пропонується, пошук за ім'ям/email
- [x] Експорт замовлень в Excel (ClosedXML) — Wave 6; Wave Admin-UX-2: товари теж експортуються в Excel (`ExportProductsToExcel`, CSV-варіант прибрано — один формат для обох розділів)
- [-] (опціонально) Діаграма продажів (ScottPlot.Avalonia) — свідомо відкладено
- [x] Вкладку бачить тільки `Admin` (через `IsAdmin`)

### 5.9. Авторизація (`LoginView` + `LoginViewModel`)

- [x] `LoginViewModel` (Username, Password, Email, IsRegistering, Error, Guest, RememberMe)
- [x] Логін — оверлей-картка всередині MainWindow (`LoginView`; окреме `LoginWindow` прибрано у Wave Login-Overlay)
- [x] Застосунок стартує як гість; оверлей логіну відкривається кнопкою «Увійти» у title bar або автоматично
- [x] «Запам'ятати мене» → відновлення сесії при наступному запуску (`TryRestoreSession`)
- [x] Реєстрація з валідацією унікальності username/email — Wave 1
- [x] BCrypt-перевірка (dev-quirk «admin → Admin» прибрано) — Wave 1
- [x] «Продовжити як гість» — працює

---

## 6. Дизайн і тема

- [x] Темна тема (`Themes/Colors.axaml` + `Themes/ControlStyles.axaml`) — Spotify-style палітра
- [x] Скругленість: токени `RadiusS/M/L/XL/Pill`
- [x] Український UI-текст
- [-] Reuse `Themes/DarkTheme.axaml` — заміщено новою темою
- [x] Акцентний колір `#E07B39` (WCAG-контраст задокументовано в Colors.axaml)
- [x] Шрифт Inter — `WithInterFont()` + `UiFont` ресурс
- [x] Іконки: власні Lucide-style геометрії в `Themes/Icons.axaml` (50+ штук, stroke-based + filled варіанти)
- [x] Культура `uk-UA` — Wave 8 (`Program.ConfigureUkrainianCulture`)
- [x] Масштаб типографіки/іконок/обкладинок ×1.25 — Wave Scale-Up
- [x] Hero-заголовок: прибрано фіксований `LineHeight` (різав нижні виносні «у», «р», «д») — Wave UI-Audit
- [x] Українська плюралізація лічильників (`UkrainianPluralConverter`: позиції / відгуки / продажі / результати) — Wave UI-Audit
- [x] Поля вводу на логін-картці отримали контрастний фон+рамку (зливались із BgElevated-карткою) — Wave UI-Audit

---

## 7. Конвертери та допоміжне

- [x] `AlbumIdToGradientBrushConverter`, `CoverPathToImageConverter`, `FirstCharConverter`
- [x] `OrderStatusToUkrainianConverter` — Wave 8
- [x] `GenreToBrushConverter`, `BoolToWidthConverter`, `BoolToHeartIconConverter`, `BoolToHighlightBrushConverter`, `BoolToOpacityConverter`, `IconKeyToGeometryConverter`, `DoubleOffsetConverter`, `RepeatModeToIconConverter`, `SuggestionKindToIconConverter`, `PlayerViewExtraConverters`, `DateTimeOffsetConverter`
- [-] Конвертер `bool → Visibility` — Avalonia має вбудоване `IsVisible`

---

## 8. Дані та seed

- [x] `Services/SampleData.cs` — 26 справжніх альбомів з `~/Downloads/Music`: виконавці, біографії, описи, обкладинки, треки з ФС, багато-жанрові теги
- [x] SampleData використовується тільки якщо БД порожня; `DbSeeder.WipeLegacyIfPresent` авто-замінює застарілий seed
- [x] Реальні обкладинки (`Album.CoverPath`) + бандлені fallback-обкладинки `Assets/covers/` і фото виконавців `Assets/artists/` (auto-backfill по slug)
- [x] Реальні семпли — `Track.FullPath`/`SamplePath` на справжні mp3/opus; `SampleStartSeconds=30`; тривалість з TagLib
- [x] Демо-користувачі (Admin + Customer) з BCrypt-паролями — `admin/admin`, `demo/demo`
- [x] `SeedTestActivity` — ідемпотентна демо-активність (замовлення, відгуки, лайки, плейлисти, історія пошуку) для тестованості фіч

---

## 9. Тестування і документація

- [x] Юніт-тести парсера запитів (`SearchQueryParserTests`: поля, аліаси, діапазони, компаратори, виключення, фрази) — Wave 9
- [x] Юніт-тести `CartService` (`CartServiceTests`: гостьовий/персистентний кошик, мердж, stock, checkout) — Wave 9
- [x] Юніт-тести `AuthService` (`AuthServiceTests`: BCrypt, унікальність, зміна пароля, remember-me) — Wave 9
- [x] Юніт-тести фасетів/агрегатів (`CatalogServiceExtensionsTests`) + `LikesServiceTests`, `SearchSuggestionsTests`
- [x] Integration-тести через `Avalonia.Headless` — `MusicApp.BugHunt/` (30+ файлів: логін-оверлей, inline-панелі, каталог, пошук, плеєр, міні-плеєр, навігація, hover, аватари/обкладинки)
- [x] Регресійні тести wishlist-UI (`WishlistUiTests`: newest-first + подія, move-to-cart, out-of-stock guard, вкладка профілю) — Wave Player-Reviews
- [x] Тести секції відгуків плеєра (`PlayerReviewsTests`: топ-3 + рейтинг-лейбл, «Показати всі», submit round-trip з прибиранням за собою, гість без форми) — Wave Player-Reviews
- [x] README з інструкцією запуску (Windows / Linux / macOS), включно з libvlc — Wave 9
- [x] Документація API сервісів — `docs/SERVICES.md` — Wave 9
- [x] Пояснення моделі ранжування для захисту — `docs/RANKING.md` (гіперпараметри §8.7) — Wave 9

---

## 10. Свідомо НЕ робимо (з специфікації)

- [-] Власна система оплати (статус міняє адмін вручну)
- [-] Рекомендаційний алгоритм («подібні альбоми») — повноцінного нема; у кошику є проста евристика «Купіть також» (артист/жанр/бестселери, Wave UI-Audit)
- [-] Мобільна версія
- [-] Повний MusicBrainz lookup
- [-] Багатокористувацький онлайн (БД локальна)

---

## Зведена статистика (після Wave Player-Reviews, перераховано механічно по рядках списку 2026-06-12)

| Категорія | Зроблено [x] | Не зроблено [ ] | Відкинуто [-] |
|---|---|---|---|
| Інфраструктура / стек | 14 | 1 | 3 |
| Модель даних і БД | 19 | 0 | 0 |
| Сервіси | 54 | 0 | 0 |
| UI: оболонка / mini-player | 26 | 0 | 0 |
| UI: екрани | 93 | 0 | 11 |
| Дизайн і тема | 11 | 0 | 1 |
| Конвертери / seed / тести | 19 | 0 | 1 |

Статусу `[?]` більше немає: усі пункти, що чекали на ручне затвердження,
закриті регресійними тестами (BugHunt) за делегуванням користувача
2026-06-12.

Єдиний відкритий пункт: ручна перевірка native libvlc під Windows/macOS —
неможлива з цієї машини (потрібен запуск на цільових ОС; NuGet-пакети
підключено умовно, інструкція в README).

**Виконано waves 1–9** (+ редизайни поза нумерацією):

- **Wave 1** — DB + Auth: EF Core SQLite з міграціями, BCrypt-авторизація, демо-користувачі.
- **Wave 2** — LibVLC: відтворення семплів/треків, гучність, seek, 30-сек cutoff, PlayerSettings.
- **Wave 3** — Catalog/Cart на DB: куплені альбоми, кошик у DB, мердж гостя, stock, gate відтворення.
- **Wave 4** — АІПС-пошук: FTS5 + тригери, парсер DSL, BM25 + композитний score, фасети, autocomplete, fuzzy, SearchHistory + SavedSearches.
- **Wave 5** — Картка товару: back, LP↔CD, відгуки + форма з gate, wishlist.
- **Wave 6** — Адмінка: CRUD продуктів, статуси замовлень, експорти Excel/CSV, користувачі, виручка за період.
- **Wave 7** — Кабінет: вкладки замовлень/відгуків/збережених запитів, зміна пароля.
- **Wave 8** — UI polish: uk-UA, колапс сайдбара, бейдж кошика, guest-banner, обкладинки, анімація міні-плеєра, drag-seek, локальні файли, chips + топ-результат.
- **Wave 9** — Тести + документація: юніт-тести парсера/кошика/авторизації, README, `docs/SERVICES.md`, `docs/RANKING.md`.
- **Редизайни** — Catalog/Search/Player/Tab-навігація, Login-оверлей, inline-панелі (без модальних вікон), лайки, сесія «запам'ятати мене», бандлені Assets, масштаб ×1.25.
- **Wave Search-UX-Audit** — критичні+важливі фікси аудиту секції «Пошук»: крешебезпечний FTS (хибні запити → порожня видача), багатослівне автодоповнення з чесними підписами і історією, гасіння debounce-таймера (попап не випадає поверх сторінок), чесні фасет-лічильники по всіх вимірах без капу виконавців, перероблений did-you-mean (цілий текст, без еха, тільки при 0 хітів), топ-результат лише при text-query та ≥2 результатів, sticky-фасети через viewport-height + власний скрол результатів, sync рядка пошуку зі сторінкою, guard повторного кліку «Пошук», empty-state з діями, SaveQuery: disabled для гостя/фідбек/дедуп/людське ім'я, історія: 1 рядок на запит. Регресія: `MusicApp.BugHunt/SearchCriticalFixTests.cs`; артефакти аудиту: `SearchUxAuditTests.cs`.
- **Wave Player-Reviews (2026-06-12)** — секція «Відгуки» на сторінці альбому плеєра (останній незакритий UI-пункт Player-Redesign §3): агрегат рейтингу/відгуків по всіх виданнях, топ-3 + «Показати всі», owner-gated форма з постом у primary product; регресійні тести `PlayerReviewsTests` + `WishlistUiTests` (закрили всі `[?]`-пункти Wave Wishlist-UI); `ProductEditView`: `Watermark` → `PlaceholderText` (останній build-warning).
- **Wave Search-UX-Audit-2 (косметика)** — browse-режим чесний («Усі альбоми» + «Альбомів у каталозі: N» замість «Результати пошуку»/«Знайдено»), лічильник ховається при 0 (дубль empty-state), рейтинг 0 = «без фільтра» (жодного чипа «★ від 0,0»), чипи відкритих діапазонів «Рік: від 1990»/«Ціна: до 600 ₴» замість «…», пунктуація розділяє слова у FTS (`FtsWords`: «AC/DC» → `ac* dc*`, «Takyon (Death Yon)» знаходиться), новий клас `Button.outline` для «Скинути все»/кнопок empty-state (ghost читався як текст), заголовок «Наявність» прибрано (один чекбокс), іконка «Зберегти запит» на `OnAccentBrush` замість хардкод-Black, Enter у порожньому полі = browse (як вкладка сайдбара, без вайпу відкритих результатів), чекбокс «Сповіщати» прибрано з профілю (механізму сповіщень немає; `NotifyOnNew` лишається у схемі).
