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
- [ ] **LibVLCSharp 3.x** + native libvlc — додати в csproj (`VideoLAN.LibVLC.Windows`, `VideoLAN.LibVLC.Mac`)
- [ ] **Entity Framework Core 8/10 (Sqlite)** — додати `Microsoft.EntityFrameworkCore.Sqlite`
- [ ] **BCrypt.Net-Next 4.x** — для хешування паролів
- [ ] **TagLibSharp 2.x** — для читання метаданих аудіо
- [ ] **ClosedXML 0.102.x** — для експорту замовлень в Excel
- [ ] (опціонально) **ScottPlot.Avalonia** — для стовпчикової діаграми в Адмінці → Статистика

### 1.2. Кросплатформенні нюанси

- [?] Логіка масштабування під Linux/XWayland (`Program.ConfigureLinuxScaling`) — реалізовано добре, поза скоупом специфікації, але не заважає
- [ ] Перевірка/інструкції встановлення libvlc на Linux (`pacman -S vlc` / `apt install libvlc-dev`)
- [ ] Перевірка native libvlc під Windows і macOS

### 1.3. Файлова структура та шляхи

- [ ] Шлях до БД через `Environment.GetFolderPath(SpecialFolder.ApplicationData)` → `MusicStore/store.db`
- [ ] Створення директорії при першому запуску, якщо її немає
- [ ] Структура папок для медіа (`samples/`, `full/`, `covers/`) і її конвенція

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
- [ ] `Models/PlayerSettings.cs` (UserId, Volume, RepeatMode, ShuffleMode, LastTrackId)
- [ ] `Models/SearchHistory.cs`
- [ ] `Models/SavedSearch.cs`
- [ ] Перевірити, чи `Track.Lyrics`, `Track.SamplePath`, `Track.FullPath` реально існують у класі

### 2.2. EF Core DbContext

- [ ] Створити `Data/MusicStoreDbContext.cs` (`DbSet<>` для всіх таблиць)
- [ ] Конфіги через Fluent API (`OnModelCreating`): ключі, зв'язки, індекси
- [ ] Конвертер `ProductFormat` ↔ string / int
- [ ] Міграції: `dotnet ef migrations add Initial`
- [ ] `Database.Migrate()` при старті, якщо БД не існує
- [ ] Seed-метод (перенести `SampleData` у seeder для першого запуску в порожню БД)

### 2.3. FTS5 пошуковий індекс

- [ ] Створити virtual table `SearchIndex` (FTS5, tokenize: `unicode61 remove_diacritics 2`)
- [ ] Тригери `INSERT/UPDATE/DELETE` на `Artists`, `Albums`, `Tracks`, `Reviews` для синхронізації індексу
- [ ] Початкове заповнення індексу з існуючих рядків
- [ ] Утиліти для виконання `MATCH`-запитів (виносити raw SQL з ORM)

---

## 3. Сервісний шар

### 3.1. Авторизація `IAuthService`

- [?] Інтерфейс `IAuthService` — створено
- [?] Реалізація `AuthService` (TryLogin/TryRegister/LoginAsGuest/Logout) — створено
- [!] **Реалізація-заглушка**: будь-які непорожні креди приймаються, `username == "admin"` → роль Admin
- [ ] Замінити stub на справжній логін через БД + BCrypt-перевірку
- [ ] Реєстрація: валідація унікальності username/email, хешування BCrypt
- [ ] Зміна пароля (з підтвердженням старого)
- [ ] Сесія: зберегти `CurrentUser` між запусками (`PlayerSettings` чи окремий файл)

### 3.2. Каталог `ICatalogService`

