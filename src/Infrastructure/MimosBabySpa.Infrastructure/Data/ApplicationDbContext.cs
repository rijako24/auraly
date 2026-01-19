using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Conversation> Conversations { get; set; }
    public DbSet<Message> Messages { get; set; }
    public DbSet<Lead> Leads { get; set; }
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<Business> Businesses { get; set; }
    public DbSet<BusinessWhatsAppNumber> BusinessWhatsAppNumbers { get; set; }
    public DbSet<BusinessConfiguration> BusinessConfigurations { get; set; }
    public DbSet<SystemConfiguration> SystemConfigurations { get; set; }
    public DbSet<ConversationContext> ConversationContexts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tenant configuration
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(e => e.TenantId);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => e.Email).IsUnique();
        });

        // Business configuration
        modelBuilder.Entity<Business>(entity =>
        {
            entity.HasKey(e => e.BusinessId);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.HasOne(e => e.Tenant)
                  .WithMany(t => t.Businesses)
                  .HasForeignKey(e => e.TenantId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // BusinessWhatsAppNumber configuration
        modelBuilder.Entity<BusinessWhatsAppNumber>(entity =>
        {
            entity.HasKey(e => e.BusinessWhatsAppNumberId);
            entity.Property(e => e.PhoneNumber).IsRequired().HasMaxLength(20);
            entity.Property(e => e.WhatsAppPhoneNumberId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.WhatsAppAccessToken).IsRequired().HasMaxLength(500);
            entity.HasOne(e => e.Business)
                  .WithMany(b => b.WhatsAppNumbers)
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.WhatsAppPhoneNumberId).IsUnique();
            entity.HasIndex(e => e.BusinessId);
        });

        // BusinessConfiguration configuration
        modelBuilder.Entity<BusinessConfiguration>(entity =>
        {
            entity.HasKey(e => e.BusinessConfigurationId);
            entity.Property(e => e.Key)
                  .HasConversion<int>(); // Convertir enum a int para almacenar en BD
            entity.Property(e => e.Value).IsRequired().HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.HasOne(e => e.Business)
                  .WithMany(b => b.Configurations)
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Cascade);
            // Índice único compuesto: un negocio no puede tener dos configuraciones con la misma clave
            entity.HasIndex(e => new { e.BusinessId, e.Key }).IsUnique();
            entity.HasIndex(e => e.BusinessId);
        });

        // SystemConfiguration configuration
        modelBuilder.Entity<SystemConfiguration>(entity =>
        {
            entity.HasKey(e => e.SystemConfigurationId);
            entity.Property(e => e.SystemConfigurationId)
                  .HasConversion<int>(); // Convertir enum a int para almacenar en BD
            entity.Property(e => e.Value).IsRequired().HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.Description).HasMaxLength(500);
        });

        // Conversation configuration
        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(e => e.ConversationId);
            entity.Property(e => e.UserNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.LastMessage).HasMaxLength(1000);
            entity.Property(e => e.LastIntent).HasMaxLength(50);
            entity.Property(e => e.CustomerName).HasMaxLength(100);
            entity.Property(e => e.RecommendedPlan).HasMaxLength(100);
            entity.HasOne(e => e.Business)
                  .WithMany(b => b.Conversations)
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.BusinessId, e.UserNumber }); // Índice compuesto
            // Relación con ConversationContext
            entity.HasMany(e => e.Contexts)
                  .WithOne(c => c.Conversation)
                  .HasForeignKey(c => c.ConversationId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Message configuration
        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.MessageId);
            entity.Property(e => e.Sender).IsRequired().HasMaxLength(20);
            entity.Property(e => e.MessageText).IsRequired().HasMaxLength(2000);
            entity.Property(e => e.Intent).IsRequired().HasMaxLength(50);
            entity.HasOne(e => e.Conversation)
                  .WithMany(c => c.Messages)
                  .HasForeignKey(e => e.ConversationId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.ConversationId);
        });

        // Lead configuration
        modelBuilder.Entity<Lead>(entity =>
        {
            entity.HasKey(e => e.LeadId);
            entity.Property(e => e.UserNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.CustomerName).HasMaxLength(100);
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.HasOne(e => e.Business)
                  .WithMany(b => b.Leads)
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.BusinessId, e.UserNumber }); // Índice compuesto
        });

        // ConversationContext configuration
        modelBuilder.Entity<ConversationContext>(entity =>
        {
            entity.HasKey(e => e.ConversationContextId);
            entity.Property(e => e.Context).IsRequired().HasColumnType("NVARCHAR(MAX)");
            entity.HasOne(e => e.Conversation)
                  .WithMany(c => c.Contexts)
                  .HasForeignKey(e => e.ConversationId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.ConversationId);
        });
    }
}
