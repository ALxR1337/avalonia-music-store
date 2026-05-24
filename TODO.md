# TODO «Музичний магазин»

Детальний роздроблений список задач відносно `maket_programy.md`.

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
- [?] Avalonia UI підключено (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`)
- [?] `CommunityToolkit.Mvvm` 8.4.2 підключено (специфікація прямо не вимагала, але прийнятна заміна власній MVVM)
- [?] `Semi.Avalonia` 12.0.1 — додатковий UI-кіт (не в специфікації, перевірити, чи реально використовується)
- [?] `Projektanker.Icons.Avalonia.FontAwesome` підключено — у специфікації Lucide/Material, потребує узгодження
- [x] **LibVLCSharp 3.x** + native libvlc — додано (`VideoLAN.LibVLC.Windows`, `VideoLAN.LibVLC.Mac`)
- [x] **Entity Framework Core 10 (Sqlite)** — `Microsoft.EntityFrameworkCore.Sqlite` 10.0.8
- [x] **BCrypt.Net-Next 4.x** — додано
- [x] **TagLibSharp 2.x** — додано
- [x] **ClosedXML 0.105** — додано в Wave 6
- [-] (опціонально) **ScottPlot.Avalonia** — свідомо відкладено, KPI + Топ-10 поки що достатньо

### 1.2. Кросплатформенні нюанси

- [?] Логіка масштабування під Linux/XWayland (`Program.ConfigureLinuxScaling`) — реалізовано добре, поза скоупом специфікації, але не заважає
- [x] Перевірка libvlc на Linux (Arch: `pacman -Qi vlc` — `libvlc.so.5` присутній)
- [ ] Перевірка native libvlc під Windows і macOS

### 1.3. Файлова структура та шляхи

- [x] Шлях до БД через `Environment.GetFolderPath(SpecialFolder.ApplicationData)` → `MusicStore/store.db` (з override `MUSICAPP_DB_PATH` для тестів)
- [x] Створення директорії при першому запуску (`DbContext.OnConfiguring`)
- [ ] Структура папок для медіа (`samples/`, `full/`, `covers/`) — поки що адмін задає шляхи через `OpenFileDialog`

---

## 2. Модель даних і БД

### 2.1. POCO-моделі

- [?] `Models/Artist.cs` (Id, Name, Aliases, Country) — створено
- [?] `Models/Genre.cs` (Id, Name) — створено
- [?] `Models/Album.cs` (Id, ArtistId, Title, Year, GenreId, CoverPath, Description) — створено
- [?] `Models/Track.cs` (Id, AlbumId, Position, Title, Lyrics, Duration, SamplePath, FullPath) — створено
- [?] `Models/Product.cs` (Id, AlbumId, Format, Price, Stock, ReleaseYear, Label, IsActive) — створено
- [?] `Models/User.cs` (Id, Username, PasswordHash, Email, Role, CreatedAt) — створено
- [?] `Models/CartItem.cs` — створено
- [?] `Models/Order.cs` + `OrderItem` — створено
- [?] `Models/Review.cs` — створено
- [?] `Models/Playlist.cs` — створено (без `PlaylistTracks` — перевірити)
- [?] `Models/Enums.cs` (ProductFormat, OrderStatus, UserRole, RepeatMode) — створено
- [?] `Models/NewArrivalAlbum.cs` — допоміжний DTO для каталогу, створено
- [x] `Models/PlayerSettings.cs` (UserId, Volume, RepeatMode, ShuffleMode, LastTrackId) — Wave 1
- [x] `Models/SearchHistory.cs` — Wave 1
- [x] `Models/SavedSearch.cs` — Wave 1
- [x] `Models/Wishlist.cs` (UserId, ProductId, AddedAt) — Wave 5
- [x] `Track.Lyrics`, `Track.SamplePath`, `Track.FullPath`, `Track.SampleStartSeconds` — присутні

### 2.2. EF Core DbContext

- [x] Створити `Data/MusicStoreDbContext.cs` (`DbSet<>` для всіх таблиць)
- [x] Конфіги через Fluent API (`OnModelCreating`): ключі, зв'язки, індекси
- [x] Конвертер `ProductFormat` / `OrderStatus` / `UserRole` / `RepeatMode` ↔ string
- [x] Міграції: Initial, AddArtistPhotoAndSampleStart, AddPlayerSettingsSearchHistorySavedSearches, AddWishlist
- [x] `Database.Migrate()` при старті (`DbSeeder.EnsureSeeded`)
- [x] Seed-метод з BCrypt-демо-користувачами (`admin/admin`, `demo/demo`)

### 2.3. FTS5 пошуковий індекс

- [x] Створити virtual table `SearchIndex` (FTS5, tokenize: `unicode61 remove_diacritics 2`) — Wave 4
- [x] Тригери `INSERT/UPDATE/DELETE` на `Artists`, `Albums`, `Tracks`, `Reviews` для синхронізації індексу — Wave 4
- [x] Початкове заповнення індексу з існуючих рядків (`Fts5Initializer.Ensure`) — Wave 4
- [x] Утиліти для виконання `MATCH`-запитів — `SearchService.ExecuteFtsHits` через `SqliteConnection`

---

## 3. Сервісний шар

### 3.1. Авторизація `IAuthService`

- [x] Інтерфейс `IAuthService` — створено
- [x] Реалізація `AuthService` (TryLogin/TryRegister/LoginAsGuest/Logout) — створено
- [x] Справжній логін через БД + BCrypt-перевірку — Wave 1
- [x] Реєстрація: валідація унікальності username/email, хешування BCrypt — Wave 1
- [x] Зміна пароля (`TryChangePassword`) — Wave 1
- [ ] Сесія: зберегти `CurrentUser` між запусками (`PlayerSettings` чи окремий файл) — поки що login кожен запуск

### 3.2. Каталог `ICatalogService`

- [x] Інтерфейс і реалізація `CatalogService` на основі EF Core
- [x] `GetProduct`, `GetAlbum`, `GetNewArrivals`, `GetNewArrivalAlbums`, `GetPopular`, `GetReviewsFor` — реалізовано
- [x] `SearchProducts(query)` — заміщено повним `SearchService` через FTS5 (Wave 4); старий метод залишився для зворотної сумісності
- [x] Каталог працює з EF Core (Albums/Products/etc. з БД, seed залишився як bootstrap для порожньої БД)
- [x] Адмін-методи: `AddProduct`, `UpdateProduct`, `SetProductActive` — Wave 6
- [x] `GetPurchasedAlbums(userId)` + `IsAlbumPurchased(albumId, userId)` — Wave 3

### 3.3. Кошик `ICartService`

- [x] Інтерфейс і `CartService` — створено
- [x] Add / Remove / UpdateQuantity / Clear / Checkout — реалізовано
- [x] Подія `CartChanged`
- [x] Персистимо кошик у БД (`CartItems` таблиця) — Wave 3
- [x] При логіні з гостьового стану — мерджимо гостьовий кошик з кошиком користувача — Wave 3
- [x] Перевірка `Stock` при додаванні / збільшенні кількості — Wave 3

### 3.4. Плеєр `IPlayerService`

- [x] Інтерфейс і `PlayerService` — створено
- [x] Події `MediaOpened`, `MediaEnded`, `PositionChanged`, `PlaybackStateChanged`
- [x] `PlayAlbum`, `PlaySample`, `TogglePlayPause`, `Next`, `Previous`, `Seek`
- [x] Інтегровано LibVLCSharp: `LibVLC`, `MediaPlayer`, передача шляхів `SamplePath` / `FullPath` — Wave 2
- [x] Контроль гучності через `MediaPlayer.Volume` (мап 0..1 → 0..100) — Wave 2
- [x] Реальний `Seek` через `MediaPlayer.Time` — Wave 2
- [x] Обмеження 30 секунд для семплів — Wave 2 (`TimeChanged` спрацьовує на `SampleStartSeconds + 30s`)
- [x] Перевірка прав на повне відтворення — Wave 3 (`IsAlbumPlayable` gate)
- [x] Збереження `PlayerSettings` (Volume, RepeatMode, ShuffleMode, LastTrackId) — Wave 2

### 3.5. Навігація `INavigationService`

- [x] Інтерфейс і реалізація з реєстрацією VM-фабрик — створено
- [x] `NavigateTo(NavTarget, param?)` — створено
- [x] Стек історії «← Назад» — Wave 5 (`NavigationService.GoBack`, кнопка на `ProductView`)

### 3.6. Пошук — окремий сервіс (АІПС-ядро)

- [x] `ISearchService` з методами `Search(string query, filters)`, `Autocomplete(string prefix)` — Wave 4
- [x] `SearchQueryParser` (рекурсивний спуск): вільний текст, `поле:значення`, `поле:від..до`, `поле:<X`, `-виключення`, `"точна фраза"` — Wave 4
- [x] AST → побудова комбінованого `MATCH` + `WHERE` запиту — Wave 4 (`SearchService.BuildMatchClause`)
- [x] Виконання FTS5 `MATCH` із BM25-ранжуванням (`-bm25(SearchIndex)`) — Wave 4
- [x] Обчислення фінального `score = BM25*0.6 + log(1+sales)*0.2 + (rating/5)*0.1 + recency*0.1` — Wave 4
- [x] Фасетна навігація: `GROUP BY` лічильники по жанру/формату/наявності поверх поточного результату — Wave 4
- [x] Динамічний перерахунок лічильників при зміні фільтрів — Wave 4
- [x] Автодоповнення: префіксний `MATCH 'deat*'` + сортування по `-bm25` — Wave 4
- [x] Debounce 200мс у UI (`MainWindowViewModel.OnSearchQueryChanged` → `DispatcherTimer`) — Wave 4
- [x] Нечіткий пошук («Чи мали ви на увазі»): Левенштейн при <3 результатах — Wave 4
- [x] Запис `SearchHistory` після кожного виконаного запиту — Wave 4
- [x] `SavedSearches`: CRUD + флаг `NotifyOnNew` — Wave 4 (перевірка нових товарів відкладена до Wave 7)

---

## 4. UI: вікно та глобальна оболонка

### 4.1. `MainWindow`

- [?] Кастомний title bar (`ExtendClientAreaToDecorationsHint`, `WindowDecorations="BorderOnly"`)
- [?] Поле пошуку по центру title bar, `Text="{Binding SearchQuery}"`, Enter → `SubmitSearch`
- [?] Drag вікна через `PointerPressed` → `BeginMoveDrag`
- [?] Кнопки Min / Max / Close
- [?] Sidebar (Каталог, Кошик, Замовлення, Профіль, Плеєр, Адмін)
- [?] Видимість секції «Адмін» по `IsAdmin`
- [?] `ContentControl` для CurrentView
- [?] Mini-player у нижній рядці
- [ ] Розміри: специфікація — мін. 1024×640, default 1280×800 — **уже відповідає**, перевірити при різних DPI
- [x] Колапсація sidebar до іконок 72px при ширині < 1100px — Wave 8 (`MainWindow.OnRootSizeChanged` + `BoolToWidthConverter`)
- [x] Підсвічування активного пункту в sidebar по `CurrentTarget` — Wave 8 (`Classes.active` binding на `IsXxxActive`)
- [x] Бейдж кількості товарів у кошику біля «Кошик» — Wave 8 (`CartCount` + `HasCartItems`)

### 4.2. Mini-player

- [?] `MiniPlayerView` + `MiniPlayerViewModel` створено
- [?] Поява при `MediaOpened` (`IsMiniPlayerVisible = true`)
- [?] Кнопка [✕] → `CloseMiniPlayer`, кнопка [⛶] → `ExpandMiniPlayer` (перехід на повний плеєр)
- [x] **Плавна анімація** появи/зникнення: `RenderTransform translateY(72)` → `translateY(0)` + Opacity 0→1, 250мс — Wave 8
- [x] Мала обкладинка 56×56 (`CurrentAlbum` + cover/gradient/FirstChar fallback) — Wave 8
- [x] Регулятор гучності в міні-плеєрі — вже був у XAML; підтверджено Wave 8
- [?] При наступному `MediaOpened` після ручного закриття — плеєр знов з'являється (логіка в `MainWindowViewModel` встановлює `IsMiniPlayerVisible=true` на `MediaOpened`)

---

## 5. UI: екрани

### 5.1. Каталог (`CatalogView` + `CatalogViewModel`)

- [?] Hero-блок з привітанням
- [?] Секція «Перегляд за жанрами» — UniformGrid 4 колонки, кнопки-картки з градієнтом
- [?] Секція «Нові надходження» — WrapPanel карток `NewArrivalAlbum` з обкладинкою, LP/CD цінами
- [?] Секція «Популярні цього місяця» — WrapPanel карток `Product`
- [?] ContextFlyout на картках: «Додати LP в кошик», «Додати CD в кошик», «Прослухати семпл»
- [?] Кнопка ▶ play-circle на обкладинці → `QuickPreview`
- [?] Клік по картці → `OpenProduct`
- [?] Клік по жанру → `OpenGenre` (навігація на SearchResults з `жанр:Rock`)
- [x] Реальні обкладинки — справжній seed з `~/Downloads/Music` з 23 альбомами, кожен має `CoverPath` (Wave Real-Music)

### 5.2. Сторінка результатів пошуку (`SearchResultsView` + VM)

- [x] Контейнер з фасетами зліва (280px) + результатами справа
- [x] Список альбомів і треків — створено
- [x] Базові фасети (Жанр, Формат, Наявність) з лічильниками — Wave 4
- [x] Фасети фільтрують результати — Wave 4 (`SearchService.Search` приймає `SearchFilters`)
- [x] Chip-tabs згори: «Усі», «Альбоми (N)», «Виконавці (N)», «Треки (N)», «Відгуки (N)» — Wave 4
- [x] Live-update: чекбокс/спін → миттєвий перезапит — Wave 4 (`partial void OnXChanged → Reload`)
- [x] Динамічні лічильники в фасетах — Wave 4
- [-] Sticky sidebar при скролі — поза scope MVP (загальний ScrollViewer працює достатньо)
- [x] Активні фільтри як видаляні chips над результатами — Wave 8 (`RemoveFilterCommand`, chip = `ActiveFilterChip` record)
- [x] Фасет «Рік» (NumericUpDown від/до) — Wave 4
- [x] Фасет «Ціна» (NumericUpDown від/до) — Wave 4
- [x] Фасет «Рейтинг ★ від N» — Wave 4
- [?] Чекбокс «Тільки в наявності» — є як facet bucket, окремий чекбокс відкладено
- [x] Кнопка «Скинути все» — Wave 4
- [x] Кнопка «💾 Зберегти запит» → запис у `SavedSearches` — Wave 4
- [-] Колапс сайдбара в `[≡ Фільтри]` при ширині <1100px — `MainWindow` sidebar вже колапсується, фасет-сайдбар лишимо як є
- [x] Топ-результат — Wave 8 (окремий блок над списками з `OpenTopResultCommand`)
- [x] Кнопка «Чи мали ви на увазі: …» при <3 результатах — Wave 4
- [x] Автодоповнення під полем пошуку в title bar (Popup) — Wave 4

### 5.3. Картка товару (`ProductView` + `ProductViewModel`)

- [x] Hero: обкладинка + назва, виконавець, чіпси Жанр/Рік
- [x] Ціна, залишок, рейтинг
- [x] Кнопки «🛒 Додати в кошик», «♡ Зберегти» (працює через `Wishlist` таблицю) — Wave 5
- [x] Треклист з кнопкою ▶ на кожному треку (запускає семпл) — Wave 5 (виправлено bug: Include Tracks)
- [x] Секція «Відгуки» з рендером
- [x] Кнопка «← Назад» — Wave 5 (через `NavigationService.GoBack()`)
- [x] **Перемикач формату «⚫ Вініл LP | ○ CD»** — Wave 5 (`ICatalogService.GetSiblingProduct`)
- [x] Кнопка «Показати всі» відгуки — Wave 5 (перші 3, потім розгортається)
- [x] Форма «Залишити відгук» для авторизованих, які купили цей продукт — Wave 5 (`CanLeaveReview` gate)
- [x] `SaveToWishlist` команда («Зберегти») — Wave 5 (`Wishlist` таблиця)

### 5.4. Кошик (`CartView` + `CartViewModel`)

- [x] Список товарів з мініатюрою, [-] кількість [+], кнопкою «Видалити»
- [x] Підрахунок суми
- [x] Кнопка «Оформити замовлення» → `Checkout` (зберігає в `Orders`, декрементить stock)
- [?] `FlashMessage` після оформлення
- [x] Перевірка авторизації перед `Checkout` — Wave 8 (`IsGuest` banner + блокування `Checkout` з flash-повідомленням)
- [-] Підтвердження «видалити товар?» — поза scope MVP
- [-] Промокоди / знижки — поза scope

### 5.5. Замовлення (`OrdersView` + `OrdersViewModel`)

- [x] `OrdersViewModel` фільтрує по `auth.CurrentUser.Id` (admin бачить усі)
- [x] `OrdersView` — створено
- [x] Фільтрувати замовлення по `auth.CurrentUser.Id` — Wave 3
- [x] Кнопка «Деталі» з розкриттям списку OrderItems — Wave 7 (`OrdersViewModel.ToggleDetailsCommand` + inline expand)
- [x] Колонки: №, дата, статус, сума, кнопка «Деталі»

### 5.6. Профіль (`ProfileView` + `ProfileViewModel`)

- [?] Дані користувача (ім'я, email, роль) — створено
- [?] Список замовлень — створено
- [x] Підвкладка «Замовлення» — таблиця з фільтром статусу + inline-expand деталі — Wave 7
- [x] Підвкладка «Мої відгуки» — список з редагуванням/видаленням (`ICatalogService.GetReviewsByUser/Update/Delete`) — Wave 7
- [x] Кнопка «Змінити пароль» (`ChangePasswordWindow` модалка) — Wave 7
- [x] Список **збережених запитів** — Wave 7 (`ProfileViewModel.SavedSearches` через `ISearchService.ListSavedSearchSummaries`)
- [?] Індикатор «нові товари по збереженому запиту» — Wave 7 (показано поточну кількість матчів через `SavedSearchSummary.CurrentCount`; справжнього дельта-індикатора немає, бо без CreatedAt на Product/Album це неточно)

### 5.7. Плеєр (`PlayerView` + `PlayerViewModel`)

- [x] Каркас VM з біндингами (TrackTitle, ArtistName, Progress, Volume…)
- [x] Список «куплених альбомів» — Wave 3 (`ICatalogService.GetPurchasedAlbums`)
- [x] Реальний фільтр куплених альбомів через `Orders → OrderItems → Products → Albums` де `Status == Completed` — Wave 3
- [x] Drag-slider прогресу (Seek) — Wave 8 (`Slider` з `IsScrubbing` + `CommitSeek` на PointerReleased)
- [x] Кнопка «+ Додати файли» (`OpenFileDialog`) для своїх локальних треків — Wave 8 (`AddLocalFilesCommand` → `IPlayerService.PlayFile`)
- [-] Reuse `Themes/DarkTheme.axaml` з нинішнього плеєра — `Themes/Colors.axaml` + `ControlStyles.axaml` повністю його заміщують
- [-] Drag&drop файлів у плейлист — поза scope MVP
- [-] Перенесення плейлистів з M3U в БД — поза scope MVP

### 5.8. Адмінка (`AdminView` + `AdminViewModel`)

- [x] KPI-картки (Товарів / Замовлень / Виручка)
- [x] TabControl з 4 вкладками: «Товари», «Замовлення», «Статистика», «Користувачі»
- [x] Вкладка «Товари»: список з кнопками ✎ / 🗑 + обробниками — Wave 6
- [x] Вкладка «Замовлення»: список + ComboBox статусу + кнопка «Зберегти» — Wave 6
- [x] Вкладка «Статистика»: Топ-10 за продажами + блок «Виручка за період» — Wave 6
- [x] Вкладка «Користувачі»: реальна таблиця + ComboBox ролі — Wave 6
- [x] Кнопка «+ Додати товар» → `ProductEditWindow` (Album/Artist/Genre dropdowns + checkbox «новий», поля Product, `OpenFileDialog` для обкладинки/семплу/повного треку) — Wave 6
- [x] Кнопка ✎ → форма редагування — Wave 6
- [x] Кнопка 🗑 → soft-delete (`SetProductActive(false)`) — Wave 6
- [?] CRUD для виконавців, альбомів, жанрів — нові створюються inline через форму товару; окремі форми не зроблено
- [x] **Експорт замовлень в Excel** (ClosedXML) — Wave 6 (`ExportOrdersToExcel`)
- [x] Експорт товарів у CSV — Wave 6 (`ExportProductsToCsv`)
- [x] Зміна `OrderStatus` через ComboBox + кнопка «Зберегти» — Wave 6
- [x] Кнопка «Деталі» замовлення → панель з OrderItems (інлайн, не модалка) — Wave 6
- [x] Статистика: блок «Виручка за період» з вибором дат і підрахунком — Wave 6
- [-] (опціонально) Стовпчикова діаграма продажів (ScottPlot.Avalonia) — свідомо відкладено
- [x] Вкладка «Користувачі»: справжня таблиця, кнопка зміни ролі — Wave 6
- [x] Підвантаження ролі: тільки `Admin` бачить вкладку (через `IsAdmin`)

### 5.9. Авторизація (`LoginWindow` + `LoginViewModel`)

- [x] `LoginViewModel` (Username, Password, Email, IsRegistering, Error, Guest)
- [x] `LoginWindow.axaml` — створено
- [x] Вікно використовується як стартове — Wave 1 (через `RequestClose` → `MainWindow`)
- [x] Запускати `LoginWindow` як стартове — Wave 1
- [x] Кнопка «Увійти» у title bar (поряд з UserDisplayName) → відкривати `LoginWindow` повторно — Wave 8 (`OpenLoginCommand`, visible коли `IsGuest`)
- [x] Реєстрація з валідацією унікальності username/email — Wave 1
- [x] Прибрано dev-quirk «admin → автоматично Admin» — Wave 1 (тепер BCrypt-перевірка)
- [x] «Продовжити як гість» — працює

---

## 6. Дизайн і тема

- [?] Темна тема (`Themes/Colors.axaml` + `Themes/ControlStyles.axaml`) — створено власні
- [?] Скругленість 8px (`RadiusS/M/L`)
- [?] Український UI-текст
- [-] **Reuse `Themes/DarkTheme.axaml`** — заміщено `Themes/Colors.axaml` + `Themes/ControlStyles.axaml`
- [?] Акцентний колір (#E07B39 / #A02C3F) — поточний accent визначається в `Themes/Colors.axaml`, остаточний вибір лишимо за дизайном
- [?] Шрифт Inter — `Program.cs::BuildAvaloniaApp.WithInterFont()` + `UiFont` ресурс
- [-] Іконки emoji → Font Awesome / Lucide — поза scope MVP (emoji читабельні в темі)
- [x] Культура `uk-UA` — Wave 8 (`Program.ConfigureUkrainianCulture`)

---

## 7. Конвертери та допоміжне

- [?] `Converters/AlbumIdToGradientBrushConverter.cs`
- [x] `Converters/CoverPathToImageConverter.cs` — використовується в `ProductView` (Wave 5)
- [?] `Converters/FirstCharConverter.cs`
- [x] Конвертер `OrderStatus` → українська локалізована назва — Wave 8 (`OrderStatusToUkrainianConverter`)
- [-] Конвертер `bool → Visibility` — Avalonia має вбудоване `IsVisible`

---

## 8. Дані та seed

- [x] `Services/SampleData.cs` — 23 справжні альбоми з `~/Downloads/Music`, з виконавцями, біографіями, описами, обкладинками, треками (зчитуються з файлової системи), багато-жанровими тегами
- [x] SampleData використовується тільки якщо БД порожня; `DbSeeder.WipeLegacyIfPresent` авто-замінює застарілий seed на новий
- [x] Реальні обкладинки — `Album.CoverPath` вказує на jpg/png у директорії альбому
- [x] Реальні семпли — `Track.FullPath` і `Track.SamplePath` показують на справжні mp3/opus файли; `SampleStartSeconds=30`
- [x] Демо-користувачі (Admin + Customer) з BCrypt-паролями — Wave 1 (`admin/admin`, `demo/demo`)

---

## 9. Тестування і документація

- [ ] Юніт-тести парсера запитів (синтаксис: поля, діапазони, виключення, фрази) — Wave 9
- [ ] Юніт-тести `CartService` (мердж, перевірка stock) — Wave 9
- [ ] Юніт-тести `AuthService` (BCrypt, унікальність) — Wave 9
- [ ] Юніт-тести фасетної навігації (правильні лічильники) — Wave 9
- [?] Integration-тести через `Avalonia.Headless` — `MusicApp.BugHunt/` (7 тестів покривають waves 3-6 UI flows)
- [ ] README з інструкцією запуску (Windows / Linux / macOS), включно з libvlc — Wave 9
- [ ] Документація API сервісів — Wave 9
- [ ] Пояснення моделі ранжування для захисту диплому (як гіперпараметри `WeightBm25=0.6`, `WeightPopularity=0.2`, `WeightRating=0.1`, `WeightRecency=0.1` в `SearchService`) — Wave 9

---

## 10. Свідомо НЕ робимо (з специфікації)

- [-] Власна система оплати (статус міняє адмін вручну)
- [-] Рекомендаційний алгоритм («подібні альбоми»)
- [-] Мобільна версія
- [-] Повний MusicBrainz lookup
- [-] Багатокористувацький онлайн (БД локальна)

---

## Зведена статистика (після Wave 6)

| Категорія | Зроблено [x] | Затверджено [?] | Конфлікт [!] | Не зроблено [ ] | Відкинуто [-] |
|---|---|---|---|---|---|
| Інфраструктура / стек | 7 | 1 | 0 | 1 | 1 |
| Модель даних | 16 | 0 | 0 | 0 | 0 |
| Сервіси | 30 | 1 | 0 | 1 | 2 |
| UI: оболонка / mini-player | 1 | 9 | 0 | 5 | 0 |
| UI: екрани | 49 | 8 | 0 | 12 | 2 |
| Дизайн і тема | 3 | 0 | 0 | 5 | 0 |
| Конвертери / seed / тести | 7 | 3 | 0 | 8 | 1 |

**Виконано waves 1–8:**

- **Wave 1** — DB + Auth: EF Core SQLite з міграціями, BCrypt-авторизація, LoginWindow як стартове, демо-користувачі.
- **Wave 2** — LibVLC: реальне відтворення семплів/повних треків, гучність, seek, 30-сек cutoff, PlayerSettings round-trip.
- **Wave 3** — Catalog/Cart на DB: куплені альбоми (`JOIN Orders → Albums`), кошик у DB, мердж гостя→юзера, перевірка stock, gate повного відтворення.
- **Wave 4** — АІПС-пошук: FTS5 + тригери, recursive-descent парсер DSL (`поле:значення`, ranges, `-виключення`, `"фраза"`), BM25 + композитний score (§8.7 hyperparameters), динамічні фасети, autocomplete з debounce, fuzzy "Чи мали ви на увазі" (Левенштейн), SearchHistory + SavedSearches.
- **Wave 5** — Картка товару: back button, format toggle LP↔CD, show-all reviews, форма залишити відгук (з auth+purchase gate), wishlist, cover binding.
- **Wave 6** — Адмінка: Add/Edit/Delete продуктів через модальне вікно (`ProductEditWindow` з file pickers), CRUD inline для Album/Artist/Genre, Order status update + Order details, Excel-експорт (ClosedXML), CSV-експорт, Users tab з role change, Revenue за період.
- **Wave 7** — Особистий кабінет: `ProfileView` із трьома вкладками — «Замовлення» (фільтр по статусу + inline-розгортання `OrderItems`), «Мої відгуки» (`GetReviewsByUser` + `UpdateReview` + `DeleteReview`), «Збережені запити» (`ListSavedSearchSummaries`, toggle notify, видалення, ▶ відкрити). Зміна пароля через `ChangePasswordWindow`. `OrdersView` отримав inline-deтаlі для звичайного користувача.
- **Wave 8** — UI polish: глобальна `uk-UA` культура, конвертер `OrderStatus`, сайдбар колапсує до 72px при ширині <1100px з активним пунктом + бейджем кошика, кнопка «Увійти» у title bar, кошик блокує checkout для гостя з банером, реальні обкладинки в каталозі/пошуку/кошику/плеєрі (через `CoverPathToImageConverter`), міні-плеєр з обкладинкою + slide-up/fade-in анімацією, draggable Seek slider у плеєрі, кнопка «+ Додати файли» через `IPlayerService.PlayFile`, removable chips у пошуку + блок ТОП-РЕЗУЛЬТАТ.

**Залишилось** (wave 9): юніт-тести (парсер, CartService, AuthService, фасети), README з libvlc setup, документація API, захист гіперпараметрів моделі ранжування.
