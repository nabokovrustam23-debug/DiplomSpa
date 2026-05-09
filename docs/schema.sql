-- =============================================================================
-- Схема БД CRM-системы сети барбершопов «Тихий час»
-- СУБД: SQLite 3.8+
-- Версия: черновик 2.0
-- =============================================================================
-- Примечания:
--   1) Авторизация — собственная реализация (без ASP.NET Core Identity).
--      Пароли хранятся как PBKDF2-HMAC-SHA256: соль 16 байт, ≥100 000 итераций,
--      выход 32 байта. Хеш и соль хранятся в Base64 в TEXT-полях.
--      В .NET используется System.Security.Cryptography.Rfc2898DeriveBytes
--      с HashAlgorithmName.SHA256 — формально это «своя» реализация поверх
--      SHA-256, не Identity.
--   2) SQLite не различает типы строго: NUMERIC, TEXT, INTEGER, REAL — это
--      type affinity. Денежные суммы храним как NUMERIC (EF Core маппит
--      decimal в TEXT с сохранением точности).
--   3) Даты/время храним в ISO 8601 как TEXT (рекомендация SQLite),
--      EF Core маппит DateTime/DateOnly/TimeOnly в этот формат.
--   4) Внешние ключи в SQLite по умолчанию выключены — приложение должно
--      выполнять PRAGMA foreign_keys = ON при каждом подключении.
--      В EF Core это настраивается через UseSqlite + опции провайдера.
--   5) Каскадное удаление допустимо, но используется только для таблиц-связок
--      и сессий. Для основных сущностей применяется ON DELETE NO ACTION
--      и soft-delete на уровне приложения (поле IsActive).
-- =============================================================================

PRAGMA foreign_keys = ON;

-- =============================================================================
-- Persona — Физическое лицо
-- Общие персональные данные. На неё ссылаются Users, Masters, Clients
-- через UNIQUE FK (связь 1:1). Это позволяет одному физлицу быть, например,
-- одновременно мастером и клиентом без дублирования ФИО/телефона.
-- =============================================================================
CREATE TABLE Persona (
    PersonaId   INTEGER PRIMARY KEY AUTOINCREMENT,
    LastName    TEXT    NOT NULL,
    FirstName   TEXT    NOT NULL,
    MiddleName  TEXT    NULL,
    Phone       TEXT    NOT NULL,
    Email       TEXT    NULL,
    BirthDate   TEXT    NULL,           -- 'YYYY-MM-DD'
    Gender      TEXT    NULL,           -- 'М' | 'Ж'
    CHECK (Gender IS NULL OR Gender IN ('М', 'Ж'))
);

CREATE UNIQUE INDEX UX_Persona_Phone ON Persona(Phone);


-- =============================================================================
-- Roles — справочник ролей
-- =============================================================================
CREATE TABLE Roles (
    RoleId  INTEGER PRIMARY KEY AUTOINCREMENT,
    Code    TEXT    NOT NULL UNIQUE,    -- Owner|Admin|Master|Client
    Name    TEXT    NOT NULL
);

INSERT INTO Roles(Code, Name) VALUES
    ('Owner',  'Владелец сети'),
    ('Admin',  'Администратор филиала'),
    ('Master', 'Мастер'),
    ('Client', 'Клиент');


-- =============================================================================
-- Branches — Филиалы
-- =============================================================================
CREATE TABLE Branches (
    BranchId    INTEGER PRIMARY KEY AUTOINCREMENT,
    Name        TEXT    NOT NULL,
    Address     TEXT    NOT NULL,
    Phone       TEXT    NULL,
    OpeningTime TEXT    NOT NULL,       -- 'HH:MM:SS'
    ClosingTime TEXT    NOT NULL,
    IsActive    INTEGER NOT NULL DEFAULT 1,
    CHECK (ClosingTime > OpeningTime),
    CHECK (IsActive IN (0, 1))
);


