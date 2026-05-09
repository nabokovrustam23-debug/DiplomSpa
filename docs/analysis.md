# CRM «Тихий час» — модули, user-story, эндпоинты, матрица прав

Версия документа: черновик 1.3
Стек: ASP.NET Core 8 **Razor Pages**, EF Core (SQLite), собственная авторизация на PBKDF2-HMAC-SHA256, Serilog для логирования, xUnit для тестов.
География: все филиалы в Краснодаре (время MSK / UTC+3 — храним и рендерим в локальном времени, без timezone-conversion).
Дизайн: приглушённая палитра «Латунь на тёплом угле» (вариант A) по умолчанию + светлая «Крем + латунь» (вариант D) через переключатель тем.

Мудборд визуала: <https://moodboard-lrrsiyir.devinapps.com>

## Зафиксированные решения

| # | Вопрос | Решение |
|---|---|---|
| 1 | UI-фреймворк | **Razor Pages** |
| 2 | Шаг свободных слотов | **15 минут** |
| 3 | Cancel cutoff (за сколько часов до визита можно отменить) | **2 часа** (вынесено в `appsettings.json`) |
| 4 | Подтверждение email | **Да** (по ссылке из письма). Подтверждение телефона — нет. |
| 5 | Восстановление пароля | **Да** (по ссылке из письма) |
| 6 | Тесты | **Да**, xUnit, базовое покрытие критичных мест |
| 7 | Сидинг тестовых данных | **Да**, через EF Core `HasData` (2 филиала, 5 услуг, 4 мастера, тестовые учётки) |
| 8 | Логирование | **Serilog** → консоль + файл с ротацией |

## Содержание

