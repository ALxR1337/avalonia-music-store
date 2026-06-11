# TODO «Музичний магазин»

Детальний роздроблений список задач відносно `maket_programy.md`.
Звірено з кодом (повний аудит робочого дерева) — 2026-06-10.

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

### 3.3. Кошик `ICartService`

- [x] Інтерфейс і `CartService`; Add / Remove / UpdateQuantity / Clear / Checkout; подія `CartChanged`
- [x] Персистимо кошик у БД (`CartItems` таблиця) — Wave 3
- [x] При логіні з гостьового стану — мерджимо гостьовий кошик з кошиком користувача — Wave 3
- [x] Перевірка `Stock` при додаванні / збільшенні кількості — Wave 3

### 3.4. Плеєр `IPlayerService`

- [x] Інтерфейс і `PlayerService`; події `MediaOpened`, `MediaEnded`, `PositionChanged`, `PlaybackStateChanged`, `ShuffleModeChanged`, `RepeatModeChanged`
- [x] `PlayAlbum`, `PlaySample`, `PlayFile`, `TogglePlayPause`, `Next`, `Previous`, `Seek`, `Stop`
- [x] Інтегровано LibVLCSharp: `LibVLC`, `MediaPlayer`, передача шляхів `SamplePath` / `FullPath` — Wave 2
- [x] Контроль гучності через `MediaPlayer.Volume` (мап 0..1 → 0..100) — Wave 2
- [x] Реальний `Seek` через `MediaPlayer.Time` — Wave 2
- [x] Обмеження 30 секунд для семплів від `SampleStartSeconds` — Wave 2
- [x] Перевірка прав на повне відтворення — Wave 3 (`IsAlbumPlayable` gate)
- [x] Збереження `PlayerSettings` (Volume, RepeatMode, ShuffleMode, LastTrackId) — Wave 2

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
- [x] Динамічний перерахунок лічильників при зміні фільтрів — Wave 4
- [x] Автодоповнення: префіксний `MATCH 'deat*'` + сортування по `-bm25` + обкладинки/фото — Wave 4
- [x] Debounce 200мс у UI (`MainWindowViewModel.OnSearchQueryChanged` → `DispatcherTimer`) — Wave 4
- [x] Нечіткий пошук («Чи мали ви на увазі»): Левенштейн при <3 результатах — Wave 4
- [x] Запис `SearchHistory` після кожного виконаного запиту — Wave 4
- [x] `SavedSearches`: CRUD + флаг `NotifyOnNew` + поточна к-ть матчів у профілі — Wave 4/7

### 3.7. Лайки `ILikesService`

- [x] `LikesService`: лайки треків і альбомів (ідемпотентні, подія `Changed`), таблиці `TrackLikes`/`AlbumLikes` — Wave Likes

---

## 4. UI: вікно та глобальна оболонка

### 4.1. `MainWindow`

- [x] Кастомний title bar (`ExtendClientAreaToDecorationsHint`, `WindowDecorations="BorderOnly"`)
- [x] Поле пошуку по центру title bar, `Text="{Binding SearchQuery}"`, Enter → `SubmitSearch`
- [x] Drag вікна через `PointerPressed` → `BeginMoveDrag`; кнопки Min / Max / Close
- [x] Sidebar (Каталог, Пошук, Кошик, Замовлення, Профіль, Плеєр, Адмін)
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
- [x] Кнопка [✕] → `CloseMiniPlayer`, кнопка [⛶] → `ExpandMiniPlayer`; знов з'являється при наступному `MediaOpened`
- [x] Плавна анімація появи/зникнення (translateY + Opacity) — Wave 8
- [x] Мала обкладинка (cover/gradient/FirstChar fallback) — Wave 8
- [x] Регулятор гучності та seek-слайдер у міні-плеєрі — Wave 8 / MiniPlayer-BottomBar

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

### 5.2. Сторінка результатів пошуку (`SearchResultsView` + VM)

- [x] Контейнер з фасетами зліва (280px) + результатами справа
- [x] Альбомо-центрична видача (`AlbumHit` з приміткою «знайдено в треку…») — Wave Search-Redesign
- [x] Фасети: Жанр (multi-select + any/all), Виконавці, Формат, Наявність — з живими лічильниками
- [x] Live-update: чекбокс/спін → миттєвий перезапит — Wave 4
- [-] Chip-tabs «Усі/Альбоми/Виконавці/Треки/Відгуки» — свідомо знято при альбомо-центричному редизайні (диверсія від макета §4.1.3 затверджена)
- [-] Sticky sidebar при скролі — поза scope MVP
- [x] Активні фільтри як видаляні chips над результатами — Wave 8
- [x] Адаптивна сітка результатів: картки розтягуються на всю ширину колонки (`Controls/UniformWrapPanel` + `Square`) — Wave UI-Audit
- [x] Колапс довгих фасет-списків (виконавці): топ-8 + «Показати всі (N)» (`FacetGroupViewModel`) — Wave UI-Audit
- [x] Топ-результат показує обкладинку альбому (зірка — лише fallback) — Wave UI-Audit
- [x] Фасети «Рік», «Ціна» (NumericUpDown від/до), «Рейтинг ★ від N» — Wave 4
- [x] Чекбокс «Тільки в наявності» — facet bucket
- [x] Кнопки «Скинути все», «💾 Зберегти запит» — Wave 4
- [-] Колапс фасет-сайдбара в `[≡ Фільтри]` при <1100px — лишаємо як є
- [x] Топ-результат (окремий блок) — Wave 8
- [x] «Чи мали ви на увазі: …» при <3 результатах — Wave 4
- [x] Автодоповнення під полем пошуку в title bar (Popup, з обкладинками) — Wave 4

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
- [x] `Order.Comment` + заповнення `ShippingAddress` (міграція `AddOrderComment`); адреса префілиться з останнього замовлення; деталі замовлення в `OrdersView` показують контакт/адресу/коментар — Wave Checkout-Flow
- [x] `FlashMessage` після оформлення → замінено екраном успіху (flash лишився для guest-блокування) — Wave Checkout-Flow
- [x] Перевірка авторизації перед `Checkout` — Wave 8 (guest banner + блокування)
- [x] Секція «Купіть також» (той самий виконавець → жанр → бестселери; для порожнього кошика — «Можливо, вас зацікавить») — Wave UI-Audit
- [x] Мініатюри товарів 72px; блок «Разом до сплати» прихований при порожньому кошику — Wave UI-Audit
- [-] Підтвердження «видалити товар?» — поза scope MVP
- [-] Промокоди / знижки — поза scope