- [?] Інтерфейс і реалізація `CatalogService` на основі `SampleData`
- [?] `GetProduct`, `GetAlbum`, `GetNewArrivals`, `GetNewArrivalAlbums`, `GetPopular`, `GetReviewsFor` — реалізовано
- [!] `SearchProducts(query)` — **наївний `Contains`**, не використовує FTS5, не парсить операторів
- [ ] Замінити каталог на EF Core (Albums/Products/etc. з БД, а не з SampleData)
- [ ] Додати методи для адмін-операцій: AddProduct, UpdateProduct, DeleteProduct, SetActive
- [ ] Метод повернення «куплених альбомів» для конкретного `UserId` (`JOIN Orders → OrderItems → Products → Albums` де `Status = Completed`)

### 3.3. Кошик `ICartService`

- [?] Інтерфейс і `CartService` — створено
- [?] Add / Remove / UpdateQuantity / Clear / Checkout — реалізовано
- [?] Подія `CartChanged`
- [ ] Персистити кошик у БД (`CartItems` таблиця) — зараз тільки в пам'яті
- [ ] При логіні з гостьового стану — мерджити гостьовий кошик з кошиком користувача
- [ ] Перевірка `Stock` при додаванні / збільшенні кількості (не дозволяти більше, ніж є)

### 3.4. Плеєр `IPlayerService`

- [?] Інтерфейс і `PlayerService` — створено
- [?] Події `MediaOpened`, `MediaEnded`, `PositionChanged`, `PlaybackStateChanged`
- [?] `PlayAlbum`, `PlaySample`, `TogglePlayPause`, `Next`, `Previous`, `Seek`
- [!] **Stub-реалізація на `DispatcherTimer`** — реального відтворення немає
- [ ] Інтегрувати LibVLCSharp: `LibVLC`, `MediaPlayer`, передача шляхів `SamplePath` / `FullPath`
- [ ] Контроль гучності через `MediaPlayer.Volume`
- [ ] Реальний `Seek` через `MediaPlayer.Time`
- [ ] Обмеження 30 секунд для семплів (`PlaySample` — зупиняти на 30с)
- [ ] Перевірка прав на повне відтворення: тільки якщо трек належить до купленого альбому
- [ ] Збереження `PlayerSettings` (Volume, RepeatMode, ShuffleMode, LastTrackId)

### 3.5. Навігація `INavigationService`

- [?] Інтерфейс і реалізація з реєстрацією VM-фабрик — створено
- [?] `NavigateTo(NavTarget, param?)` — створено
- [ ] Стек історії «← Назад» (у специфікації на картці товару є кнопка «← Назад»)

### 3.6. Пошук — окремий сервіс (АІПС-ядро)

- [ ] `ISearchService` з методами `Search(string query)`, `Autocomplete(string prefix)`
- [ ] `SearchQueryParser` (рекурсивний спуск): вільний текст, `поле:значення`, `поле:від..до`, `поле:<X`, `-виключення`, `"точна фраза"`
- [ ] AST → побудова комбінованого `MATCH` + `WHERE` запиту
- [ ] Виконання FTS5 `MATCH` із BM25-ранжуванням
- [ ] Обчислення фінального `score = BM25*0.6 + log(1+sales)*0.2 + (rating/5)*0.1 + recency*0.1`
- [ ] Фасетна навігація: `GROUP BY` лічильники по жанру/року/формату/ціні/рейтингу поверх поточного результату
- [ ] Динамічний перерахунок лічильників при зміні фільтрів
- [ ] Автодоповнення: префіксний `MATCH 'deat*'` + сортування по `popularity_score`
- [ ] Debounce 200мс у UI
- [ ] Нечіткий пошук («Чи мали ви на увазі»): триграмний пошук / Левенштейн при <3 результатах
- [ ] Запис `SearchHistory` після кожного виконаного запиту
- [ ] `SavedSearches`: CRUD + флаг `NotifyOnNew` + перевірка нових товарів

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
- [ ] Колапсація sidebar до іконок 56px при ширині < 1100px
- [ ] Підсвічування активного пункту в sidebar по `CurrentTarget`
- [ ] Бейдж кількості товарів у кошику біля «Кошик»

### 4.2. Mini-player