-- =============================================================================
-- Users — учётная запись для собственной авторизации
-- BranchId заполняется только для роли Admin (его филиал).
-- Для Master филиал лежит в Masters.BranchId; для Owner и Client — NULL.
-- =============================================================================
CREATE TABLE Users (
    UserId             INTEGER PRIMARY KEY AUTOINCREMENT,
    PersonaId          INTEGER NOT NULL UNIQUE,
    RoleId             INTEGER NOT NULL,
    BranchId           INTEGER NULL,
    Login              TEXT    NOT NULL UNIQUE,    -- email или нормализованный телефон
    PasswordHash       TEXT    NOT NULL,           -- Base64(PBKDF2-HMAC-SHA256, 32 байта)
    PasswordSalt       TEXT    NOT NULL,           -- Base64(16 байт)
    PasswordIterations INTEGER NOT NULL DEFAULT 100000,
    IsEmailConfirmed   INTEGER NOT NULL DEFAULT 0, -- 0 — email не подтверждён
    IsActive           INTEGER NOT NULL DEFAULT 1,
    CreatedAt          TEXT    NOT NULL DEFAULT (datetime('now')),
    LastLoginAt        TEXT    NULL,
    CHECK (IsActive IN (0, 1)),
    CHECK (IsEmailConfirmed IN (0, 1)),
    FOREIGN KEY (PersonaId) REFERENCES Persona(PersonaId) ON DELETE NO ACTION,
    FOREIGN KEY (RoleId)    REFERENCES Roles(RoleId)      ON DELETE NO ACTION,
    FOREIGN KEY (BranchId)  REFERENCES Branches(BranchId) ON DELETE NO ACTION
);

CREATE INDEX IX_Users_Branch ON Users(BranchId);


-- =============================================================================
-- UserTokens — одноразовые токены (подтверждение email, сброс пароля)
-- Один пользователь может иметь несколько активных токенов разных типов;
-- старые токены того же типа гасятся при выпуске нового (на уровне приложения).
-- =============================================================================
CREATE TABLE UserTokens (
    TokenId     INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId      INTEGER NOT NULL,
    Purpose     TEXT    NOT NULL,           -- EmailVerification|PasswordReset
    Token       TEXT    NOT NULL UNIQUE,    -- Base64Url(RandomNumberGenerator 32 байта)
    ExpiresAt   TEXT    NOT NULL,
    CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now')),
    ConsumedAt  TEXT    NULL,
    CHECK (Purpose IN ('EmailVerification','PasswordReset')),
    FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE CASCADE
);

CREATE INDEX IX_UserTokens_User_Purpose ON UserTokens(UserId, Purpose);


-- =============================================================================
-- UserSessions — токены аутентификации (cookie-based или Bearer)
-- =============================================================================
CREATE TABLE UserSessions (
    SessionId   INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId      INTEGER NOT NULL,
    Token       TEXT    NOT NULL UNIQUE,
    ExpiresAt   TEXT    NOT NULL,
    CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now')),
    RevokedAt   TEXT    NULL,
    UserAgent   TEXT    NULL,
    IpAddress   TEXT    NULL,
    FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE CASCADE
);

CREATE INDEX IX_UserSessions_User ON UserSessions(UserId);


-- =============================================================================
-- Services — Услуги прайс-листа (общий прайс на всю сеть)
-- =============================================================================
CREATE TABLE Services (
    ServiceId       INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT    NOT NULL,
    Description     TEXT    NULL,
    DurationMinutes INTEGER NOT NULL,
    Price           NUMERIC NOT NULL,
    IsActive        INTEGER NOT NULL DEFAULT 1,
    CHECK (DurationMinutes > 0),
    CHECK (Price >= 0),
    CHECK (IsActive IN (0, 1))
);


-- =============================================================================
-- Masters — Мастера-барберы. Привязаны к одному филиалу (1:N от Branches).
-- =============================================================================
CREATE TABLE Masters (
    MasterId    INTEGER PRIMARY KEY AUTOINCREMENT,
    PersonaId   INTEGER NOT NULL UNIQUE,
    BranchId    INTEGER NOT NULL,
    Position    TEXT    NOT NULL,
    HireDate    TEXT    NOT NULL,
    AvatarPath  TEXT    NULL,
    Bio         TEXT    NULL,
    IsActive    INTEGER NOT NULL DEFAULT 1,
    CHECK (IsActive IN (0, 1)),
    FOREIGN KEY (PersonaId) REFERENCES Persona(PersonaId) ON DELETE NO ACTION,
    FOREIGN KEY (BranchId)  REFERENCES Branches(BranchId) ON DELETE NO ACTION
);

CREATE INDEX IX_Masters_Branch ON Masters(BranchId);


-- =============================================================================
-- MasterService — связка «мастер ↔ услуги» (N:M)
-- Один мастер делает несколько услуг; одну услугу делают несколько мастеров.
-- =============================================================================
CREATE TABLE MasterService (
    MasterId  INTEGER NOT NULL,
    ServiceId INTEGER NOT NULL,
    PRIMARY KEY (MasterId, ServiceId),
    FOREIGN KEY (MasterId)  REFERENCES Masters(MasterId)   ON DELETE CASCADE,
    FOREIGN KEY (ServiceId) REFERENCES Services(ServiceId) ON DELETE CASCADE
);


