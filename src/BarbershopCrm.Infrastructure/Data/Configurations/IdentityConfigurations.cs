using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarbershopCrm.Infrastructure.Data.Configurations;

public sealed class PersonaConfiguration : IEntityTypeConfiguration<Persona>
{
    public void Configure(EntityTypeBuilder<Persona> b)
    {
        b.ToTable("Persona", t =>
            t.HasCheckConstraint("CK_Persona_Gender", "Gender IS NULL OR Gender IN ('М','Ж')"));

        b.HasKey(x => x.PersonaId);
        b.Property(x => x.LastName).IsRequired();
        b.Property(x => x.FirstName).IsRequired();
        b.Property(x => x.Phone).IsRequired();
        b.HasIndex(x => x.Phone).IsUnique().HasDatabaseName("UX_Persona_Phone");
    }
}

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("Roles");
        b.HasKey(x => x.RoleId);
        b.Property(x => x.Code).IsRequired();
        b.Property(x => x.Name).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
    }
}

public sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> b)
    {
        b.ToTable("Branches", t =>
        {
            t.HasCheckConstraint("CK_Branches_Hours", "ClosingTime > OpeningTime");
            t.HasCheckConstraint("CK_Branches_IsActive", "IsActive IN (0,1)");
        });
        b.HasKey(x => x.BranchId);
        b.Property(x => x.Name).IsRequired();
        b.Property(x => x.Address).IsRequired();
        b.Property(x => x.IsActive).HasDefaultValue(true);
    }
}

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users", t =>
        {
            t.HasCheckConstraint("CK_Users_IsActive", "IsActive IN (0,1)");
            t.HasCheckConstraint("CK_Users_IsEmailConfirmed", "IsEmailConfirmed IN (0,1)");
        });

        b.HasKey(x => x.UserId);
        b.Property(x => x.Login).IsRequired();
        b.Property(x => x.PasswordHash).IsRequired();
        b.Property(x => x.PasswordSalt).IsRequired();
        b.Property(x => x.PasswordIterations).HasDefaultValue(100_000);
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.Property(x => x.CreatedAt).HasDefaultValueSql("(datetime('now'))");

        b.HasIndex(x => x.Login).IsUnique();
        b.HasIndex(x => x.PersonaId).IsUnique();
        b.HasIndex(x => x.BranchId).HasDatabaseName("IX_Users_Branch");

        b.HasOne(x => x.Persona)
            .WithOne(p => p.User!)
            .HasForeignKey<User>(x => x.PersonaId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Role)
            .WithMany(r => r.Users)
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Branch)
            .WithMany(br => br.Admins)
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> b)
    {
        b.ToTable("UserSessions");
        b.HasKey(x => x.SessionId);
        b.Property(x => x.Token).IsRequired();
        b.Property(x => x.CreatedAt).HasDefaultValueSql("(datetime('now'))");
        b.HasIndex(x => x.Token).IsUnique();
        b.HasIndex(x => x.UserId).HasDatabaseName("IX_UserSessions_User");

        b.HasOne(x => x.User)
            .WithMany(u => u.Sessions)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class UserTokenConfiguration : IEntityTypeConfiguration<UserToken>
{
    public void Configure(EntityTypeBuilder<UserToken> b)
    {
        b.ToTable("UserTokens", t =>
            t.HasCheckConstraint("CK_UserTokens_Purpose",
                $"Purpose IN ('{UserTokenPurpose.EmailVerification}','{UserTokenPurpose.PasswordReset}')"));

        b.HasKey(x => x.TokenId);
        b.Property(x => x.Purpose).IsRequired();
        b.Property(x => x.Token).IsRequired();
        b.Property(x => x.CreatedAt).HasDefaultValueSql("(datetime('now'))");

        b.HasIndex(x => x.Token).IsUnique();
        b.HasIndex(x => new { x.UserId, x.Purpose })
            .HasDatabaseName("IX_UserTokens_User_Purpose");

        b.HasOne(x => x.User)
            .WithMany(u => u.Tokens)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
