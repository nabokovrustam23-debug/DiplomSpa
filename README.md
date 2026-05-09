# DiplomSpa — CRM «Тихий час»

Дипломный проект: CRM-система сети барбершопов «Тихий час» (Краснодар).

## Стек

- ASP.NET Core 8 (Razor Pages)
- Entity Framework Core 8 + SQLite
- Собственная авторизация на PBKDF2-HMAC-SHA256 (без ASP.NET Core Identity)
- Serilog (консоль + файл с ротацией)
- xUnit + FluentAssertions

## Структура решения

```
src/
  BarbershopCrm.Domain/          сущности, перечисления (POCOs)
  BarbershopCrm.Infrastructure/  EF Core DbContext, конфигурации, миграции, PasswordHasher
  BarbershopCrm.Web/             Razor Pages, Program.cs, _Layout, palette CSS
tests/
  BarbershopCrm.Tests/           xUnit, тесты PasswordHasher
docs/
  analysis.md                    проектная документация (модули, user-story, матрица прав, дизайн)
  schema.sql                     справочная схема БД (SQLite)
  moodboard.html                 интерактивный мудборд палитр
```

## Запуск

```bash
dotnet restore
dotnet run --project src/BarbershopCrm.Web
```

В Development-режиме приложение само применяет миграции и создаёт `barbershop.db` в каталоге запуска. Слушает `http://localhost:5158`.

## Полезные команды

```bash
# тесты
dotnet test

# создать миграцию
dotnet ef migrations add <Name> \
  --project src/BarbershopCrm.Infrastructure \
  --startup-project src/BarbershopCrm.Web

# применить миграции в Production
dotnet ef database update \
  --project src/BarbershopCrm.Infrastructure \
  --startup-project src/BarbershopCrm.Web
```

## Что делает каркас

- Структура решения и проектные ссылки.
- Все 17 сущностей из `docs/schema.sql` с EF Core Fluent-конфигурациями (включая частичный уникальный индекс `UX_Bookings_ActiveSlot` для защиты от двойного бронирования).
- Миграция `InitialCreate` со сидом справочников: 4 роли, 2 филиала в Краснодаре, 5 услуг.
- `Pbkdf2PasswordHasher` (HMAC-SHA256, соль 16 байт, 100 000 итераций, выход 32 байта) + 5 xUnit-тестов.
- Serilog: консоль + `logs/app-*.log` с ежедневной ротацией и хранением 14 дней.
- `_Layout.cshtml` с переключателем тем (cookie `theme`, рендер `<html data-theme="...">`), палитра «Латунь на тёплом угле» / «Крем + латунь», шрифты Cormorant Garamond + Inter.
- Главная страница с разделами «Филиалы» и «Услуги», подтягивающая данные из БД.

## Что НЕ входит в каркас

Бизнес-логика модулей M1–M6 (вход, регистрация, подтверждение email, сброс пароля, расписание, онлайн-запись, лиды, отчёты, уведомления) — добавляется в отдельных PR согласно `docs/analysis.md`.