-- =============================================================================
-- Clients — Клиенты
-- Может существовать «гостевой» клиент (без Users) — Persona заполнена,
-- учётной записи нет. При последующей регистрации Persona связывается
-- с Users по PersonaId, и история визитов «прилипает» автоматически.
-- =============================================================================
CREATE TABLE Clients (
    ClientId    INTEGER PRIMARY KEY AUTOINCREMENT,
    PersonaId   INTEGER NOT NULL UNIQUE,
    Source      TEXT    NULL,           -- откуда узнал: Реклама|Рекомендация|...
    Notes       TEXT    NULL,
    CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (PersonaId) REFERENCES Persona(PersonaId) ON DELETE NO ACTION
);


-- =============================================================================
-- WorkSchedules — Рабочий график мастера
-- Смена, обед, выходной, отпуск, больничный.
-- =============================================================================
CREATE TABLE WorkSchedules (
    WorkScheduleId  INTEGER PRIMARY KEY AUTOINCREMENT,
    MasterId        INTEGER NOT NULL,
    BranchId        INTEGER NOT NULL,
    WorkDate        TEXT    NOT NULL,   -- 'YYYY-MM-DD'
    StartTime       TEXT    NOT NULL,   -- 'HH:MM:SS'
    EndTime         TEXT    NOT NULL,
    ScheduleType    TEXT    NOT NULL,   -- Work|Lunch|DayOff|Vacation|SickLeave
    CHECK (EndTime > StartTime),
    CHECK (ScheduleType IN ('Work','Lunch','DayOff','Vacation','SickLeave')),
    UNIQUE (MasterId, WorkDate, StartTime),
    FOREIGN KEY (MasterId) REFERENCES Masters(MasterId)   ON DELETE NO ACTION,
    FOREIGN KEY (BranchId) REFERENCES Branches(BranchId)  ON DELETE NO ACTION
);

CREATE INDEX IX_WorkSchedules_Master_Date ON WorkSchedules(MasterId, WorkDate);


-- =============================================================================
-- Bookings — Записи на приём
-- DurationMinutes и PriceSnapshot — «моментальные снимки» данных услуги
-- на момент создания записи (изменение прайс-листа не ломает старые записи).
-- =============================================================================
CREATE TABLE Bookings (
    BookingId       INTEGER PRIMARY KEY AUTOINCREMENT,
    ClientId        INTEGER NOT NULL,
    MasterId        INTEGER NOT NULL,
    ServiceId       INTEGER NOT NULL,
    BranchId        INTEGER NOT NULL,
    StartDateTime   TEXT    NOT NULL,           -- ISO 8601 'YYYY-MM-DD HH:MM:SS'
    DurationMinutes INTEGER NOT NULL,
    PriceSnapshot   NUMERIC NOT NULL,
    Status          TEXT    NOT NULL DEFAULT 'Created',
    Source          TEXT    NOT NULL DEFAULT 'Online',  -- Online|Admin|Lead
    CancelReason    TEXT    NULL,
    CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
    UpdatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
    CHECK (DurationMinutes > 0),
    CHECK (PriceSnapshot >= 0),
    CHECK (Status IN ('Created','Confirmed','Cancelled','Completed','NoShow')),
    CHECK (Source IN ('Online','Admin','Lead')),
    FOREIGN KEY (ClientId)  REFERENCES Clients(ClientId)   ON DELETE NO ACTION,
    FOREIGN KEY (MasterId)  REFERENCES Masters(MasterId)   ON DELETE NO ACTION,
    FOREIGN KEY (ServiceId) REFERENCES Services(ServiceId) ON DELETE NO ACTION,
    FOREIGN KEY (BranchId)  REFERENCES Branches(BranchId)  ON DELETE NO ACTION
);

-- Уникальный частичный индекс — защита от двойного бронирования слота.
-- Применяется только к активным записям; отменённые/завершённые не блокируют слот.
-- В SQLite фильтрованные индексы поддерживаются с версии 3.8.
CREATE UNIQUE INDEX UX_Bookings_ActiveSlot
    ON Bookings(MasterId, StartDateTime)
    WHERE Status IN ('Created','Confirmed');

CREATE INDEX IX_Bookings_Master_Start ON Bookings(MasterId, StartDateTime);
CREATE INDEX IX_Bookings_Client       ON Bookings(ClientId);
CREATE INDEX IX_Bookings_Branch_Start ON Bookings(BranchId, StartDateTime);


-- =============================================================================
-- Visits — Факт оказания услуги (1:1 с Bookings)
-- Создаётся при переводе записи в статус 'Completed'.
-- TotalAmount может отличаться от PriceSnapshot (скидка, доп. услуга).
-- =============================================================================
CREATE TABLE Visits (
    VisitId     INTEGER PRIMARY KEY AUTOINCREMENT,
    BookingId   INTEGER NOT NULL UNIQUE,
    TotalAmount NUMERIC NOT NULL,
    MasterNotes TEXT    NULL,
    CompletedAt TEXT    NOT NULL DEFAULT (datetime('now')),
    CHECK (TotalAmount >= 0),
    FOREIGN KEY (BookingId) REFERENCES Bookings(BookingId) ON DELETE NO ACTION
);


