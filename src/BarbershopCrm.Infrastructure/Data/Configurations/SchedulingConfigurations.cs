using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarbershopCrm.Infrastructure.Data.Configurations;

public sealed class WorkScheduleConfiguration : IEntityTypeConfiguration<WorkSchedule>
{
    public void Configure(EntityTypeBuilder<WorkSchedule> b)
    {
        b.ToTable("WorkSchedules", t =>
        {
            t.HasCheckConstraint("CK_WorkSchedules_Times", "EndTime > StartTime");
            t.HasCheckConstraint(
                "CK_WorkSchedules_Type",
                $"ScheduleType IN ('{ScheduleType.Work}','{ScheduleType.Lunch}','{ScheduleType.DayOff}','{ScheduleType.Vacation}','{ScheduleType.SickLeave}')");
        });

        b.HasKey(x => x.WorkScheduleId);
        b.Property(x => x.ScheduleType).IsRequired();

        b.HasIndex(x => new { x.MasterId, x.WorkDate, x.StartTime })
            .IsUnique()
            .HasDatabaseName("UQ_WorkSchedules_Slot");

        b.HasIndex(x => new { x.MasterId, x.WorkDate })
            .HasDatabaseName("IX_WorkSchedules_Master_Date");

        b.HasOne(x => x.Master)
            .WithMany(m => m.Schedules)
            .HasForeignKey(x => x.MasterId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> b)
    {
        b.ToTable("Bookings", t =>
        {
            t.HasCheckConstraint("CK_Bookings_Duration", "DurationMinutes > 0");
            t.HasCheckConstraint("CK_Bookings_Price", "PriceSnapshot >= 0");
            t.HasCheckConstraint(
                "CK_Bookings_Status",
                $"Status IN ('{BookingStatus.Created}','{BookingStatus.Confirmed}','{BookingStatus.Cancelled}','{BookingStatus.Completed}','{BookingStatus.NoShow}')");
            t.HasCheckConstraint(
                "CK_Bookings_Source",
                $"Source IN ('{BookingSource.Online}','{BookingSource.Admin}','{BookingSource.Lead}')");
        });

        b.HasKey(x => x.BookingId);
        b.Property(x => x.PriceSnapshot).HasColumnType("NUMERIC");
        b.Property(x => x.Status).IsRequired().HasDefaultValue(BookingStatus.Created);
        b.Property(x => x.Source).IsRequired().HasDefaultValue(BookingSource.Online);
        b.Property(x => x.CreatedAt).HasDefaultValueSql("(datetime('now'))");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("(datetime('now'))");

        // Частичный уникальный индекс — защита от двойного бронирования слота.
        b.HasIndex(x => new { x.MasterId, x.StartDateTime })
            .IsUnique()
            .HasFilter($"Status IN ('{BookingStatus.Created}','{BookingStatus.Confirmed}')")
            .HasDatabaseName("UX_Bookings_ActiveSlot");

        b.HasIndex(x => new { x.MasterId, x.StartDateTime })
            .HasDatabaseName("IX_Bookings_Master_Start");
        b.HasIndex(x => x.ClientId).HasDatabaseName("IX_Bookings_Client");
        b.HasIndex(x => new { x.BranchId, x.StartDateTime })
            .HasDatabaseName("IX_Bookings_Branch_Start");

        b.HasOne(x => x.Client)
            .WithMany(c => c.Bookings)
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Master)
            .WithMany()
            .HasForeignKey(x => x.MasterId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Service)
            .WithMany()
            .HasForeignKey(x => x.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class VisitConfiguration : IEntityTypeConfiguration<Visit>
{
    public void Configure(EntityTypeBuilder<Visit> b)
    {
        b.ToTable("Visits", t =>
            t.HasCheckConstraint("CK_Visits_Total", "TotalAmount >= 0"));

        b.HasKey(x => x.VisitId);
        b.Property(x => x.TotalAmount).HasColumnType("NUMERIC");
        b.Property(x => x.CompletedAt).HasDefaultValueSql("(datetime('now'))");
        b.HasIndex(x => x.BookingId).IsUnique();

        b.HasOne(x => x.Booking)
            .WithOne(bk => bk.Visit!)
            .HasForeignKey<Visit>(x => x.BookingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