1. [Список модулей](#1-список-модулей)
2. [Модуль M1. Аутентификация и роли](#m1-аутентификация-и-роли)
3. [Модуль M2. Каталог: филиалы, услуги, мастера](#m2-каталог-филиалы-услуги-мастера)
4. [Модуль M3. Расписание мастеров](#m3-расписание-мастеров)
5. [Модуль M4. Записи и заявки](#m4-записи-и-заявки)
6. [Модуль M5. Аналитика и отчётность](#m5-аналитика-и-отчётность)
7. [Модуль M6. Уведомления](#m6-уведомления)
8. [Матрица прав «роль × действие»](#матрица-прав-роль--действие)
9. [Сидинг тестовых данных](#сидинг-тестовых-данных)
10. [План тестирования](#план-тестирования)
11. [Конфигурация Serilog](#конфигурация-serilog)
12. [Дизайн UI](#дизайн-ui)
13. [Открытые вопросы](#открытые-вопросы)

---

## 1. Список модулей

| № | Модуль | Назначение | Ключевые таблицы |
|---|---|---|---|
| M1 | Аутентификация и роли | Регистрация, вход, сессии, подтверждение email, сброс пароля, управление ролями, PBKDF2-хеширование. | `Users`, `Roles`, `UserSessions`, `UserTokens`, `Persona`, `ConsentLog` |
| M2 | Каталог | CRUD филиалов, услуг, мастеров; привязка мастер↔услуги. | `Branches`, `Services`, `Masters`, `MasterService`, `Persona` |
| M3 | Расписание | Рабочие смены, обеды, отпуска; расчёт свободных слотов. | `WorkSchedules`, `Bookings` |
| M4 | Записи и заявки | Онлайн-запись клиентом, ручное создание администратором, заявки от гостей, статусы записей, факт оказания услуги. | `Bookings`, `Visits`, `Leads`, `Clients` |
| M5 | Аналитика | Дашборды и отчёты для админа (по своему филиалу) и владельца (сводно/по филиалам). | `Bookings`, `Visits`, `Masters`, `Branches` |
| M6 | Уведомления | Email/SMS/In-app: подтверждение записи, напоминание, отмена, новая заявка администратору. | `Notifications` |

Сквозной модуль (не главный, но нужен для защиты): **Аудит и журналирование** (`AuditLog`, Serilog → файл/консоль).

---

## M1. Аутентификация и роли

### User-story

| ID | Роль | Хочу | Чтобы |
|---|---|---|---|
| US-1.1 | Гость | зарегистрироваться по логину (email или телефон) и паролю | стать клиентом и записываться онлайн |
| US-1.2 | Гость | подтвердить согласие на обработку ПДн при регистрации и подаче заявки | соответствие 152-ФЗ |
| US-1.3 | Любой пользователь | войти в систему по логину и паролю | получить доступ к функциям своей роли |
| US-1.4 | Любой пользователь | выйти из системы (revoke сессии) | защитить свою учётку на чужом устройстве |
| US-1.5 | Клиент | сменить свой пароль | повысить безопасность |
| US-1.6 | Владелец | назначать пользователю роль (Owner/Admin/Master/Client) и привязывать админа к филиалу | управлять правами в системе |
| US-1.7 | Владелец | деактивировать учётную запись (soft-disable) | заблокировать доступ без удаления данных |
| US-1.8 | Любой пользователь | подтвердить email по ссылке из письма | повышение доверия и возможность сброса пароля |
| US-1.9 | Любой пользователь | сбросить забытый пароль по ссылке из письма | восстановить доступ к учётке |

### Эндпоинты (Razor Pages / Controllers)

```
GET   /Account/Register            — форма регистрации гостя → клиент
POST  /Account/Register
GET   /Account/Login
POST  /Account/Login
POST  /Account/Logout
GET   /Account/Profile             — просмотр и редактирование Persona
POST  /Account/Profile
GET   /Account/ChangePassword
POST  /Account/ChangePassword

# Подтверждение email
GET   /Account/ConfirmEmail?token=          — переход по ссылке из письма
POST  /Account/ResendConfirmationEmail      — выслать письмо повторно

# Сброс пароля
GET   /Account/ForgotPassword
POST  /Account/ForgotPassword               — отправляет письмо со ссылкой
GET   /Account/ResetPassword?token=         — форма ввода нового пароля
POST  /Account/ResetPassword

GET   /Owner/Users                 — список всех учёток (с фильтром по роли/филиалу)
GET   /Owner/Users/Edit/{id}       — смена роли, привязка филиала, активация/деактивация
POST  /Owner/Users/Edit/{id}
```

### Подтверждение email и сброс пароля

Оба сценария используют одну таблицу `UserTokens`:

```
UserTokens(TokenId, UserId, Purpose, Token, ExpiresAt, CreatedAt, ConsumedAt)
   Purpose ∈ {EmailVerification, PasswordReset}
   Token   = Base64Url(RandomNumberGenerator 32 байта)
```

**EmailVerification:** при регистрации создаётся запись `UserTokens` с `Purpose='EmailVerification'`, `ExpiresAt = now + 24h`. На email уходит ссылка `/Account/ConfirmEmail?token=…`. При переходе токен помечается `ConsumedAt=now`, а `Users.IsEmailConfirmed=1`. Незаверифицированному пользователю **запрещено создавать онлайн-записи** (но можно входить и редактировать профиль).

**PasswordReset:** на `/Account/ForgotPassword` пользователь вводит email; если такой Login есть и `IsActive=1`, создаётся токен с `Purpose='PasswordReset'`, `ExpiresAt = now + 1h`. Старые активные `PasswordReset`-токены этого пользователя гасятся (`ConsumedAt=now`). На email уходит ссылка `/Account/ResetPassword?token=…`. По ней — форма нового пароля; при сабмите токен потребляется, в `Users` записывается новый `PasswordHash`/`PasswordSalt`, **все активные `UserSessions` ревокаются** (для этого пользователя), чтобы выкинуть атакующего из всех сессий.

Если email не существует в БД — на форме всё равно показываем «если такой email зарегистрирован, мы отправили письмо», чтобы не давать возможность пробивать базу пользователей.

### Алгоритм хеширования

```csharp
// При регистрации
byte[] salt = RandomNumberGenerator.GetBytes(16);
const int iterations = 100_000;
byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
    password, salt, iterations, HashAlgorithmName.SHA256, 32);

user.PasswordSalt       = Convert.ToBase64String(salt);
user.PasswordHash       = Convert.ToBase64String(hash);
user.PasswordIterations = iterations;

// При проверке
byte[] expected = Rfc2898DeriveBytes.Pbkdf2(
    password,
    Convert.FromBase64String(user.PasswordSalt),
    user.PasswordIterations,
    HashAlgorithmName.SHA256, 32);
bool ok = CryptographicOperations.FixedTimeEquals(
    expected, Convert.FromBase64String(user.PasswordHash));
```

### Сессии

- Cookie-based: cookie `session_token` хранит токен из `UserSessions.Token`.
- Token = `RandomNumberGenerator.GetBytes(32)` → Base64Url.
- Время жизни — настраивается (по умолчанию 30 дней).
- При логине — INSERT в `UserSessions`. При логауте — `RevokedAt = now`.
- Middleware читает cookie, ищет неотозванную и не истёкшую сессию, кладёт `User`/`Role`/`BranchId` в `HttpContext.Items`.

---

## M2. Каталог: филиалы, услуги, мастера

### User-story

| ID | Роль | Хочу | Чтобы |
|---|---|---|---|
| US-2.1 | Любой посетитель | видеть публичный список филиалов с адресом и часами работы | выбрать ближайший |
| US-2.2 | Любой посетитель | видеть прайс-лист | оценить стоимость и длительность |
| US-2.3 | Любой посетитель | видеть карточки мастеров филиала (фото, должность, био, услуги) | выбрать мастера для записи |
| US-2.4 | Владелец | CRUD филиалов | управлять сетью |
| US-2.5 | Владелец | CRUD услуг прайс-листа | поддерживать актуальные цены |
| US-2.6 | Администратор/Владелец | CRUD карточек мастеров своего филиала (для админа) или любого (для владельца), привязка к услугам | управлять составом команды |
| US-2.7 | Администратор/Владелец | деактивировать мастера (soft-delete) | сохранить историю при увольнении |

### Эндпоинты

```
# Публичные
GET   /                            — главная: краткие карточки филиалов
GET   /Branches                    — список филиалов
GET   /Branches/{id}               — детальная страница филиала + его мастера
GET   /Services                    — прайс-лист

# Владелец
GET/POST /Owner/Branches
GET/POST /Owner/Branches/Edit/{id}
GET/POST /Owner/Branches/Delete/{id}        — soft-delete (IsActive=0)
GET/POST /Owner/Services
GET/POST /Owner/Services/Edit/{id}

# Владелец и Администратор (фильтр по BranchId на стороне сервиса)
GET/POST /Masters/Manage                    — список мастеров (для админа — только своего филиала)
GET/POST /Masters/Manage/Edit/{id}          — редактирование, выбор услуг (MasterService)
```

---

## M3. Расписание мастеров

### User-story

| ID | Роль | Хочу | Чтобы |
|---|---|---|---|
| US-3.1 | Администратор | проставить рабочие смены и обеды мастеров своего филиала на неделю/месяц вперёд | мастера могли принимать записи |
| US-3.2 | Мастер | посмотреть своё расписание на день/неделю с записями | понимать свою загрузку |
| US-3.3 | Мастер | оставить заявку на отпуск/больничный | согласовать график |
| US-3.4 | Администратор | подтвердить заявку на отпуск/больничный или внести их сам | ведение графика |
| US-3.5 | Любой посетитель/клиент | при онлайн-записи увидеть свободные слоты выбранного мастера на выбранную дату | выбрать удобное время |

### Алгоритм генерации свободных слотов

Вход: `branchId`, `serviceId`, `date`, опционально `masterId` (если не указан — «любой свободный»).

1. Найти мастеров филиала, умеющих делать услугу: `Masters JOIN MasterService WHERE BranchId=… AND ServiceId=… AND IsActive=1`.
2. Для каждого мастера взять его рабочие интервалы: `WorkSchedules WHERE MasterId IN … AND WorkDate=… AND ScheduleType='Work'`.
3. Из этих интервалов вычесть: обеды (`Lunch`), занятые периоды (`Bookings WHERE Status IN ('Created','Confirmed') AND DATE(StartDateTime)=…`).
4. Получившиеся свободные интервалы порезать на слоты длиной `Service.DurationMinutes` (с шагом 15 минут — параметр).
5. Проверить, что слот целиком умещается до конца рабочей смены и до `Branch.ClosingTime`.
6. Вернуть массив `{ masterId, masterName, startDateTime }` (отсортированный по времени).

Слоты считаются «на лету» при каждом запросе — отдельная таблица слотов не нужна.

### Эндпоинты

```
GET   /Master/Schedule             — мастер видит свой график (день/неделя)
GET   /Master/Schedule/RequestLeave
POST  /Master/Schedule/RequestLeave        — заявка на отпуск/больничный

GET   /Admin/Schedule              — таблица «мастер × день» с интервалами
GET   /Admin/Schedule/Edit?masterId=&date=
POST  /Admin/Schedule/Edit                 — добавить/удалить интервал

GET   /api/slots?branchId=&serviceId=&date=&masterId=  — JSON для UI онлайн-записи
```

---

## M4. Записи и заявки

### User-story

| ID | Роль | Хочу | Чтобы |
|---|---|---|---|
| US-4.1 | Клиент | пройти мастер онлайн-записи: филиал → услуга → мастер (или «любой свободный») → дата → слот → подтверждение | записаться без звонка |
| US-4.2 | Клиент | увидеть список своих активных и прошлых записей | помнить о визите |
| US-4.3 | Клиент | отменить свою запись не позднее, чем за N часов до начала (N — параметр, по умолчанию 2) | если планы изменились |
| US-4.4 | Клиент | оставить заявку на консультацию («перезвоните») | если не получается выбрать слот сам |
| US-4.5 | Гость | оставить заявку (имя + телефон + предпочитаемый филиал + комментарий) без регистрации | минимизировать шаг до общения |
| US-4.6 | Администратор | видеть очередь входящих заявок своего филиала, помечать «в работе», превращать заявку в запись | обрабатывать поток клиентов |
| US-4.7 | Администратор | вручную создавать запись (например, по звонку) | работать с не-онлайн клиентами |
| US-4.8 | Администратор | редактировать/переносить/отменять любую запись своего филиала | оперативно реагировать на изменения |
| US-4.9 | Мастер | подтверждать запись (Created → Confirmed) | синхронизироваться с клиентом |
| US-4.10 | Мастер | завершать запись (→ Completed) с указанием суммы и заметок (создаётся `Visit`) | вести историю и аналитику |
| US-4.11 | Мастер | помечать «не пришёл» (→ NoShow) | корректность отчётности |

### Бизнес-правила

- Slot-uniqueness: при INSERT в `Bookings` срабатывает уникальный частичный индекс `UX_Bookings_ActiveSlot`. Если параллельная запись успела занять слот — получаем `SqliteException(UNIQUE constraint failed)`, конвертируем в дружелюбную ошибку «слот занят, выберите другой».
- Cancel cutoff: `Status=Created|Confirmed && (StartDateTime - now) >= cancelCutoffHours`.
- Lead → Booking: при конвертации заявки админ заполняет поля записи; в `Bookings.Source` пишется `'Lead'`, в `Leads.CreatedBookingId` — id новой записи, статус заявки → `Done`.
- Completed: триггерится мастером, в одной транзакции создаём `Visit(BookingId, TotalAmount, MasterNotes)`.
- NoShow: ставится мастером (или админом), отдельная сущность не создаётся.

### Эндпоинты

```
# Клиент
GET   /Booking/New                         — мастер онлайн-записи (multi-step или single page)
POST  /Booking/New
GET   /Account/Bookings                    — мои записи
POST  /Account/Bookings/{id}/Cancel

# Гость + Клиент
GET   /Lead                                — форма заявки (для гостя — без авторизации)
POST  /Lead

# Администратор
GET   /Admin/Bookings?from=&to=&status=    — журнал записей своего филиала
GET   /Admin/Bookings/New
POST  /Admin/Bookings/New
GET   /Admin/Bookings/Edit/{id}
POST  /Admin/Bookings/Edit/{id}
POST  /Admin/Bookings/{id}/Cancel
GET   /Admin/Leads
GET   /Admin/Leads/{id}
POST  /Admin/Leads/{id}/Take                — взять в работу (Status=InProgress)
POST  /Admin/Leads/{id}/Convert             — превратить в Booking
POST  /Admin/Leads/{id}/Reject

# Мастер
GET   /Master/Bookings?date=                — записи на день
POST  /Master/Bookings/{id}/Confirm
POST  /Master/Bookings/{id}/Complete        — body: TotalAmount, MasterNotes
POST  /Master/Bookings/{id}/NoShow
```

---

## M5. Аналитика и отчётность

### User-story

| ID | Роль | Хочу | Чтобы |
|---|---|---|---|
| US-5.1 | Администратор | видеть дашборд по своему филиалу за выбранный период | оценивать работу филиала |
| US-5.2 | Владелец | видеть сводный дашборд по сети + переключение на конкретный филиал | сравнивать филиалы |
| US-5.3 | Администратор/Владелец | выгрузить отчёт в CSV/Excel за период | формальная отчётность |

### Метрики

| Метрика | Формула (упрощённо) |
|---|---|
| Количество записей по статусам | `COUNT(Bookings) GROUP BY Status` за период |
| Выручка | `SUM(Visits.TotalAmount)` за период (через JOIN с `Bookings.BranchId`) |
| Средний чек | `AVG(Visits.TotalAmount)` |
| Загрузка мастера | `SUM(Bookings.DurationMinutes WHERE Status IN (Confirmed, Completed)) / SUM(WorkSchedules.MinutesIfTypeWork)` за период |
| Доля повторных клиентов | `clients_with_>=2_completed_visits / total_clients_with_>=1_completed_visit` |
| Топ-N услуг | `COUNT(Bookings) GROUP BY ServiceId ORDER BY DESC` |
| Отмены | `COUNT(Bookings WHERE Status='Cancelled')` |
| No-show | `COUNT(Bookings WHERE Status='NoShow')` |

### Эндпоинты

```
GET   /Admin/Analytics?from=&to=                    — дашборд (только свой филиал)
GET   /Admin/Analytics/Export?from=&to=&format=csv

GET   /Owner/Analytics?from=&to=&branchId=          — дашборд (вся сеть или один филиал)
GET   /Owner/Analytics/Compare?from=&to=            — сравнение филиалов (таблица)
GET   /Owner/Analytics/Export?from=&to=&branchId=
```

UI: SVG-графики (Chart.js или Razor partials с inline SVG — на выбор; для диплома Chart.js проще).

---

## M6. Уведомления

### User-story

| ID | Роль | Хочу | Чтобы |
|---|---|---|---|
| US-6.1 | Клиент | при создании записи получить email-подтверждение | убедиться, что бронирование прошло |
| US-6.2 | Клиент | за N часов до визита получить email-напоминание | не забыть |
| US-6.3 | Клиент | при отмене получить уведомление | подтверждение действия |
| US-6.4 | Администратор | при поступлении новой заявки получить in-app уведомление | оперативно перезвонить |
| US-6.5 | Мастер | при создании записи на его имя получить in-app уведомление | держать график |

### Архитектура

- Доменные сервисы при бизнес-событиях создают строку в `Notifications` со `Status='Pending'`.
- Отдельный `BackgroundService` (`NotificationDispatcher`) опрашивает таблицу раз в N секунд и:
  - для `Channel='Email'` — отправляет через SMTP (для диплома достаточно `MailKit` + Mailtrap или папка `mail-drop/` с .eml-файлами);
  - для `Channel='Sms'` — заглушка с логированием (реальный шлюз стоит денег);
  - для `Channel='InApp'` — оставляет в БД, фронт показывает уведомления через polling или SignalR.
- При успехе → `Status='Sent', SentAt=now`. При ошибке → `Status='Failed', Error=…`. Retry-policy — экспоненциальный backoff (опционально).
- Напоминания за N часов: фоновый job раз в час сканирует `Bookings WHERE Status IN (Created,Confirmed) AND StartDateTime BETWEEN now+N AND now+N+1h` и создаёт `Notifications`.

### Эндпоинты (только для просмотра пользователем)

```
GET   /Account/Notifications              — мои in-app уведомления
POST  /Account/Notifications/{id}/Read
```

---

## Матрица прав «роль × действие»

Обозначения: `✓` — разрешено всегда; `✓*` — разрешено только в пределах своего филиала / для своих сущностей; `–` — запрещено.

| Действие | Гость | Клиент | Мастер | Админ | Владелец |
|---|---|---|---|---|---|
| **M1. Аутентификация** ||||||
| Регистрация (создание Client-учётки) | ✓ | – | – | – | – |
| Вход / выход | – | ✓ | ✓ | ✓ | ✓ |
| Просмотр и редактирование своего профиля | – | ✓ | ✓ | ✓ | ✓ |
| Смена своего пароля | – | ✓ | ✓ | ✓ | ✓ |
| Управление учётными записями (роли, привязка филиала, деактивация) | – | – | – | – | ✓ |
| **M2. Каталог** ||||||
| Просмотр публичных страниц (филиалы, услуги, мастера) | ✓ | ✓ | ✓ | ✓ | ✓ |
| CRUD филиалов | – | – | – | – | ✓ |
| CRUD услуг прайс-листа | – | – | – | – | ✓ |
| CRUD карточек мастеров | – | – | – | ✓* | ✓ |
| Привязка услуг к мастеру | – | – | – | ✓* | ✓ |
| **M3. Расписание** ||||||
| Просмотр своего расписания | – | – | ✓ | – | – |
| Заявка на отпуск/больничный | – | – | ✓ | – | – |
| Просмотр расписания мастеров филиала | – | – | – | ✓* | ✓ |
| Создание/редактирование рабочих смен и обедов | – | – | – | ✓* | ✓ |
| Утверждение отпусков/больничных | – | – | – | ✓* | ✓ |
| **M4. Записи и заявки** ||||||
| Создание онлайн-записи | – | ✓ | – | – | – |
| Просмотр своих записей | – | ✓ | – | – | – |
| Отмена своей записи (с учётом cutoff) | – | ✓ | – | – | – |
| Оставление заявки (Lead) | ✓ | ✓ | – | – | – |
| Просмотр и редактирование любой записи филиала | – | – | – | ✓* | ✓ |
| Ручное создание записи | – | – | – | ✓* | ✓ |
| Обработка заявок и конвертация в запись | – | – | – | ✓* | ✓ |
| Просмотр своего расписания записей (день/неделя) | – | – | ✓ | ✓* | ✓ |
| Подтверждение записи (Created → Confirmed) | – | – | ✓* | ✓* | ✓ |
| Завершение записи (→ Completed, с созданием Visit) | – | – | ✓* | ✓* | ✓ |
| Отметка NoShow | – | – | ✓* | ✓* | ✓ |
| **M5. Аналитика** ||||||
| Дашборд по своему филиалу | – | – | – | ✓* | ✓ |
| Сводный дашборд по сети, сравнение филиалов | – | – | – | – | ✓ |
| Экспорт отчётов | – | – | – | ✓* | ✓ |
| **M6. Уведомления** ||||||
| Просмотр своих in-app уведомлений | – | ✓ | ✓ | ✓ | ✓ |
| Получение email-уведомлений о записях | – | ✓ | – | – | – |
| **Сквозное** ||||||
| Просмотр AuditLog | – | – | – | – | ✓ |

### Реализация в коде

- В `HttpContext` middleware кладёт `CurrentUser` (UserId, RoleCode, BranchId).
- Атрибуты-фильтры:
  - `[Authorize]` — проверяет наличие сессии.
  - `[AuthorizeRole("Owner")]` — проверяет роль (свой кастомный, не из Identity).
  - `[AuthorizeRole("Admin","Owner")]` — список ролей.
  - Для проверки «свой филиал» — атрибут или ручная проверка в action: `if (role=="Admin" && entity.BranchId != currentUser.BranchId) return Forbid();`.

---

## Сидинг тестовых данных

Сидинг через EF Core `modelBuilder.Entity<…>().HasData(…)` в `OnModelCreating`. Применяется миграцией. Для удобства защиты заложено:

**Справочники**
- 4 роли (`Owner`, `Admin`, `Master`, `Client`) — уже в `INSERT INTO Roles` в схеме.
- 2 филиала: «Тихий час на Ленина» (10:00–22:00) и «Тихий час на Победы» (09:00–21:00).
- 5 услуг с длительностью и ценой:
  - Мужская стрижка — 60 мин, 1500 ₽
  - Стрижка машинкой — 30 мин, 800 ₽
  - Бритьё опасной бритвой — 45 мин, 1200 ₽
  - Стрижка бороды — 30 мин, 700 ₽
  - Камуфляж седины — 30 мин, 900 ₽

**Учётки** (пароль у всех тестовых аккаунтов одинаковый — `Test12345!`, в проде должен быть выпилен сидинг паролей):
- Владелец: `owner@tihiychas.ru`
- Администраторы: `admin1@tihiychas.ru` (филиал 1), `admin2@tihiychas.ru` (филиал 2)
- Мастера: 4 штуки, по 2 в каждом филиале (`master1@…` … `master4@…`), каждому привязаны 3–5 услуг через `MasterService`
- Клиенты: 3 штуки (`client1@…` … `client3@…`), у одного — несколько завершённых записей в прошлом для красивой аналитики

**Расписание и записи** (генерируются seed-классом, не вручную в HasData — слишком много строк):
- На текущую неделю + следующую: рабочие смены мастеров (`Work` 10:00–22:00 с обедом 14:00–15:00).
- Несколько `Bookings` в прошлом (`Status='Completed'` + соответствующие `Visits`) — чтобы аналитика не была пустой.
- Несколько `Bookings` в будущем (`Status='Created'`/`'Confirmed'`) — для демонстрации workflow мастера.
- Пара `Leads` в статусе `New` — чтобы у админа на дашборде было что обработать.

Эти данные удобно вынести в отдельный класс `DataSeeder` и запускать из `Program.cs` при старте, если БД пустая (а статические справочники — через `HasData` в миграции).

---

## План тестирования

Проект тестов: `tests/BarbershopCrm.Tests` (xUnit + FluentAssertions + EF Core In-Memory или SQLite-in-memory).

### Обязательные тесты (минимум для защиты)

| # | Что тестируем | Почему критично |
|---|---|---|
| T1 | `PasswordHasher.Hash` → `Verify` возвращает true для правильного пароля и false для неправильного | Доказывает корректность собственной криптографии |
| T2 | `PasswordHasher` с одинаковым паролем но разной солью даёт разные хеши | Проверка использования соли |
| T3 | `SlotService.GetAvailableSlots` исключает слоты, пересекающиеся с существующими `Bookings` | Защита от двойного бронирования на бизнес-уровне |
| T4 | `SlotService.GetAvailableSlots` исключает обеды и нерабочие интервалы | Корректность алгоритма генерации слотов |
| T5 | INSERT в `Bookings` с конфликтным слотом падает с `UNIQUE constraint failed` | Защита на уровне БД (через `UX_Bookings_ActiveSlot`) |
| T6 | Отмена записи позже cancel cutoff (по умолчанию 2 ч) запрещена | Бизнес-правило |
| T7 | Lead → Booking: `Leads.CreatedBookingId` заполняется, статус становится `Done`, новая запись имеет `Source='Lead'` | Корректность сценария обработки заявки |
| T8 | Авторизация: пользователь с ролью Admin не может редактировать запись чужого филиала | Безопасность multi-tenancy по филиалам |

### Рекомендуемые (если успеваешь)

| # | Что тестируем |
|---|---|
| T9 | `EmailConfirmationService`: токен консьюмится, повторное использование падает |
| T10 | `PasswordResetService`: после сброса все `UserSessions` пользователя ревокаются |
| T11 | `AnalyticsService`: метрики (выручка, средний чек) считаются корректно на синтетических данных |
| T12 | `NotificationDispatcher`: запись со статусом `Pending` обрабатывается, ставится `Sent`/`Failed` |

### Подход

- Юнит-тесты для чистых функций (`PasswordHasher`, расчёты в `SlotService`/`AnalyticsService`).
- Интеграционные тесты с **SQLite-in-memory** (`Microsoft.Data.Sqlite` + `:memory:` или временный файл) для проверки констрейнтов БД (`UX_Bookings_ActiveSlot`, FK, CHECK).
- Razor Pages-страницы можно покрывать через `WebApplicationFactory<Program>` (E2E), но для диплома это перебор — достаточно сервисного слоя.

---

## Конфигурация Serilog

```csharp
// Program.cs
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "BarbershopCrm")
    .WriteTo.Console()
    .WriteTo.File(
        path: "logs/app-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate:
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}"));
```

Пакеты: `Serilog.AspNetCore`, `Serilog.Sinks.Console`, `Serilog.Sinks.File`, `Serilog.Settings.Configuration`.

`appsettings.json`:
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.EntityFrameworkCore.Database.Command": "Information",
        "Microsoft.AspNetCore": "Warning"
      }
    }
  }
}
```

Логируем обязательно: вход/выход пользователей (без паролей), создание/отмена записей, конвертацию заявок, изменения ролей (это пишется и в `AuditLog`, и в Serilog), ошибки отправки уведомлений, исключения.

---

## Дизайн UI

Визуальный язык «Тихого часа» — приглушённые тона, теплое углистое основание, акцент в латуни. Без высококонтрастных ярких блоков, без неона. Идея — вечер в барбершопе с тёплым светом.

### Режимы

Обе темы с переключателем в хедере (иконка луна/солнце):
- **Тёмная** (по умолчанию) — вариант A из мудборда.
- **Светлая** (альтернатива) — вариант D.

Выбор пользователя хранится в cookie `theme` (`dark` | `light`) сроком на год, рендерится на сервере в `<html data-theme="...">` — без «мигания» при загрузке. Если cookie нет — берём тёмную (или `prefers-color-scheme`, если хочется умнее — решим на реализации).

### Палитра как CSS-переменные

```css
:root, [data-theme="dark"] {
  /* фон и поверхности */
  --color-bg:        #1F1B17;  /* тёплый уголь */
  --color-surface:   #2A251F;  /* карточки, модальные окна */
  --color-surface-2: #332C24;  /* hover-состояние карточек */
  --color-border:    #3B342C;  /* линии */

  /* текст */
  --color-text:      #E8E0D2;  /* основной */
  --color-text-muted:#A39685;  /* вторичный, подписи */
  --color-text-faint:#6E6358;  /* дисейбл */

  /* акцент */
  --color-accent:        #B89968;  /* латунь */
  --color-accent-hover:  #CCAE7E;  /* hover */
  --color-accent-on:     #1F1B17;  /* цвет текста на акцентном фоне */

  /* семантические */
  --color-success:   #7A8471;  /* приглушённый шалфей */
  --color-error:     #A8675F;  /* терракот */
  --color-warning:   #C9A063;  /* бронза */
  --color-info:      #7B8B96;  /* дымчатый голубой */
}

[data-theme="light"] {
  --color-bg:        #F5F1EA;  /* крем */
  --color-surface:   #FFFFFF;
  --color-surface-2: #EFE9DD;
  --color-border:    #E5DECF;

  --color-text:      #2C2620;
  --color-text-muted:#6B6258;
  --color-text-faint:#9A8F7F;

  --color-accent:        #8E7548;
  --color-accent-hover:  #A88958;
  --color-accent-on:     #FFFFFF;

  --color-success:   #5A6B52;
  --color-error:     #8E5048;
  --color-warning:   #A07338;
  --color-info:      #5A6B78;
}
```

### Типографика

- **Заголовки** (h1–h3, названия карточек, брендовые элементы): **Cormorant Garamond** (Google Fonts), вес 500–600.
- **Основной текст, кнопки, формы, таблицы**: **Inter** (Google Fonts), вес 400/500/600.
- **Цифры** (цены, слоты, выручка): табулярные — `font-variant-numeric: tabular-nums`.
- **Моноширинный** (HEX, токены, идентификаторы): `ui-monospace, 'Cascadia Code', monospace`.

Размеры: 14px базовый текст, 16px в формах, 20–24px подзаголовки, 32–48px большие заголовки. Интерлиньяж 1.5–1.6 для текста.

Шрифты подключаются одним блоком в `_Layout.cshtml`:
```html
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Cormorant+Garamond:wght@500;600&family=Inter:wght@400;500;600&display=swap" rel="stylesheet">
```

### Базовые компоненты

Собираем мини-библиотеку CSS-классов (`wwwroot/css/site.css` + Razor partials). Без Bootstrap-бутилки — собственная, под свои переменные.

| Компонент | Что внутри |
|---|---|
| `.btn` / `.btn--outline` / `.btn--ghost` | Кнопки. Радиус 8px, паддинг 12×22, hover-подсветка латуни. |
| `.card` | Карточка на `--color-surface`, тихий бордер, radius 12px. |
| `.field` (label + input/select) | Прозрачный фон, бордер `--color-border`, при фокусе — outline `--color-accent`. |
| `.badge` (`.success`, `.error`, `.info`, `.warning`) | Плашки статусов: полупрозрачный фон семантического цвета, текст этим же цветом. |
| `.slot` / `.slot--active` | Кнопки-слоты 10:00, 10:15. При выборе — фон `--color-accent`. |
| `.flash` (`.flash--success`, `--error`) | Алёрты в верхней части страницы (TempData → паршал). |
| `.table--quiet` | Таблицы без zebra-полос, только тонкие разделители. |
| `.dialog` | Модальные окна на `<dialog>` элементе (нативный HTML, без JS-фреймворка). |

Иконки: **Lucide** (инлайн SVG или через web-component) — тонкие, линейные, хорошо лягут на приглушённый стиль. Никакого Material/FontAwesome — они слишком жирные для выбранного языка.

### Лейаут и layout-страницы

- **Два layout'а**: `_Layout.cshtml` (публичные + клиент) и `_AdminLayout.cshtml` (админ/владелец/мастер — с боковым меню).
- **Грид**: 12-колоночный не нужен. Обычный CSS Grid / Flexbox по месту.
- **Ширина контента**: 1200px max-width на дашбордах, 800px на публичных страницах, 480px на формах логина/регистрации.
- **Отступы**: `--space-1: 4px`, `2: 8px`, `3: 12px`, `4: 16px`, `5: 24px`, `6: 32px`, `8: 48px`. Только эти значения.
- **Адаптивность**: один брейкпоинт 720px (mobile/desktop). Планшетный промежуточный вид не обязателен для диплома.

### Переключатель тем — реализация

```
GET  /Theme/Set?value=dark|light&returnUrl=/...   — ставит cookie, редиректит обратно
```

На сервере в `_Layout.cshtml`:
```cshtml
@{
    var theme = Context.Request.Cookies["theme"];
    if (theme != "light" && theme != "dark") theme = "dark";
}
<html lang="ru" data-theme="@theme">
```

Кнопка в хедере вызывает GET-эндпоинт. Никакого localStorage — cookie позволяет рендерить сразу в правильной теме без мигания.

### Микро-требования

- Никаких резких теней. Drop-shadow разрешён только на светлой теме и очень лёгкий (`0 1px 2px rgba(0,0,0,0.04)`).
- Анимации — короткие (150–200ms), `ease-out`. Никаких bounce'ов.
- Радиусы — одни и те же: 8px (кнопки, поля), 12px (карточки), 999px (бейджи).
- Контраст WCAG AA обязателен для текста. Текущая палитра его даёт на основном тексте; muted-text на границе (4.5:1) — проверим при реализации.
- Для логотипа «Тихий час» — wordmark на Cormorant Garamond, без иллюстрации пока. Можно добавить иконку (ножницы/луна), если захочешь, на этапе реализации.

### Мудборд

Опубликован интерактивный пример всех 4 вариантов (A — основной, D — светлый режим, B/C — отброшены): <https://moodboard-lrrsiyir.devinapps.com>.

---

## Открытые вопросы

Все ранее поставленные вопросы закрыты. Последние решения, влияющие на реализацию:

| # | Вопрос | Решение |
|---|---|---|
| 9  | Поведение при неподтверждённом email | **Мягкий вариант**: логин разрешён, запрещена только онлайн-запись и сброс пароля, на UI баннер с кнопкой «выслать письмо повторно». |
| 10 | «Любой свободный мастер» | **Равномерное распределение**: система выбирает мастера с наименьшим числом активных записей на этот день среди тех, кто умеет выбранную услугу и работает в этот слот. |
| 11 | Сетка слотов (точки старта) | Шаг 15 мин от рабочего начала мастера, без выравнивания по «круглым» часам (принято). |
| 12 | Часовой пояс | Все филиалы в Краснодаре (MSK / UTC+3). Храним и показываем всё в локальном времени, без timezone-conversion. |

### Алгоритм «любой свободный мастер» (вопрос 10)

Псевдокод:

```
вход: branchId, serviceId, startDateTime
1. eligibleMasters = SELECT m.*
   FROM Masters m
   JOIN MasterService ms ON ms.MasterId = m.MasterId
   WHERE m.BranchId   = :branchId
     AND m.IsActive   = 1
     AND ms.ServiceId = :serviceId
     AND EXISTS (
         -- мастер работает в этот интервал
         SELECT 1 FROM WorkSchedules ws
         WHERE ws.MasterId = m.MasterId
           AND ws.WorkDate = DATE(:startDateTime)
           AND ws.ScheduleType = 'Work'
           AND TIME(:startDateTime)            >= ws.StartTime
           AND TIME(:startDateTime + duration) <= ws.EndTime)
     AND NOT EXISTS (
         -- мастер не обедает в этот интервал
         SELECT 1 FROM WorkSchedules ws
         WHERE ws.MasterId = m.MasterId
           AND ws.WorkDate = DATE(:startDateTime)
           AND ws.ScheduleType IN ('Lunch','Vacation','SickLeave','DayOff')
           AND TIME(:startDateTime) < ws.EndTime
           AND TIME(:startDateTime + duration) > ws.StartTime)
     AND NOT EXISTS (
         -- мастер свободен на этот интервал
         SELECT 1 FROM Bookings b
         WHERE b.MasterId = m.MasterId
           AND b.Status IN ('Created','Confirmed')
           AND b.StartDateTime < :startDateTime + duration
           AND b.StartDateTime + b.DurationMinutes > :startDateTime)

2. для каждого из eligibleMasters посчитать
   bookingsToday = COUNT(Bookings WHERE MasterId=m AND DATE=today
                          AND Status IN ('Created','Confirmed'))

3. выбрать мастера с минимальным bookingsToday;
   при равенстве — с меньшим MasterId (детерминированно)
```

Стоит покрыть тестом (T13): два мастера, у первого 3 записи на день, у второго 1 → `AnyMasterPicker` выбирает второго.
