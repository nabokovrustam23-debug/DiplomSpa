using BarbershopCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarbershopCrm.Infrastructure.Data.Configurations;

public sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> b)
    {
        b.ToTable("Services", t =>
        {
            t.HasCheckConstraint("CK_Services_Duration", "DurationMinutes > 0");
            t.HasCheckConstraint("CK_Services_Price", "Price >= 0");
            t.HasCheckConstraint("CK_Services_IsActive", "IsActive IN (0,1)");
        });

        b.HasKey(x => x.ServiceId);
        b.Property(x => x.Name).IsRequired();
        b.Property(x => x.Price).HasColumnType("NUMERIC");
        b.Property(x => x.IsActive).HasDefaultValue(true);
    }
}

public sealed class MasterConfiguration : IEntityTypeConfiguration<Master>
{
    public void Configure(EntityTypeBuilder<Master> b)
    {
        b.ToTable("Masters", t =>
            t.HasCheckConstraint("CK_Masters_IsActive", "IsActive IN (0,1)"));

        b.HasKey(x => x.MasterId);
        b.Property(x => x.Position).IsRequired();
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.HasIndex(x => x.PersonaId).IsUnique();
        b.HasIndex(x => x.BranchId).HasDatabaseName("IX_Masters_Branch");

        b.HasOne(x => x.Persona)
            .WithOne(p => p.Master!)
            .HasForeignKey<Master>(x => x.PersonaId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Branch)
            .WithMany(br => br.Masters)
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MasterServiceConfiguration : IEntityTypeConfiguration<MasterService>
{
    public void Configure(EntityTypeBuilder<MasterService> b)
    {
        b.ToTable("MasterService");
        b.HasKey(x => new { x.MasterId, x.ServiceId });

        b.HasOne(x => x.Master)
            .WithMany(m => m.MasterServices)
            .HasForeignKey(x => x.MasterId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Service)
            .WithMany(s => s.MasterServices)
            .HasForeignKey(x => x.ServiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> b)
    {
        b.ToTable("Clients");
        b.HasKey(x => x.ClientId);
        b.Property(x => x.CreatedAt).HasDefaultValueSql("(datetime('now'))");
        b.HasIndex(x => x.PersonaId).IsUnique();

        b.HasOne(x => x.Persona)
            .WithOne(p => p.Client!)
            .HasForeignKey<Client>(x => x.PersonaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