- [?] `MiniPlayerView` + `MiniPlayerViewModel` створено
- [?] Поява при `MediaOpened` (`IsMiniPlayerVisible = true`)
- [?] Кнопка [✕] → `CloseMiniPlayer`, кнопка [⛶] → `ExpandMiniPlayer` (перехід на повний плеєр)
- [ ] **Плавна анімація** появи/зникнення: `TranslateTransform.Y` 72→0, `Opacity` 0→1, 250мс CubicEaseOut (зараз — миттєво)
- [ ] Мала обкладинка 56×56 (зараз не виводиться обкладинка взагалі)
- [ ] Регулятор гучності в міні-плеєрі (Volume є у VM, треба ще привʼязати UI-слайдер)
- [ ] Перевірити: при наступному `MediaOpened` після ручного закриття — плеєр знов з'являється

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
- [ ] Реальні обкладинки (зараз — градієнт + перша літера; `CoverPath` майже не заповнюється)

### 5.2. Сторінка результатів пошуку (`SearchResultsView` + VM)

- [?] Контейнер з фасетами зліва (260px) + результатами справа
- [?] Список альбомів і треків — створено
- [?] Базові фасети (Жанр, Формат) з лічильниками
- [!] Фасети **не фільтрують результати**, тільки показуються
- [ ] Chip-tabs згори: «Усі», «Альбоми (N)», «Виконавці (N)», «Треки (N)», «Відгуки (N)»
- [ ] Live-update: чекбокс/слайдер → миттєвий перезапит
- [ ] Динамічні лічильники в фасетах
- [ ] Sticky sidebar при скролі
- [ ] Активні фільтри як видаляні chips над результатами
- [ ] Фасет «Рік» (range-слайдер)
- [ ] Фасет «Ціна» (range-слайдер 200—X)
- [ ] Фасет «Рейтинг ★ від N»
- [ ] Чекбокс «Тільки в наявності»
- [ ] Кнопка «Скинути все»
- [ ] Кнопка «💾 Зберегти запит» → запис у `SavedSearches`
- [ ] Колапс сайдбара в `[≡ Фільтри]` при ширині <1100px
- [ ] Топ-результат (перший блок)
- [ ] Кнопка «Чи мали ви на увазі: …» при <3 результатах
- [ ] Автодоповнення під полем пошуку в title bar (Spotify-style випадаюча панель)

### 5.3. Картка товару (`ProductView` + `ProductViewModel`)

- [?] Hero: обкладинка + назва, виконавець, чіпси Жанр/Рік/Формат
- [?] Ціна, залишок, рейтинг
- [?] Кнопки «🛒 Додати в кошик» (працює), «♡ Зберегти» (заглушка)
- [?] Треклист з кнопкою ▶ на кожному треку (запускає семпл)
- [?] Секція «Відгуки» з рендером
- [ ] Кнопка «← Назад» — є в макеті, у view не реалізована
- [ ] **Перемикач формату «⚫ Вініл LP | ○ CD»** — спосіб переключитися між двома Product одного Album
- [ ] Кнопка «Показати всі» відгуки (зараз показуються всі одразу)
- [ ] Форма «Залишити відгук» для авторизованих, які купили цей продукт
- [ ] `SaveToWishlist` команда («Зберегти») — реалізувати з користувачем-таблицею

### 5.4. Кошик (`CartView` + `CartViewModel`)

- [?] Список товарів з мініатюрою, [-] кількість [+], кнопкою «Видалити»
- [?] Підрахунок суми
- [?] Кнопка «Оформити замовлення» → `Checkout`
- [?] `FlashMessage` після оформлення
- [ ] Перевірка авторизації перед `Checkout` (гостям — попередній логін)
- [ ] Підтвердження «видалити товар?» (опціонально)
- [ ] Промокоди / знижки — поза scope, перевірити

### 5.5. Замовлення (`OrdersView` + `OrdersViewModel`)

- [?] `OrdersViewModel` — мінімальний, повертає всі замовлення (поки що не фільтрує по користувачу)
- [?] `OrdersView` — створено
- [ ] Фільтрувати замовлення по `auth.CurrentUser.Id`
- [ ] Кнопка «Деталі» з розкриттям списку OrderItems
- [ ] Колонки: №, дата, статус, сума, кнопка «Деталі»