### 5.5. Замовлення (`OrdersView` + `OrdersViewModel`)

- [x] Фільтр по `auth.CurrentUser.Id` (admin бачить усі) — Wave 3
- [x] Колонки: №, дата, статус, сума; кнопка «Деталі» з inline-розкриттям OrderItems — Wave 7

### 5.6. Профіль (`ProfileView` + `ProfileViewModel`)

- [x] Дані користувача (ім'я, email, роль)
- [x] Підвкладка «Замовлення» — таблиця з фільтром статусу + inline-expand деталі — Wave 7
- [x] Підвкладка «Мої відгуки» — список з редагуванням/видаленням — Wave 7
- [x] Зміна пароля — inline-панель `ChangePasswordView` (модальне вікно прибрано у Wave Inline-Panels)
- [x] Список збережених запитів з к-тю поточних матчів (`SavedSearchSummary.CurrentCount`) — Wave 7
- [-] Справжній дельта-індикатор «нових товарів» по збереженому запиту — без CreatedAt на Product/Album неточно; показуємо поточну к-ть матчів

### 5.7. Плеєр (`PlayerView` + `PlayerViewModel`)

- [x] Каркас VM з біндингами (TrackTitle, ArtistName, Progress, Volume…)
- [x] Список «куплених альбомів» (`GetPurchasedAlbums`, Status == Completed) — Wave 3
- [x] Drag-slider прогресу (Seek, `IsScrubbing` + `CommitSeek`) — Wave 8
- [x] Кнопка «+ Додати файли» (`OpenFileDialog`) → `IPlayerService.PlayFile` — Wave 8
- [x] Редизайн сторінки альбому: хедер, треклист, відгуки, метадані, fullscreen-обкладинка, хоткеї — Wave Player-Redesign
- [x] Лайки треків/альбомів у плеєрі (`ILikesService`) — Wave Likes
- [-] Reuse `Themes/DarkTheme.axaml` — заміщено `Themes/Colors.axaml` + `ControlStyles.axaml`
- [-] Drag&drop файлів у плейлист — поза scope MVP
- [-] Перенесення плейлистів з M3U в БД — поза scope MVP

### 5.8. Адмінка (`AdminView` + `AdminViewModel`)

- [x] KPI-картки (Товарів / Замовлень / Виручка)
- [x] TabControl з 4 вкладками: «Товари», «Замовлення», «Статистика», «Користувачі»
- [x] Вкладка «Товари»: список з ✎ / 🗑 (soft-delete `SetProductActive(false)`) — Wave 6
- [x] «+ Додати товар» / ✎ → inline-панель `ProductEditView` (Album/Artist/Genre dropdowns + «новий», file pickers; модальне вікно прибрано у Wave Inline-Panels)
- [x] CRUD для виконавців/альбомів/жанрів — inline через форму товару (окремі форми не потрібні)
- [x] Вкладка «Замовлення»: ComboBox статусу + «Зберегти» + inline-деталі — Wave 6
- [x] Вкладка «Статистика»: Топ-10 за продажами + «Виручка за період» з вибором дат — Wave 6
- [x] Вкладка «Користувачі»: таблиця + ComboBox ролі — Wave 6
- [x] Експорт замовлень в Excel (ClosedXML) + товарів у CSV — Wave 6
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
- [x] Integration-тести через `Avalonia.Headless` — `MusicApp.BugHunt/` (20+ файлів: логін-оверлей, inline-панелі, каталог, пошук, плеєр, міні-плеєр, навігація, hover, аватари/обкладинки)
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

## Зведена статистика (після Wave 9, звірено з кодом)

| Категорія | Зроблено [x] | Не зроблено [ ] | Відкинуто [-] |
|---|---|---|---|
| Інфраструктура / стек | 16 | 1 | 3 |
| Модель даних і БД | 18 | 0 | 0 |
| Сервіси | 41 | 0 | 0 |
| UI: оболонка / mini-player | 18 | 0 | 0 |
| UI: екрани | 56 | 0 | 9 |
| Дизайн і тема | 8 | 0 | 1 |
| Конвертери / seed / тести | 17 | 0 | 2 |

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