-- =============================================================================
-- Leads — Заявки от гостей/клиентов на консультацию (звонок администратора)
-- PersonaId NULL для анонимного гостя; RawName/RawPhone хранят данные «как ввели»,
-- даже если Persona впоследствии будет создана и слинкована.
-- =============================================================================
CREATE TABLE Leads (
    LeadId             INTEGER PRIMARY KEY AUTOINCREMENT,
    PersonaId          INTEGER NULL,
    RawName            TEXT    NOT NULL,
    RawPhone           TEXT    NOT NULL,
    PreferredBranchId  INTEGER NULL,
    Comment            TEXT    NULL,
    Status             TEXT    NOT NULL DEFAULT 'New',
    CreatedBookingId   INTEGER NULL,
    ProcessedByUserId  INTEGER NULL,
    CreatedAt          TEXT    NOT NULL DEFAULT (datetime('now')),
    ProcessedAt        TEXT    NULL,
    CHECK (Status IN ('New','InProgress','Done','Rejected')),
    FOREIGN KEY (PersonaId)         REFERENCES Persona(PersonaId)  ON DELETE SET NULL,
    FOREIGN KEY (PreferredBranchId) REFERENCES Branches(BranchId)  ON DELETE SET NULL,
    FOREIGN KEY (CreatedBookingId)  REFERENCES Bookings(BookingId) ON DELETE SET NULL,
    FOREIGN KEY (ProcessedByUserId) REFERENCES Users(UserId)       ON DELETE SET NULL
);

CREATE INDEX IX_Leads_Status_Created ON Leads(Status, CreatedAt);


-- =============================================================================
-- Notifications — Уведомления (email/sms/in-app)
-- Создаются доменными сервисами; отправляются BackgroundService-ом.
-- =============================================================================
CREATE TABLE Notifications (
    NotificationId      INTEGER PRIMARY KEY AUTOINCREMENT,
    RecipientPersonaId  INTEGER NOT NULL,
    Channel             TEXT    NOT NULL,
    Subject             TEXT    NULL,
    Body                TEXT    NOT NULL,
    RelatedBookingId    INTEGER NULL,
    Status              TEXT    NOT NULL DEFAULT 'Pending',
    CreatedAt           TEXT    NOT NULL DEFAULT (datetime('now')),
    SentAt              TEXT    NULL,
    Error               TEXT    NULL,
    CHECK (Channel IN ('Email','Sms','InApp')),
    CHECK (Status  IN ('Pending','Sent','Failed')),
    FOREIGN KEY (RecipientPersonaId) REFERENCES Persona(PersonaId)  ON DELETE NO ACTION,
    FOREIGN KEY (RelatedBookingId)   REFERENCES Bookings(BookingId) ON DELETE SET NULL
);

CREATE INDEX IX_Notifications_Status_Created ON Notifications(Status, CreatedAt);


-- =============================================================================
-- ConsentLog — Журнал согласий на обработку ПДн (152-ФЗ)
-- =============================================================================
CREATE TABLE ConsentLog (
    ConsentId   INTEGER PRIMARY KEY AUTOINCREMENT,
    PersonaId   INTEGER NOT NULL,
    ConsentType TEXT    NOT NULL,           -- PersonalData|Marketing
    AcceptedAt  TEXT    NOT NULL DEFAULT (datetime('now')),
    IpAddress   TEXT    NULL,
    UserAgent   TEXT    NULL,
    CHECK (ConsentType IN ('PersonalData','Marketing')),
    FOREIGN KEY (PersonaId) REFERENCES Persona(PersonaId) ON DELETE NO ACTION
);


-- =============================================================================
-- AuditLog — Журнал административных действий
-- =============================================================================
CREATE TABLE AuditLog (
    AuditId     INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId      INTEGER NULL,
    Action      TEXT    NOT NULL,           -- RoleChanged|BookingCancelled|...
    EntityType  TEXT    NOT NULL,           -- User|Booking|Master|...
    EntityId    INTEGER NULL,
    Details     TEXT    NULL,               -- JSON с подробностями
    CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now')),
    IpAddress   TEXT    NULL,
    FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE SET NULL
);

CREATE INDEX IX_AuditLog_Created ON AuditLog(CreatedAt);
CREATE INDEX IX_AuditLog_Entity  ON AuditLog(EntityType, EntityId);