### 5.6. Профіль (`ProfileView` + `ProfileViewModel`)

- [?] Дані користувача (ім'я, email, роль) — створено
- [?] Список замовлень — створено
- [ ] Підвкладка «Замовлення» — таблиця з фільтрами
- [ ] Підвкладка «Мої відгуки» — список з редагуванням/видаленням
- [ ] Кнопка «Змінити пароль» (модалка з трьома полями)
- [ ] Список **збережених запитів** (з `SavedSearches`) — швидке повторне виконання
- [ ] Індикатор «нові товари по збереженому запиту»

### 5.7. Плеєр (`PlayerView` + `PlayerViewModel`)

- [?] Каркас VM з біндингами (TrackTitle, ArtistName, Progress, Volume…)
- [?] Список «куплених альбомів» — **зараз бере перші 2 з каталогу** (заглушка)
- [ ] Реальний фільтр куплених альбомів через `Orders → OrderItems → Products → Albums` де `UserId == current` і `Status == Completed`
- [ ] Drag-slider прогресу (Seek)
- [ ] Кнопка «+ Додати файли» (`OpenFileDialog`) для своїх локальних треків
- [ ] Reuse `Themes/DarkTheme.axaml` з нинішнього плеєра (зараз — Themes/Colors.axaml + ControlStyles.axaml)
- [ ] Drag&drop файлів у плейлист
- [ ] Перенесення плейлистів з M3U в БД (`Playlists`, `PlaylistTracks`)

### 5.8. Адмінка (`AdminView` + `AdminViewModel`)

- [?] KPI-картки (Товарів / Замовлень / Виручка)
- [?] TabControl з 4 вкладками: «Товари», «Замовлення», «Статистика», «Користувачі»
- [?] Вкладка «Товари»: список з кнопками ✎ / 🗑 (без обробників)
- [?] Вкладка «Замовлення»: список + ComboBox статусу (не зберігає)
- [?] Вкладка «Статистика»: Топ-10 за продажами
- [?] Вкладка «Користувачі»: заглушка з текстом
- [ ] Кнопка «+ Додати товар» → форма редагування (Album/Artist/Genre, поля Product, OpenFileDialog для обкладинки і двох аудіо)
- [ ] Кнопка ✎ → форма редагування
- [ ] Кнопка 🗑 → soft-delete (IsActive = false) або hard з підтвердженням
- [ ] CRUD для виконавців, альбомів, жанрів
- [ ] **Експорт замовлень в Excel** (ClosedXML) — зараз кнопки є, обробників нема
- [ ] Експорт товарів у CSV — кнопка є
- [ ] Зміна `OrderStatus` через ComboBox — пов'язати з командою + збереженням
- [ ] Кнопка «Деталі» замовлення → модальне вікно зі списком OrderItems
- [ ] Статистика: блок «Виручка за період» з вибором дат і підрахунком
- [ ] (опціонально) Стовпчикова діаграма продажів (ScottPlot.Avalonia)
- [ ] Вкладка «Користувачі»: справжня таблиця, кнопка зміни ролі
- [ ] Підвантаження ролі: тільки `Admin` бачить вкладку (вже працює через `IsAdmin`)

### 5.9. Авторизація (`LoginWindow` + `LoginViewModel`)

- [?] `LoginViewModel` (Username, Password, Email, IsRegistering, Error, Guest)
- [?] `LoginWindow.axaml` — створено
- [!] Вікно **не використовується** — у `App.OnFrameworkInitializationCompleted` одразу `LoginAsGuest()` і відкривається MainWindow
- [ ] Запускати `LoginWindow` як стартове, або як модалку перед MainWindow
- [ ] Кнопка «Увійти» у title bar (поряд з UserDisplayName) → відкривати `LoginWindow`
- [ ] Реєстрація з валідацією email/паролю
- [ ] Прибрати dev-quirk «admin → автоматично Admin» коли під'єднана БД
- [ ] «Продовжити як гість» — працює, але має пройти повний flow

---

## 6. Дизайн і тема

- [?] Темна тема (`Themes/Colors.axaml` + `Themes/ControlStyles.axaml`) — створено власні
- [?] Скругленість 8px (`RadiusS/M/L`)
- [?] Український UI-текст
- [ ] **Reuse `Themes/DarkTheme.axaml`** з нинішнього плеєра, як вимагає специфікація (поки що — нові стилі, не зрозуміло чи це той самий ресурс)
- [ ] Акцентний колір — теплий помаранчевий (#E07B39) АБО вишневий (#A02C3F) — обрати один
- [ ] Шрифт Inter (підключено `Avalonia.Fonts.Inter`) — переконатися, що застосовується глобально
- [ ] Іконки — замінити emoji (`🏠`, `🛒`, `📦`, `👤`, `🎧`, `⚙`, `🔍`, `▶`) на Font Awesome / Lucide / Material через `Projektanker.Icons.Avalonia`
- [ ] Культура `uk-UA` (формат дат, чисел) — встановити глобально

---

## 7. Конвертери та допоміжне

- [?] `Converters/AlbumIdToGradientBrushConverter.cs`
- [?] `Converters/CoverPathToImageConverter.cs`
- [?] `Converters/FirstCharConverter.cs`
- [ ] Конвертер `OrderStatus` → українська локалізована назва («Новий», «В обробці», «Виконано», «Скасовано»)
- [ ] Конвертер `bool → Visibility` (якщо понадобиться)

---

## 8. Дані та seed

- [?] `Services/SampleData.cs` з 8 виконавцями, 8 альбомами, 16 продуктами, відгуками, замовленнями
- [ ] Замінити SampleData на seed для EF Core (виконується тільки якщо БД порожня)
- [ ] Реальні обкладинки альбомів у `Assets/covers/`
- [ ] Реальні семпли (30с) у `Assets/samples/` або в окремій data-папці
- [ ] Демо-користувачі (Admin + Customer) з BCrypt-паролями

---

## 9. Тестування і документація

- [ ] Юніт-тести парсера запитів (синтаксис: поля, діапазони, виключення, фрази)
- [ ] Юніт-тести `CartService` (мердж, перевірка stock)
- [ ] Юніт-тести `AuthService` (BCrypt, унікальність)
- [ ] Юніт-тести фасетної навігації (правильні лічильники)
- [ ] README з інструкцією запуску (Windows / Linux / macOS), включно з libvlc
- [ ] Документація API сервісів
- [ ] Пояснення моделі ранжування для захисту диплому (як гіперпараметри)

---

## 10. Свідомо НЕ робимо (з специфікації)

- [-] Власна система оплати (статус міняє адмін вручну)
- [-] Рекомендаційний алгоритм («подібні альбоми»)
- [-] Мобільна версія
- [-] Повний MusicBrainz lookup
- [-] Багатокористувацький онлайн (БД локальна)

---

## Зведена статистика

| Категорія | Зроблено [?] | В процесі [~] | Конфлікт [!] | Не зроблено [ ] |
|---|---|---|---|---|
| Інфраструктура / стек | 4 | 0 | 0 | ~10 |
| Модель даних | 12 | 0 | 0 | ~7 |
| Сервіси | 9 | 0 | 3 | ~25 |
| UI / екрани | 35 | 0 | 2 | ~50 |
| Дизайн і тема | 3 | 0 | 0 | 5 |
| Конвертери / seed / тести | 4 | 0 | 0 | ~10 |

**Загалом**: каркас MVVM-прототипу зверстаний і дані на in-memory `SampleData` ходять, але вся технічна основа специфікації (SQLite + EF Core, FTS5 пошук, BCrypt-авторизація, libVLC-відтворення, ClosedXML-експорт, TagLibSharp-метадані) — попереду.
