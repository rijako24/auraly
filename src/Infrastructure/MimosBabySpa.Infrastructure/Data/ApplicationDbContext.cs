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
    public DbSet<Reservation> Reservations { get; set; }
    public DbSet<Service> Services { get; set; }
    public DbSet<ServiceBundleItem> ServiceBundleItems { get; set; }
    public DbSet<ServiceAddOnRule> ServiceAddOnRules { get; set; }
    public DbSet<ReservationAddOn> ReservationAddOns { get; set; }
    public DbSet<BusinessResource> BusinessResources { get; set; }
    public DbSet<ServiceResourceUsage> ServiceResourceUsages { get; set; }
    public DbSet<Employee> Employees { get; set; }
    public DbSet<EmployeeService> EmployeeServices { get; set; }
    public DbSet<ConversationStateEntity> ConversationStates { get; set; }

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
            
            // Información de contacto y descripción
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.Address).HasMaxLength(500);
            entity.Property(e => e.Phone).HasMaxLength(50);
            entity.Property(e => e.Email).HasMaxLength(200);
            entity.Property(e => e.Website).HasMaxLength(500);
            
                // Horarios y métodos de pago (JSON)
                entity.Property(e => e.OperatingHoursJson)
                      .HasColumnType("NVARCHAR(MAX)")
                      .HasDefaultValue("{}");
                entity.Property(e => e.PaymentMethodsJson)
                      .HasColumnType("NVARCHAR(MAX)")
                      .HasDefaultValue("[]");

                // Logo del negocio
                entity.Property(e => e.LogoUrl).HasMaxLength(500);

                // Personalidad del asistente (JSON) — columna legada; la carga activa usa BusinessConfiguration key=Personality
                entity.Property(e => e.PersonalityJson)
                      .HasColumnType("NVARCHAR(MAX)")
                      .HasDefaultValue("{}");

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
            entity.Property(e => e.State)
                  .HasConversion<int>() // Convertir enum ConversationState a int
                  .HasDefaultValue(ConversationState.Idle); // Valor por defecto
            entity.HasOne(e => e.Business)
                  .WithMany(b => b.Conversations)
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.BusinessId, e.UserNumber }); // Índice compuesto
            entity.HasIndex(e => e.State); // Índice para búsquedas por estado
            // Relación con ConversationContext
            entity.HasMany(e => e.Contexts)
                  .WithOne(c => c.Conversation)
                  .HasForeignKey(c => c.ConversationId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ConversationContext configuration
        modelBuilder.Entity<ConversationContext>(entity =>
        {
            entity.HasKey(e => e.ConversationContextId);
            entity.Property(e => e.Field).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Value).IsRequired().HasMaxLength(500);
            entity.HasOne(e => e.Conversation)
                  .WithMany(c => c.Contexts)
                  .HasForeignKey(e => e.ConversationId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.ConversationId);
            entity.HasIndex(e => new { e.ConversationId, e.Field }); // Índice compuesto para búsquedas rápidas
        });

        // Message configuration
        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.MessageId);
            entity.Property(e => e.Sender).IsRequired().HasMaxLength(20);
            entity.Property(e => e.MessageText).IsRequired().HasMaxLength(2000);
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

        // Reservation configuration
        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.HasKey(e => e.ReservationId);
            entity.Property(e => e.ServiceId).IsRequired();
            entity.Property(e => e.EmployeeId).IsRequired();
            entity.Property(e => e.ReservationDateTime).IsRequired();
            entity.Property(e => e.DurationMinutes).IsRequired();
            entity.Property(e => e.Status)
                  .HasConversion<int>(); // Convertir enum a int
            entity.Property(e => e.CalendarEventId).HasMaxLength(500);
            entity.HasOne(e => e.Business)
                  .WithMany(b => b.Reservations)
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Service)
                  .WithMany()
                  .HasForeignKey(e => e.ServiceId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Employee)
                  .WithMany(emp => emp.Reservations)
                  .HasForeignKey(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Conversation)
                  .WithMany()
                  .HasForeignKey(e => e.ConversationId)
                  .OnDelete(DeleteBehavior.SetNull); // Si se elimina la conversación, el ConversationId se pone en null
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => e.ServiceId);
            entity.HasIndex(e => e.EmployeeId);
            entity.HasIndex(e => new { e.BusinessId, e.ReservationDateTime });
            entity.HasIndex(e => new { e.EmployeeId, e.ReservationDateTime }); // Índice compuesto para búsquedas de disponibilidad
            entity.HasIndex(e => e.ConversationId); // Índice para búsquedas por conversación
        });

        // BusinessResource configuration
        modelBuilder.Entity<BusinessResource>(entity =>
        {
            entity.HasKey(e => e.BusinessResourceId);
            entity.Property(e => e.ResourceName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Quantity).IsRequired();
            entity.HasOne(e => e.Business)
                  .WithMany()
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => new { e.BusinessId, e.ResourceName }).IsUnique(); // Un recurso único por nombre por negocio
        });

        // Service configuration
        modelBuilder.Entity<Service>(entity =>
        {
            entity.HasKey(e => e.ServiceId);
            entity.Property(e => e.ServiceName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasColumnType("NVARCHAR(MAX)"); // Descripción sin límite para contenido detallado
            entity.Property(e => e.DurationMinutes).IsRequired();
            entity.Property(e => e.Price).IsRequired().HasPrecision(18, 2); // Precisión para valores monetarios
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
            entity.Property(e => e.Category).IsRequired()
                  .HasDefaultValue(Domain.Enums.ServiceCategory.Otro).HasConversion<int>();
            entity.Property(e => e.Tier).IsRequired().HasDefaultValue(Domain.Enums.ServiceTier.Base)
                  .HasConversion<int>();
            entity.Property(e => e.ServiceType).IsRequired()
                  .HasDefaultValue(Domain.Enums.ServiceType.Standard).HasConversion<int>();
            entity.HasOne(e => e.Business)
                  .WithMany()
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => new { e.BusinessId, e.Category });
            entity.HasIndex(e => new { e.BusinessId, e.ServiceName }).IsUnique();
        });

        // ServiceAddOnRule configuration
        modelBuilder.Entity<ServiceAddOnRule>(entity =>
        {
            entity.HasKey(e => e.ServiceAddOnRuleId);
            entity.Property(e => e.DisplayOrder).IsRequired().HasDefaultValue(1);
            entity.HasOne(e => e.Business)
                  .WithMany()
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.AddOnService)
                  .WithMany()
                  .HasForeignKey(e => e.AddOnServiceId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CompatibleService)
                  .WithMany()
                  .HasForeignKey(e => e.CompatibleServiceId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => new { e.BusinessId, e.AddOnServiceId, e.CompatibleServiceId }).IsUnique();
        });

        // ReservationAddOn configuration
        modelBuilder.Entity<ReservationAddOn>(entity =>
        {
            entity.HasKey(e => e.ReservationAddOnId);
            entity.Property(e => e.PriceSnapshot).IsRequired().HasPrecision(18, 2);
            entity.HasOne(e => e.Reservation)
                  .WithMany(r => r.AddOns)
                  .HasForeignKey(e => e.ReservationId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.AddOnService)
                  .WithMany()
                  .HasForeignKey(e => e.AddOnServiceId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.ReservationId);
            entity.HasIndex(e => new { e.ReservationId, e.AddOnServiceId }).IsUnique();
        });

        // ServiceBundleItem configuration
        modelBuilder.Entity<ServiceBundleItem>(entity =>
        {
            entity.HasKey(e => e.ServiceBundleItemId);
            entity.Property(e => e.DisplayOrder).IsRequired().HasDefaultValue(1);
            entity.HasOne(e => e.BundleService)
                  .WithMany(s => s.BundleItems)
                  .HasForeignKey(e => e.BundleServiceId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.IncludedService)
                  .WithMany()
                  .HasForeignKey(e => e.IncludedServiceId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.BundleServiceId, e.IncludedServiceId }).IsUnique();
        });

        // ServiceResourceUsage configuration
        modelBuilder.Entity<ServiceResourceUsage>(entity =>
        {
            entity.HasKey(e => e.ServiceResourceUsageId);
            entity.Property(e => e.Quantity).IsRequired();
            entity.HasOne(e => e.Service)
                  .WithMany(s => s.ResourceUsages)
                  .HasForeignKey(e => e.ServiceId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.BusinessResource)
                  .WithMany()
                  .HasForeignKey(e => e.BusinessResourceId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.ServiceId, e.BusinessResourceId }).IsUnique(); // Un uso único por servicio-recurso
        });

        // Employee configuration
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(e => e.EmployeeId);
            entity.Property(e => e.BusinessId).IsRequired();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.HasOne(e => e.Business)
                  .WithMany()
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => new { e.BusinessId, e.Name }); // Índice para búsquedas por nombre
        });

        // EmployeeService configuration (many-to-many)
        modelBuilder.Entity<EmployeeService>(entity =>
        {
            entity.HasKey(e => e.EmployeeServiceId);
            entity.Property(e => e.EmployeeId).IsRequired();
            entity.Property(e => e.ServiceId).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.HasOne(e => e.Employee)
                  .WithMany(emp => emp.EmployeeServices)
                  .HasForeignKey(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Service)
                  .WithMany()
                  .HasForeignKey(e => e.ServiceId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.EmployeeId);
            entity.HasIndex(e => e.ServiceId);
            entity.HasIndex(e => new { e.EmployeeId, e.ServiceId }).IsUnique(); // Una relación única por par empleado-servicio
        });

        // ConversationState configuration
        modelBuilder.Entity<ConversationStateEntity>(entity =>
        {
            entity.HasKey(e => e.ConversationId);
            entity.Property(e => e.StateJson).IsRequired().HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.Version).IsRequired().HasDefaultValue(1);
            entity.Property(e => e.UpdatedAt).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.HasOne(e => e.Conversation)
                  .WithOne()
                  .HasForeignKey<ConversationStateEntity>(e => e.ConversationId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Business)
                  .WithMany()
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => e.ConversationId).IsUnique();
        });

    }
}
