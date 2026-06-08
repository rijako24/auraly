using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
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
    public DbSet<BusinessAttachment> BusinessAttachments { get; set; }
    public DbSet<BusinessConfiguration> BusinessConfigurations { get; set; }
    public DbSet<SystemConfiguration> SystemConfigurations { get; set; }
    public DbSet<ConversationContext> ConversationContexts { get; set; }
    public DbSet<CustomerMemory> CustomerMemory { get; set; }
    public DbSet<Reservation> Reservations { get; set; }
    public DbSet<Service> Services { get; set; }
    public DbSet<ServiceCategory> ServiceCategories { get; set; }
    public DbSet<ServiceBundleItem> ServiceBundleItems { get; set; }
    public DbSet<ServiceAddOnRule> ServiceAddOnRules { get; set; }
    public DbSet<ReservationAddOn> ReservationAddOns { get; set; }
    public DbSet<BusinessResource> BusinessResources { get; set; }
    public DbSet<ServiceResourceUsage> ServiceResourceUsages { get; set; }
    public DbSet<Employee> Employees { get; set; }
    public DbSet<EmployeeService> EmployeeServices { get; set; }
    public DbSet<ConversationStateEntity> ConversationStates { get; set; }
    public DbSet<PaymentTransaction> PaymentTransactions { get; set; }
    public DbSet<Enrollment> Enrollments { get; set; }

    public DbSet<AppUser> AppUsers { get; set; }
    public DbSet<AppRole> AppRoles { get; set; }
    public DbSet<Permission> Permissions { get; set; }
    public DbSet<UserRole> UserRoles { get; set; }
    public DbSet<RolePermission> RolePermissions { get; set; }
    public DbSet<UserExternalLogin> UserExternalLogins { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }

    // Agentic engine
    public DbSet<AgentType> AgentTypes { get; set; }
    public DbSet<Agent> Agents { get; set; }

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
            entity.Property(e => e.LogoUrl).HasMaxLength(500);

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
            // AgentId: which agent handles conversations on this number (nullable for backwards compat)
            entity.HasOne(e => e.Agent)
                  .WithMany()
                  .HasForeignKey(e => e.AgentId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => e.WhatsAppPhoneNumberId).IsUnique();
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => e.AgentId);
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
            entity.Property(e => e.CustomerName).HasMaxLength(100);
            entity.Property(e => e.CustomerEmail).HasMaxLength(200);
            entity.Property(e => e.Status).HasConversion<byte>().HasDefaultValue(ConversationLifecycleStatus.Active);
            entity.Property(e => e.OpenedAt).IsRequired();
            entity.Property(e => e.LastActivityAt).IsRequired();
            entity.Property(e => e.CloseReason).HasMaxLength(50);
            entity.HasOne(e => e.Business)
                  .WithMany(b => b.Conversations)
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.BusinessId, e.UserNumber });
            entity.HasIndex(e => e.Status);
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
            entity.Property(e => e.Value).IsRequired().HasMaxLength(2000);
            entity.HasOne(e => e.Conversation)
                  .WithMany(c => c.Contexts)
                  .HasForeignKey(e => e.ConversationId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.ConversationId);
            entity.HasIndex(e => new { e.ConversationId, e.Field }).IsUnique();
        });

        // CustomerMemory configuration
        modelBuilder.Entity<CustomerMemory>(entity =>
        {
            entity.HasKey(e => e.CustomerMemoryId);
            entity.Property(e => e.UserNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Field).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Value).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();
            entity.HasOne(e => e.Business)
                  .WithMany()
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.BusinessId, e.UserNumber, e.Field }).IsUnique();
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
            entity.Property(e => e.CustomerEmail).HasMaxLength(200);
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
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.CalendarEventId).HasMaxLength(500);
            entity.Property(e => e.CustomerNameSnapshot).HasMaxLength(100);
            entity.Property(e => e.CustomerEmailSnapshot).HasMaxLength(200);
            entity.Property(e => e.CustomerPhoneSnapshot).HasMaxLength(50);
            entity.Property(e => e.AvailableSlotsCsv).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.CustomAttributesJson).HasColumnType("NVARCHAR(MAX)");
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
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);
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

        // BusinessAttachment configuration
        modelBuilder.Entity<BusinessAttachment>(entity =>
        {
            entity.HasKey(e => e.BusinessAttachmentId);
            entity.Property(e => e.BlobPath).IsRequired().HasMaxLength(500);
            entity.Property(e => e.MediaType).IsRequired().HasMaxLength(50).HasDefaultValue("document");
            entity.Property(e => e.Filename).HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.HasOne(e => e.Business)
                  .WithMany()
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.BusinessId);
        });

        // ServiceCategory configuration
        modelBuilder.Entity<ServiceCategory>(entity =>
        {
            entity.HasKey(e => e.ServiceCategoryId);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.DisplayOrder).IsRequired().HasDefaultValue(0);
            entity.HasOne(e => e.Business)
                  .WithMany()
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => new { e.BusinessId, e.Name }).IsUnique();
        });

        // Service configuration
        modelBuilder.Entity<Service>(entity =>
        {
            entity.HasKey(e => e.ServiceId);
            entity.Property(e => e.ServiceName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.DurationMinutes).IsRequired();
            entity.Property(e => e.Price).IsRequired().HasPrecision(18, 2);
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
            entity.Property(e => e.Tier).IsRequired().HasDefaultValue(Domain.Enums.ServiceTier.Base)
                  .HasConversion<int>();
            entity.Property(e => e.ServiceType).IsRequired()
                  .HasDefaultValue(Domain.Enums.ServiceType.Standard).HasConversion<int>();
            entity.HasOne(e => e.Business)
                  .WithMany()
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ServiceCategory)
                  .WithMany()
                  .HasForeignKey(e => e.CategoryId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => new { e.BusinessId, e.CategoryId });
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
            entity.Property(e => e.Owner).HasConversion<byte>().HasDefaultValue(ConversationOwner.Bot);
            entity.Property(e => e.LastUserMessage).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.LastBotMessage).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.VerificationsJson).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.StageSnapshotsJson).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.CompletedStagesJson).HasColumnType("NVARCHAR(MAX)");
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

        // PaymentTransaction configuration
        modelBuilder.Entity<PaymentTransaction>(entity =>
        {
            entity.HasKey(e => e.PaymentTransactionId);
            entity.Property(e => e.PaymentReferenceId).IsRequired().HasMaxLength(200);
            entity.Property(e => e.ProviderTransactionId).HasMaxLength(200);
            entity.Property(e => e.Currency).IsRequired().HasMaxLength(10);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.Source).HasConversion<int>();
            entity.Property(e => e.Source).HasConversion<int>().HasDefaultValue(Domain.Enums.PaymentTransactionSource.Automated);
            entity.Property(e => e.WebhookPayloadJson).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.CheckoutKind).HasConversion<int>().HasDefaultValue(CheckoutKind.Reservation);
            entity.Property(e => e.CheckoutSnapshotJson).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.QuoteHash).HasMaxLength(128);
            entity.Property(e => e.ConfirmationOutcome).HasMaxLength(100);
            entity.Property(e => e.LinkUrl).HasMaxLength(1000);
            entity.Property(e => e.Snapshot_CustomerName).HasMaxLength(200);
            entity.Property(e => e.Snapshot_CustomerEmail).HasMaxLength(200);
            entity.Property(e => e.Snapshot_CustomerPhone).HasMaxLength(50);
            entity.Property(e => e.Snapshot_AddOnIds).HasMaxLength(500);
            entity.Property(e => e.Snapshot_CustomAttributesJson).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.SupersededAt);
            entity.HasOne<PaymentTransaction>()
                .WithMany()
                .HasForeignKey(e => e.SupersededByPaymentTransactionId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Conversation)
                .WithMany()
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Reservation)
                .WithMany()
                .HasForeignKey(e => e.ReservationId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasOne<Service>()
                .WithMany()
                .HasForeignKey(e => e.Snapshot_ServiceId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasIndex(e => e.PaymentReferenceId).IsUnique();
            entity.HasIndex(e => e.ConversationId);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => e.ReservationId);
        });

        // Enrollment configuration
        modelBuilder.Entity<Enrollment>(entity =>
        {
            entity.HasKey(e => e.EnrollmentId);
            entity.Property(e => e.CustomerName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.CustomerPhone).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CustomerEmail).HasMaxLength(200);
            entity.Property(e => e.FixedScheduleLabel).HasMaxLength(500);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.CustomAttributesJson).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Conversation)
                .WithMany()
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Service)
                .WithMany()
                .HasForeignKey(e => e.ServiceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.PaymentTransaction)
                .WithMany()
                .HasForeignKey(e => e.PaymentTransactionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => e.ConversationId);
            entity.HasIndex(e => e.ServiceId);
            entity.HasIndex(e => e.PaymentTransactionId).IsUnique();
        });

        // AppUser configuration
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
            entity.Property(e => e.NormalizedUsername).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
            entity.Property(e => e.NormalizedEmail).IsRequired().HasMaxLength(256);
            entity.Property(e => e.PasswordHash).HasMaxLength(500);
            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PhoneNumber).HasMaxLength(20);
            entity.Property(e => e.AvatarUrl).HasMaxLength(500);
            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.AppUsers)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.NormalizedUsername).IsUnique();
            entity.HasIndex(e => e.NormalizedEmail).IsUnique();
            entity.HasIndex(e => e.TenantId);
        });

        // AppRole configuration
        modelBuilder.Entity<AppRole>(entity =>
        {
            entity.HasKey(e => e.RoleId);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.NormalizedName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.HasOne(e => e.Tenant)
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.TenantId, e.NormalizedName }).IsUnique();
        });

        // Permission configuration
        modelBuilder.Entity<Permission>(entity =>
        {
            entity.HasKey(e => e.PermissionId);
            entity.Property(e => e.Module).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Action).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Resource).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.HasIndex(e => e.Resource).IsUnique();
        });

        // UserRole configuration
        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(e => e.UserRoleId);
            entity.HasOne(e => e.User)
                .WithMany(u => u.UserRoles)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Role)
                .WithMany(r => r.UserRoles)
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.UserId, e.RoleId, e.BusinessId }).IsUnique().HasFilter("BusinessId IS NOT NULL");
            entity.HasIndex(e => new { e.UserId, e.RoleId }).IsUnique().HasFilter("BusinessId IS NULL");
        });

        // RolePermission configuration
        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.HasKey(e => e.RolePermissionId);
            entity.HasOne(e => e.Role)
                .WithMany(r => r.RolePermissions)
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Permission)
                .WithMany(p => p.RolePermissions)
                .HasForeignKey(e => e.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.RoleId, e.PermissionId }).IsUnique();
        });

        // UserExternalLogin configuration
        modelBuilder.Entity<UserExternalLogin>(entity =>
        {
            entity.HasKey(e => e.ExternalLoginId);
            entity.Property(e => e.Provider).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ProviderKey).IsRequired().HasMaxLength(256);
            entity.Property(e => e.ProviderDisplayName).HasMaxLength(200);
            entity.Property(e => e.ProviderEmail).HasMaxLength(256);
            entity.HasOne(e => e.User)
                .WithMany(u => u.ExternalLogins)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.Provider, e.ProviderKey }).IsUnique();
        });

        // RefreshToken configuration
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.RefreshTokenId);
            entity.Property(e => e.Token).IsRequired().HasMaxLength(500);
            entity.Property(e => e.DeviceInfo).HasMaxLength(500);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.HasOne(e => e.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ExpiresAt);
        });

        // AuditLog configuration
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.AuditLogId);
            entity.Property(e => e.Action).IsRequired().HasMaxLength(100);
            entity.Property(e => e.EntityType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.EntityId).HasMaxLength(100);
            entity.Property(e => e.OldValues).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.NewValues).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.CorrelationId).HasMaxLength(100);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Tenant)
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => new { e.TenantId, e.Timestamp });
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CorrelationId);
        });

        // ── Generic Flow Engine ─────────────────────────────────────────────────────

        modelBuilder.Entity<AgentType>(entity =>
        {
            entity.HasKey(e => e.AgentTypeId);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<Agent>(entity =>
        {
            entity.HasKey(e => e.AgentId);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.SettingsJson).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.SystemPromptMarkdown).HasColumnType("NVARCHAR(MAX)");
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.AgentType)
                .WithMany(at => at.Agents)
                .HasForeignKey(e => e.AgentTypeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => new { e.BusinessId, e.Name }).IsUnique();
        });

    }
}
