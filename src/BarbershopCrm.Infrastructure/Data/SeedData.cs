using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Infrastructure.Data;

/// <summary>
/// Базовый сидинг справочников через <see cref="ModelBuilder.Entity{TEntity}"/>.HasData.
/// Здесь только то, что должно существовать в любой среде (Roles, Branches, Services).
/// Тестовые пользователи/мастера/бронирования сидятся отдельным <c>DataSeeder</c>-сервисом
/// при старте приложения в Development-режиме.
/// </summary>
internal static class SeedData
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>().HasData(
            new Role { RoleId = 1, Code = RoleCode.Owner,  Name = "Владелец сети" },
            new Role { RoleId = 2, Code = RoleCode.Admin,  Name = "Администратор филиала" },
            new Role { RoleId = 3, Code = RoleCode.Master, Name = "Мастер" },
            new Role { RoleId = 4, Code = RoleCode.Client, Name = "Клиент" }
        );

        modelBuilder.Entity<Branch>().HasData(
            new Branch
            {
                BranchId = 1,
                Name = "Тихий час — Центр",
                Address = "Краснодар, ул. Красная, 32",
                Phone = "+7 (861) 200-10-10",
                OpeningTime = new TimeOnly(10, 0),
                ClosingTime = new TimeOnly(22, 0),
                IsActive = true
            },
            new Branch
            {
                BranchId = 2,
                Name = "Тихий час — Фестивальный",
                Address = "Краснодар, ул. Тургенева, 138",
                Phone = "+7 (861) 200-10-11",
                OpeningTime = new TimeOnly(9, 0),
                ClosingTime = new TimeOnly(21, 0),
                IsActive = true
            }
        );

        modelBuilder.Entity<Service>().HasData(
            new Service
            {
                ServiceId = 1,
                Name = "Мужская стрижка",
                Description = "Классическая мужская стрижка ножницами и машинкой.",
                DurationMinutes = 60,
                Price = 1500m,
                IsActive = true
            },
            new Service
            {
                ServiceId = 2,
                Name = "Стрижка машинкой",
                Description = "Короткая стрижка одной длиной.",
                DurationMinutes = 30,
                Price = 800m,
                IsActive = true
            },
            new Service
            {
                ServiceId = 3,
                Name = "Бритьё опасной бритвой",
                Description = "Классическое бритьё с горячим полотенцем.",
                DurationMinutes = 45,
                Price = 1200m,
                IsActive = true
            },
            new Service
            {
                ServiceId = 4,
                Name = "Стрижка бороды",
                Description = "Моделирование контура и подравнивание бороды.",
                DurationMinutes = 30,
                Price = 700m,
                IsActive = true
            },
            new Service
            {
                ServiceId = 5,
                Name = "Камуфляж бороды",
                Description = "Тонирование седины в бороде.",
                DurationMinutes = 30,
                Price = 900m,
                IsActive = true
            }
        );
    }
}
