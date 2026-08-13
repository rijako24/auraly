using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Infrastructure.Data.ReadModels;
namespace MimosBabySpa.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Conversation> Conversations { get; set; }
    public DbSet<Message> Messages { get; set; }
    public DbSet<Lead> Leads { get; set; }
    public DbSet<Campaign> Campaigns { get; set; }
    public DbSet<CampaignRecipient> CampaignRecipients { get; set; }
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<Business> Businesses { get; set; }
    public DbSet<BusinessWhatsAppNumber> BusinessWhatsAppNumbers { get; set; }
    public DbSet<BusinessAttachment> BusinessAttachments { get; set; }
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
    public DbSet<BusinessWorkingHour> BusinessWorkingHours { get; set; }
    public DbSet<EmployeeWorkingHour> EmployeeWorkingHours { get; set; }
    public DbSet<EmployeeScheduleException> EmployeeScheduleExceptions { get; set; }
    public DbSet<BusinessSchedulingSettings> BusinessSchedulingSettings { get; set; }
    public DbSet<BusinessAvailabilityBlock> BusinessAvailabilityBlocks { get; set; }
    public DbSet<ScheduledAutomationJob> ScheduledAutomationJobs { get; set; }
    public DbSet<ReservationAttendanceResponse> ReservationAttendanceResponses { get; set; }
    public DbSet<IntegrationConnection> IntegrationConnections { get; set; }
    public DbSet<IntegrationChannelWarehouse> IntegrationChannelWarehouses { get; set; }
    public DbSet<ExternalCommerceCustomer> ExternalCommerceCustomers { get; set; }
    public DbSet<ExternalCustomerReconciliationOutboxMessage> ExternalCustomerReconciliationOutboxMessages { get; set; }
    public DbSet<ProductCategory> ProductCategories { get; set; }
    public DbSet<ReservationIntegrationEvent> ReservationIntegrationEvents { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<PublishedProductPriceRow> PublishedProductPrices { get; set; }
    public DbSet<ProductOffer> ProductOffers { get; set; }
    public DbSet<ProductImage> ProductImages { get; set; }
    public DbSet<ProductLink> ProductLinks { get; set; }
    public DbSet<ProductAlias> ProductAliases { get; set; }
    public DbSet<ProductSearchTerm> ProductSearchTerms { get; set; }
    public DbSet<CartMutationReceipt> CartMutationReceipts { get; set; }
    public DbSet<ProductRecommendationRule> ProductRecommendationRules { get; set; }
    public DbSet<Promotion> Promotions { get; set; }
    public DbSet<PromotionCondition> PromotionConditions { get; set; }
    public DbSet<PromotionBenefit> PromotionBenefits { get; set; }
    public DbSet<PromotionApplication> PromotionApplications { get; set; }
    public DbSet<OrderDraft> OrderDrafts { get; set; }
    public DbSet<OrderDraftItem> OrderDraftItems { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<OrderConnectionEvent> OrderConnectionEvents { get; set; }
    public DbSet<ExternalEscalationAttempt> ExternalEscalationAttempts { get; set; }
    public DbSet<ExternalEscalationOutcomeDelivery> ExternalEscalationOutcomeDeliveries { get; set; }
    public DbSet<ConversationStateEntity> ConversationStates { get; set; }
    public DbSet<PaymentTransaction> PaymentTransactions { get; set; }
    public DbSet<Enrollment> Enrollments { get; set; }
    public DbSet<InboundMessageReceipt> InboundMessageReceipts { get; set; }

    public DbSet<AppUser> AppUsers { get; set; }
    public DbSet<AppRole> AppRoles { get; set; }
    public DbSet<Permission> Permissions { get; set; }
    public DbSet<UserRole> UserRoles { get; set; }
    public DbSet<RolePermission> RolePermissions { get; set; }
    public DbSet<UserExternalLogin> UserExternalLogins { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }
    public DbSet<BusinessSubscription> BusinessSubscriptions { get; set; }
    public DbSet<BusinessUsagePeriod> BusinessUsagePeriods { get; set; }
    public DbSet<UsageLedgerEntry> UsageLedgerEntries { get; set; }
    public DbSet<UsageCostRate> UsageCostRates { get; set; }

    // Agentic engine
    public DbSet<AgentType> AgentTypes { get; set; }
    public DbSet<Agent> Agents { get; set; }
    public DbSet<AgentTemplate> AgentTemplates { get; set; }
    public DbSet<BusinessInboundContact> BusinessInboundContacts { get; set; }

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

            // InformaciÃ³n de contacto y descripciÃ³n
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.Address).HasMaxLength(500);
            entity.Property(e => e.Phone).HasMaxLength(50);
            entity.Property(e => e.Email).HasMaxLength(200);
            entity.Property(e => e.Website).HasMaxLength(500);
            entity.Property(e => e.LogoUrl).HasMaxLength(500);
            entity.Property(e => e.TimeZone).IsRequired().HasMaxLength(100);

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
            entity.Property(e => e.WhatsAppBusinessAccountId).HasMaxLength(100);
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
            entity.Property(e => e.CurrentStageName).HasMaxLength(100);
            entity.Property(e => e.Status).HasConversion<byte>().HasDefaultValue(ConversationLifecycleStatus.Active);
            entity.Property(e => e.OpenedAt).IsRequired();
            entity.Property(e => e.LastActivityAt).IsRequired();
            entity.Property(e => e.CloseReason).HasMaxLength(50);
            entity.HasOne(e => e.Business)
                  .WithMany(b => b.Conversations)
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.BusinessId, e.UserNumber });
            entity.HasIndex(e => e.AgentId);
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
            entity.Property(e => e.Value).IsRequired().HasColumnType("nvarchar(max)");
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
            entity.Property(e => e.MessageText).IsRequired().HasColumnType("nvarchar(max)");
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
            entity.Property(e => e.QualificationBand).HasMaxLength(50);
            entity.Property(e => e.QualificationLabel).HasMaxLength(160);
            entity.Property(e => e.QualificationFlowId).HasMaxLength(100);
            entity.Property(e => e.QualificationStageId).HasMaxLength(100);            entity.HasOne(e => e.Business)
                  .WithMany(b => b.Leads)
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.BusinessId, e.UserNumber }); // Ãndice compuesto
        });

        modelBuilder.Entity<Campaign>(entity =>
        {
            entity.HasKey(e => e.CampaignId);
            entity.Property(e => e.Name).HasMaxLength(160).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.Property(e => e.SourceType).HasMaxLength(30).IsRequired();
            entity.Property(e => e.TemplateName).HasMaxLength(120).IsRequired();
            entity.Property(e => e.LanguageCode).HasMaxLength(20).IsRequired();
            entity.Property(e => e.TemplateCategory).HasMaxLength(30).IsRequired();
            entity.Property(e => e.FiltersJson).HasColumnType("nvarchar(max)");
            entity.Property(e => e.ParameterMappingJson).HasColumnType("nvarchar(max)");
            entity.HasOne(e => e.Business)
                  .WithMany()
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CreatedByUser)
                  .WithMany()
                  .HasForeignKey(e => e.CreatedByUserId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.Recipients)
                  .WithOne(e => e.Campaign)
                  .HasForeignKey(e => e.CampaignId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.BusinessId, e.CreatedAt });
        });

        modelBuilder.Entity<CampaignRecipient>(entity =>
        {
            entity.HasKey(e => e.CampaignRecipientId);
            entity.Property(e => e.PhoneNormalized).HasMaxLength(50).IsRequired();
            entity.Property(e => e.CustomerName).HasMaxLength(160);
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.Property(e => e.WhatsAppMessageId).HasMaxLength(160);
            entity.Property(e => e.Error).HasMaxLength(1000);
            entity.Property(e => e.VariablesJson).HasColumnType("nvarchar(max)");
            entity.Property(e => e.AttemptCount).IsRequired();
            entity.HasOne(e => e.Business)
                  .WithMany()
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SourceLead)
                  .WithMany()
                  .HasForeignKey(e => e.SourceLeadId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SourceReservation)
                  .WithMany()
                  .HasForeignKey(e => e.SourceReservationId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.CampaignId, e.PhoneNormalized }).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.Status });
        });
        // Reservation configuration
        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.HasKey(e => e.ReservationId);
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
                  .OnDelete(DeleteBehavior.SetNull); // Si se elimina la conversaciÃ³n, el ConversationId se pone en null
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => e.ServiceId);
            entity.HasIndex(e => e.EmployeeId);
            entity.HasIndex(e => new { e.BusinessId, e.ReservationDateTime });
            entity.HasIndex(e => new { e.EmployeeId, e.ReservationDateTime }); // Ãndice compuesto para bÃºsquedas de disponibilidad
            entity.HasIndex(e => e.ConversationId); // Ãndice para bÃºsquedas por conversaciÃ³n
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
            entity.HasIndex(e => new { e.BusinessId, e.ResourceName }).IsUnique(); // Un recurso Ãºnico por nombre por negocio
        });

        modelBuilder.Entity<BusinessWorkingHour>(entity =>
        {
            entity.HasKey(e => e.BusinessWorkingHourId);
            entity.Property(e => e.DayOfWeek).HasConversion<int>();
            entity.Property(e => e.OpenTime).IsRequired();
            entity.Property(e => e.CloseTime).IsRequired();
            entity.HasOne(e => e.Business)
                  .WithMany()
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.BusinessId, e.DayOfWeek, e.OpenTime });
        });

        modelBuilder.Entity<EmployeeWorkingHour>(entity =>
        {
            entity.HasKey(e => e.EmployeeWorkingHourId);
            entity.Property(e => e.DayOfWeek).HasConversion<int>();
            entity.Property(e => e.OpenTime).IsRequired();
            entity.Property(e => e.CloseTime).IsRequired();
            entity.HasOne(e => e.Business)
                  .WithMany()
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Employee)
                  .WithMany()
                  .HasForeignKey(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.BusinessId, e.EmployeeId, e.DayOfWeek, e.OpenTime });
            entity.HasIndex(e => e.EmployeeId);
        });

        modelBuilder.Entity<EmployeeScheduleException>(entity =>
        {
            entity.HasKey(e => e.EmployeeScheduleExceptionId);
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.HasOne(e => e.Business)
                  .WithMany()
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Employee)
                  .WithMany()
                  .HasForeignKey(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.BusinessId, e.EmployeeId, e.Date });
        });

        modelBuilder.Entity<BusinessSchedulingSettings>(entity =>
        {
            entity.HasKey(e => e.BusinessSchedulingSettingsId);
            entity.Property(e => e.EmployeeStrategy).IsRequired().HasMaxLength(50);
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.BusinessId).IsUnique();
        });

        modelBuilder.Entity<BusinessAvailabilityBlock>(entity =>
        {
            entity.HasKey(e => e.BusinessAvailabilityBlockId);
            entity.Property(e => e.Date).HasColumnType("date");
            entity.Property(e => e.StartTime).HasColumnType("time");
            entity.Property(e => e.EndTime).HasColumnType("time");
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.Source).IsRequired().HasMaxLength(50).HasDefaultValue("operations");
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Employee)
                .WithMany()
                .HasForeignKey(e => e.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasIndex(e => new { e.BusinessId, e.Date });
            entity.HasIndex(e => new { e.BusinessId, e.EmployeeId, e.Date });
        });
        modelBuilder.Entity<ScheduledAutomationJob>(entity =>
        {
            entity.HasKey(e => e.ScheduledAutomationJobId);
            entity.Property(e => e.JobType).HasConversion<int>();
            entity.Property(e => e.DeduplicationKey).IsRequired().HasMaxLength(300);
            entity.Property(e => e.PayloadJson).IsRequired().HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.WhatsAppMessageId).HasMaxLength(200);
            entity.Property(e => e.LastError).HasMaxLength(4000);
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Reservation)
                .WithMany()
                .HasForeignKey(e => e.ReservationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Agent)
                .WithMany()
                .HasForeignKey(e => e.AgentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.DeduplicationKey).IsUnique();
            entity.HasIndex(e => new { e.Status, e.ScheduledAtUtc });
            entity.HasIndex(e => new { e.BusinessId, e.ReservationId, e.JobType });
        });

        modelBuilder.Entity<ReservationAttendanceResponse>(entity =>
        {
            entity.HasKey(e => e.ReservationAttendanceResponseId);
            entity.Property(e => e.ResponseType).HasConversion<int>();
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Reservation)
                .WithMany()
                .HasForeignKey(e => e.ReservationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.SourceJob)
                .WithMany()
                .HasForeignKey(e => e.SourceJobId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasIndex(e => new { e.BusinessId, e.ReservationId, e.RespondedAtUtc });
        });

        modelBuilder.Entity<IntegrationConnection>(entity =>
        {
            entity.HasKey(e => e.IntegrationConnectionId);
            entity.Property(e => e.ConnectionType).HasConversion<int>().HasDefaultValue(ConnectionType.Integration);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.AccountIdentifier).HasMaxLength(300);
            entity.Property(e => e.SettingsJson).IsRequired().HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.SecretsJson).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.LastError).HasMaxLength(4000);
            entity.Property(e => e.CatalogSyncNextPage).HasDefaultValue(1);
            entity.Property(e => e.CustomerSyncNextPage).HasDefaultValue(1);
            entity.Property(e => e.CatalogDeltaCursorDate).HasColumnType("date");
            entity.HasOne(e => e.Business)
                  .WithMany()
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.BusinessId, e.ConnectionType, e.Provider, e.Capability }).IsUnique();
            entity.HasIndex(e => e.BusinessId);
        });
        modelBuilder.Entity<IntegrationChannelWarehouse>(entity =>
        {
            entity.HasKey(mapping => mapping.IntegrationChannelWarehouseId);
            entity.Property(mapping => mapping.WarehouseCode).IsRequired().HasMaxLength(100);
            entity.Property(mapping => mapping.WarehouseName).HasMaxLength(200);
            entity.HasOne(mapping => mapping.Business).WithMany()
                .HasForeignKey(mapping => mapping.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(mapping => mapping.IntegrationConnection).WithMany()
                .HasForeignKey(mapping => mapping.IntegrationConnectionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(mapping => mapping.BusinessWhatsAppNumber).WithMany()
                .HasForeignKey(mapping => mapping.BusinessWhatsAppNumberId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(mapping => new
                {
                    mapping.IntegrationConnectionId,
                    mapping.BusinessWhatsAppNumberId
                })
                .IsUnique();
            entity.HasIndex(mapping => mapping.BusinessId);
        });


        modelBuilder.Entity<ProductCategory>(entity =>
        {
            entity.HasKey(category => category.ProductCategoryId);
            entity.Property(category => category.ExternalCategoryId).HasMaxLength(150);
            entity.Property(category => category.Name).IsRequired().HasMaxLength(150);
            entity.HasOne(category => category.Business).WithMany()
                .HasForeignKey(category => category.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(category => category.Parent).WithMany(category => category.Children)
                .HasForeignKey(category => category.ParentProductCategoryId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasOne(category => category.IntegrationConnection).WithMany()
                .HasForeignKey(category => category.IntegrationConnectionId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasIndex(category => category.BusinessId);
            entity.HasIndex(category => new
                {
                    category.BusinessId,
                    category.IntegrationConnectionId,
                    category.ExternalCategoryId
                })
                .IsUnique()
                .HasFilter("[IntegrationConnectionId] IS NOT NULL AND [ExternalCategoryId] IS NOT NULL");
            entity.HasIndex(category => new
                {
                    category.BusinessId,
                    category.IntegrationConnectionId,
                    category.Name
                })
                .IsUnique();
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.ProductId);
            entity.Property(e => e.Source).HasConversion<int>();
            entity.Property(e => e.ExternalProductId).HasMaxLength(300);
            entity.Property(e => e.Sku).HasMaxLength(100);
            entity.Property(e => e.ProductCode).HasMaxLength(64);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(250);
            entity.Property(e => e.CategoryName).HasMaxLength(150);
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
            entity.Property(e => e.Currency).IsRequired().HasMaxLength(10);
            entity.Property(e => e.StockQuantity).HasPrecision(18, 2);
            entity.Property(e => e.RawPayloadJson).HasColumnType("NVARCHAR(MAX)");
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.IntegrationConnection)
                .WithMany()
                .HasForeignKey(e => e.IntegrationConnectionId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasOne(e => e.ProductCategory)
                .WithMany(category => category.Products)
                .HasForeignKey(e => e.ProductCategoryId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => new { e.BusinessId, e.Name });
            entity.HasIndex(e => new { e.BusinessId, e.CategoryName });
            entity.HasIndex(e => new { e.BusinessId, e.Sku });
            entity.HasIndex(e => new { e.BusinessId, e.IntegrationConnectionId, e.ExternalProductId })
                .IsUnique()
                .HasFilter("[IntegrationConnectionId] IS NOT NULL AND [ExternalProductId] IS NOT NULL");
        });

        modelBuilder.Entity<PublishedProductPriceRow>(entity =>
        {
            entity.ToTable("ProductPrices");
            entity.HasKey(price => price.ProductPriceId);
            entity.Property(price => price.Amount).HasPrecision(19, 4);
            entity.Property(price => price.CurrencyCode).HasMaxLength(3).IsFixedLength();
            entity.Property(price => price.ValidFrom).HasColumnType("datetimeoffset");
            entity.Property(price => price.ValidUntil).HasColumnType("datetimeoffset");
            entity.Property(price => price.CreatedAt).HasColumnType("datetimeoffset");
            entity.HasIndex(price => new { price.BusinessId, price.ProductId, price.IsActive });
            // ProductPrices is a dependent of the existing product master. This
            // relationship makes EF preserve the SQL foreign-key insert order when
            // a legacy caller still creates both records in one unit of work.
            entity.HasOne<Product>()
                .WithMany()
                .HasForeignKey(price => price.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ExternalCommerceCustomer>(entity =>
        {
            entity.HasKey(customer => customer.ExternalCommerceCustomerId);
            entity.Property(customer => customer.ExternalAccountId).IsRequired().HasMaxLength(150);
            entity.Property(customer => customer.ExternalCustomerId).IsRequired().HasMaxLength(150);
            entity.Property(customer => customer.ReconciliationStatus).IsRequired().HasMaxLength(16);
            entity.Property(customer => customer.ReconciliationError).HasMaxLength(500);
            entity.Property(customer => customer.ReconciliationOrigin).HasMaxLength(16);
            entity.Property(customer => customer.Name).HasMaxLength(250);
            entity.Property(customer => customer.PhoneNormalized).IsRequired().HasMaxLength(50);
            entity.Property(customer => customer.Phone).HasMaxLength(50);
            entity.HasOne(customer => customer.Business).WithMany()
                .HasForeignKey(customer => customer.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(customer => customer.IntegrationConnection).WithMany()
                .HasForeignKey(customer => customer.IntegrationConnectionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(customer => new
            {
                customer.BusinessId,
                customer.IntegrationConnectionId,
                customer.ExternalAccountId,
                customer.ExternalCustomerId
            }).IsUnique();
            entity.HasIndex(customer => new
            {
                customer.BusinessId,
                customer.IntegrationConnectionId,
                customer.PhoneNormalized,
                customer.IsActive
            });
        });

        modelBuilder.Entity<ExternalCustomerReconciliationOutboxMessage>(entity =>
        {
            entity.HasKey(message => message.MessageId);
            entity.Property(message => message.LastError).HasMaxLength(1000);
            entity.Property(message => message.RowVersion).IsRowVersion();
            entity.HasOne(message => message.ExternalCommerceCustomer).WithMany()
                .HasForeignKey(message => message.ExternalCommerceCustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(message => message.Business).WithMany()
                .HasForeignKey(message => message.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(message => message.ExternalCommerceCustomerId)
                .IsUnique()
                .HasFilter("[PublishedAt] IS NULL");
            entity.HasIndex(message => new
            {
                message.PublishedAt,
                message.AvailableAt,
                message.LeaseExpiresAt,
                message.OccurredAt
            });
        });

        ProductOfferModelConfiguration.Configure(modelBuilder);
        ProductSearchModelConfiguration.Configure(modelBuilder);
        modelBuilder.Entity<ProductRecommendationRule>(entity =>
        {
        CartMutationModelConfiguration.Configure(modelBuilder);
            entity.HasKey(e => e.ProductRecommendationRuleId);
            entity.Property(e => e.MatchType).HasConversion<int>();
            entity.Property(e => e.RecommendationType).HasConversion<int>();
            entity.Property(e => e.SourceValue).HasMaxLength(300);
            entity.Property(e => e.RecommendedExternalProductId).HasMaxLength(300);
            entity.Property(e => e.RecommendedSku).HasMaxLength(100);
            entity.Property(e => e.RecommendedSearchText).HasMaxLength(300);
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.IntegrationConnection)
                .WithMany()
                .HasForeignKey(e => e.IntegrationConnectionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SourceProduct)
                .WithMany()
                .HasForeignKey(e => e.SourceProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.RecommendedProduct)
                .WithMany()
                .HasForeignKey(e => e.RecommendedProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.BusinessId, e.IsActive, e.Priority });
            entity.HasIndex(e => e.IntegrationConnectionId);
            entity.HasIndex(e => e.SourceProductId);
            entity.HasIndex(e => e.RecommendedProductId);
        });

        modelBuilder.Entity<Promotion>(entity =>
        {
            entity.HasKey(e => e.PromotionId);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.CouponCode).HasMaxLength(80);
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => new { e.BusinessId, e.IsActive, e.StartsAtUtc, e.EndsAtUtc });
            entity.HasIndex(e => new { e.BusinessId, e.CouponCode })
                .HasFilter("CouponCode IS NOT NULL");
        });

        modelBuilder.Entity<PromotionCondition>(entity =>
        {
            entity.HasKey(e => e.PromotionConditionId);
            entity.Property(e => e.ItemType).HasConversion<int>();
            entity.Property(e => e.CategoryName).HasMaxLength(150);
            entity.Property(e => e.MinQuantity).HasPrecision(18, 2);
            entity.Property(e => e.MinSubtotal).HasPrecision(18, 2);
            entity.HasOne(e => e.Promotion)
                .WithMany(p => p.Conditions)
                .HasForeignKey(e => e.PromotionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Product)
                .WithMany()
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasOne(e => e.Service)
                .WithMany()
                .HasForeignKey(e => e.ServiceId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => e.PromotionId);
        });

        modelBuilder.Entity<PromotionBenefit>(entity =>
        {
            entity.HasKey(e => e.PromotionBenefitId);
            entity.Property(e => e.BenefitType).HasConversion<int>();
            entity.Property(e => e.TargetItemType).HasConversion<int>();
            entity.Property(e => e.CategoryName).HasMaxLength(150);
            entity.Property(e => e.DiscountPercentage).HasPrecision(5, 2);
            entity.Property(e => e.DiscountAmount).HasPrecision(18, 2);
            entity.Property(e => e.FixedUnitPrice).HasPrecision(18, 2);
            entity.Property(e => e.AppliesToQuantity).HasPrecision(18, 2);
            entity.HasOne(e => e.Promotion)
                .WithMany(p => p.Benefits)
                .HasForeignKey(e => e.PromotionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Product)
                .WithMany()
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasOne(e => e.Service)
                .WithMany()
                .HasForeignKey(e => e.ServiceId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => e.PromotionId);
        });

        modelBuilder.Entity<PromotionApplication>(entity =>
        {
            entity.HasKey(e => e.PromotionApplicationId);
            entity.Property(e => e.DiscountAmount).HasPrecision(18, 2);
            entity.Property(e => e.SnapshotJson).IsRequired().HasColumnType("NVARCHAR(MAX)");
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Promotion)
                .WithMany()
                .HasForeignKey(e => e.PromotionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Order)
                .WithMany()
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasOne(e => e.Reservation)
                .WithMany()
                .HasForeignKey(e => e.ReservationId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasOne(e => e.PaymentTransaction)
                .WithMany()
                .HasForeignKey(e => e.PaymentTransactionId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => e.PromotionId);
            entity.HasIndex(e => e.OrderId);
            entity.HasIndex(e => e.ReservationId);
            entity.HasIndex(e => e.PaymentTransactionId);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.OrderId);
            entity.Property(e => e.Source).HasConversion<int>();
            entity.Property(e => e.FulfillmentMode).HasConversion<int>();
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.CommerceWarehouseCode).HasMaxLength(100);
            entity.Property(e => e.CustomerNameSnapshot).HasMaxLength(150);
            entity.Property(e => e.CustomerEmailSnapshot).HasMaxLength(200);
            entity.Property(e => e.CustomerPhoneSnapshot).HasMaxLength(50);
            entity.Property(e => e.CustomerDocumentSnapshot).HasMaxLength(80);
            entity.Property(e => e.DeliveryAddressSnapshot).HasMaxLength(500);
            entity.Property(e => e.Currency).IsRequired().HasMaxLength(10);
            entity.Property(e => e.Subtotal).HasPrecision(18, 2);
            entity.Property(e => e.DiscountTotal).HasPrecision(18, 2);

            entity.Property(e => e.Total).HasPrecision(18, 2);
            entity.Property(e => e.ExternalOrderId).HasMaxLength(300);
            entity.Property(e => e.ExternalDocumentNumber).HasMaxLength(300);
            entity.Property(e => e.ExternalStatus).HasMaxLength(100);
            entity.Property(e => e.IdempotencyKey).HasMaxLength(200);
            entity.Property(e => e.CustomAttributesJson).HasColumnType("NVARCHAR(MAX)");
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Agent)
                .WithMany()
                .HasForeignKey(e => e.AgentId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
            entity.HasOne(e => e.Conversation)
                .WithMany()
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
            entity.HasOne(e => e.IntegrationConnection)
                .WithMany()
                .HasForeignKey(e => e.IntegrationConnectionId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasOne(e => e.PaymentTransaction)
                .WithMany()
                .HasForeignKey(e => e.PaymentTransactionId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => new { e.ConversationId, e.Status });
            entity.HasIndex(e => new { e.BusinessId, e.CreatedAt });
            entity.HasIndex(e => new { e.BusinessId, e.Status });
            entity.HasIndex(e => new { e.BusinessId, e.ExternalOrderId });
            entity.HasIndex(e => e.PaymentTransactionId)
                .IsUnique()
                .HasFilter("[PaymentTransactionId] IS NOT NULL");
            entity.HasIndex(e => new { e.BusinessId, e.IdempotencyKey })
                .IsUnique()
                .HasFilter("[IdempotencyKey] IS NOT NULL");
        });

        modelBuilder.Entity<OrderDraft>(entity =>
        {
            entity.HasKey(e => e.OrderDraftId);
            entity.Property(e => e.Source).HasConversion<int>();
            entity.Property(e => e.FulfillmentMode).HasConversion<int>();
            entity.Property(e => e.CommerceWarehouseCode).HasMaxLength(100);
            entity.Property(e => e.CustomerNameSnapshot).HasMaxLength(150);
            entity.Property(e => e.CustomerEmailSnapshot).HasMaxLength(200);
            entity.Property(e => e.CustomerPhoneSnapshot).HasMaxLength(50);
            entity.Property(e => e.CustomerDocumentSnapshot).HasMaxLength(80);
            entity.Property(e => e.DeliveryAddressSnapshot).HasMaxLength(500);
            entity.Property(e => e.Currency).IsRequired().HasMaxLength(10);
            entity.Property(e => e.Subtotal).HasPrecision(18, 2);
            entity.Property(e => e.DiscountTotal).HasPrecision(18, 2);

            entity.Property(e => e.Total).HasPrecision(18, 2);
            entity.Property(e => e.CustomAttributesJson).HasColumnType("NVARCHAR(MAX)");
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Agent)
                .WithMany()
                .HasForeignKey(e => e.AgentId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
            entity.HasOne(e => e.Conversation)
                .WithMany()
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.IntegrationConnection)
                .WithMany()
                .HasForeignKey(e => e.IntegrationConnectionId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasOne(e => e.PaymentTransaction)
                .WithMany()
                .HasForeignKey(e => e.PaymentTransactionId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => new { e.BusinessId, e.ConversationId }).IsUnique();
            entity.HasIndex(e => e.PaymentTransactionId)
                .IsUnique()
                .HasFilter("[PaymentTransactionId] IS NOT NULL");
        });

        modelBuilder.Entity<OrderDraftItem>(entity =>
        {
            entity.HasKey(e => e.OrderDraftItemId);
            entity.Property(e => e.ExternalProductId).HasMaxLength(300);
            entity.Property(e => e.Sku).HasMaxLength(100);
            entity.Property(e => e.ProductNameSnapshot).IsRequired().HasMaxLength(250);
            entity.Property(e => e.Quantity).HasPrecision(18, 2);
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
            entity.Property(e => e.DiscountAmount).HasPrecision(18, 2);

            entity.Property(e => e.LineTotal).HasPrecision(18, 2);
            entity.Property(e => e.RawPayloadJson).HasColumnType("NVARCHAR(MAX)");
            entity.HasOne(e => e.OrderDraft)
                .WithMany(d => d.Items)
                .HasForeignKey(e => e.OrderDraftId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Product)
                .WithMany()
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasOne(e => e.IntegrationConnection)
                .WithMany()
                .HasForeignKey(e => e.IntegrationConnectionId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasIndex(e => e.OrderDraftId);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => e.ProductId);
            entity.HasIndex(e => new { e.BusinessId, e.ExternalProductId });
        });
        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(e => e.OrderItemId);
            entity.Property(e => e.ExternalProductId).HasMaxLength(300);
            entity.Property(e => e.Sku).HasMaxLength(100);
            entity.Property(e => e.ProductNameSnapshot).IsRequired().HasMaxLength(250);
            entity.Property(e => e.Quantity).HasPrecision(18, 2);
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
            entity.Property(e => e.DiscountAmount).HasPrecision(18, 2);

            entity.Property(e => e.LineTotal).HasPrecision(18, 2);
            entity.Property(e => e.RawPayloadJson).HasColumnType("NVARCHAR(MAX)");
            entity.HasOne(e => e.Order)
                .WithMany(o => o.Items)
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Product)
                .WithMany()
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasOne(e => e.IntegrationConnection)
                .WithMany()
                .HasForeignKey(e => e.IntegrationConnectionId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasIndex(e => e.OrderId);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => e.ProductId);
            entity.HasIndex(e => new { e.BusinessId, e.ExternalProductId });
        });

        modelBuilder.Entity<OrderConnectionEvent>(entity =>
        {
            entity.HasKey(e => e.OrderConnectionEventId);
            entity.Property(e => e.ConnectionType).HasConversion<int>().HasDefaultValue(ConnectionType.Commerce);
            entity.Property(e => e.ExternalEventId).HasMaxLength(500);
            entity.Property(e => e.LastError).HasMaxLength(4000);
            entity.Property(e => e.RequestJson).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.ResponseJson).HasColumnType("NVARCHAR(MAX)");
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Order)
                .WithMany()
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.IntegrationConnection)
                .WithMany()
                .HasForeignKey(e => e.IntegrationConnectionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.OrderId, e.IntegrationConnectionId }).IsUnique();
            entity.HasIndex(e => e.BusinessId);
        });

        modelBuilder.Entity<ReservationIntegrationEvent>(entity =>
        {
            entity.HasKey(e => e.ReservationIntegrationEventId);
            entity.Property(e => e.Provider).HasConversion<int>();
            entity.Property(e => e.Capability).HasConversion<int>();
            entity.Property(e => e.ExternalEventId).HasMaxLength(500);
            entity.Property(e => e.LastError).HasMaxLength(4000);
            entity.HasOne(e => e.Business)
                  .WithMany()
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Reservation)
                  .WithMany()
                  .HasForeignKey(e => e.ReservationId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.IntegrationConnection)
                  .WithMany()
                  .HasForeignKey(e => e.IntegrationConnectionId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.ReservationId, e.IntegrationConnectionId }).IsUnique();
            entity.HasIndex(e => e.BusinessId);
        });

        modelBuilder.Entity<ExternalEscalationAttempt>(entity =>
        {
            entity.HasKey(e => e.ExternalEscalationAttemptId);
            entity.Property(e => e.EventName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.TargetType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ContactKey).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ContactNameSnapshot).IsRequired().HasMaxLength(200);
            entity.Property(e => e.ContactRoleSnapshot).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ContactPhoneSnapshot).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ContactTypeSnapshot).HasMaxLength(50);
            entity.Property(e => e.PickupAddressSnapshot).HasMaxLength(500);
            entity.Property(e => e.AttemptCode).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CustomPayloadJson).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.WhatsAppMessageId).HasMaxLength(128);
            entity.Property(e => e.OutcomeKey).HasMaxLength(100);
            entity.Property(e => e.ResponseText).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.ResponsePayloadJson).HasColumnType("NVARCHAR(MAX)");
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SourceAgent)
                .WithMany()
                .HasForeignKey(e => e.SourceAgentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.InboundAgent)
                .WithMany()
                .HasForeignKey(e => e.InboundAgentIdSnapshot)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => new { e.BusinessId, e.EventName, e.TargetType, e.TargetId });
            entity.HasIndex(e => new { e.BusinessId, e.ContactPhoneSnapshot, e.Status });
            entity.HasIndex(e => new { e.BusinessId, e.AttemptCode, e.ContactPhoneSnapshot });
            entity.HasIndex(e => e.WhatsAppMessageId);
        });

        modelBuilder.Entity<ExternalEscalationOutcomeDelivery>(entity =>
        {
            entity.HasKey(e => e.ExternalEscalationOutcomeDeliveryId);
            entity.Property(e => e.OutcomeKey).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PayloadJson).IsRequired().HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.LastError).HasMaxLength(4000);
            entity.HasOne(e => e.Business).WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Attempt).WithMany().HasForeignKey(e => e.ExternalEscalationAttemptId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.ExternalEscalationAttemptId, e.OutcomeKey }).IsUnique();
            entity.HasIndex(e => new { e.PublishedAt, e.NextAttemptAt });
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
            entity.Property(e => e.Keywords).HasMaxLength(1000);
            entity.Property(e => e.DurationMinutes).IsRequired();
            entity.Property(e => e.Price).IsRequired().HasPrecision(18, 2);
            entity.Property(e => e.IncludeInCheckoutTotal).IsRequired().HasDefaultValue(true);
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
            entity.Property(e => e.Tier).IsRequired().HasDefaultValue(Domain.Enums.ServiceTier.Base)
                  .HasConversion<int>();
            entity.Property(e => e.ServiceType).IsRequired()
                  .HasDefaultValue(Domain.Enums.ServiceType.Standard).HasConversion<int>();
            entity.Property(e => e.FulfillmentKind).IsRequired()
                  .HasDefaultValue(Domain.Enums.ServiceFulfillmentKind.Reservation).HasConversion<int>();
            entity.Property(e => e.FixedScheduleLabel).HasMaxLength(500);
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
            entity.HasIndex(e => new { e.ServiceId, e.BusinessResourceId }).IsUnique(); // Un uso Ãºnico por servicio-recurso
        });

        // Employee configuration
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(e => e.EmployeeId);
            entity.Property(e => e.BusinessId).IsRequired();
            entity.Property(e => e.PartyId);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.HasOne(e => e.Business)
                  .WithMany()
                  .HasForeignKey(e => e.BusinessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => new { e.BusinessId, e.PartyId }).IsUnique().HasFilter("[PartyId] IS NOT NULL");
            entity.HasIndex(e => new { e.BusinessId, e.Name }); // Ãndice para bÃºsquedas por nombre
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
            entity.HasIndex(e => new { e.EmployeeId, e.ServiceId }).IsUnique(); // Una relaciÃ³n Ãºnica por par empleado-servicio
        });

        // ConversationState configuration
        modelBuilder.Entity<ConversationStateEntity>(entity =>
        {
            entity.HasKey(e => e.ConversationId);
            entity.Property(e => e.Owner).HasConversion<byte>().HasDefaultValue(ConversationOwner.Bot);
            entity.Property(e => e.LastUserMessage).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.LastBotMessage).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.ActiveRequestStartedAtUtc);
            entity.Property(e => e.VerificationsJson).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.StageSnapshotsJson).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.RuntimeStateJson).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.FollowUpDueAtUtc);
            entity.Property(e => e.Version).IsRequired().HasDefaultValue(1).IsConcurrencyToken();
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
            entity.HasIndex(e => e.FollowUpDueAtUtc)
                  .HasFilter("[FollowUpDueAtUtc] IS NOT NULL");
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
            entity.Property(e => e.Source).HasConversion<int>();
            entity.Property(e => e.Source).HasConversion<int>().HasDefaultValue(Domain.Enums.PaymentTransactionSource.Automated);
            entity.Property(e => e.WebhookPayloadJson).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.CheckoutKind).HasConversion<int>().HasDefaultValue(CheckoutKind.Reservation);
            entity.Property(e => e.CheckoutSnapshotJson).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.QuoteHash).HasMaxLength(128);
            entity.Property(e => e.ConfirmationOutcome).HasMaxLength(100);
            entity.Property(e => e.LinkUrl).HasMaxLength(1000);
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
            entity.HasIndex(e => e.PaymentReferenceId).IsUnique();
            entity.HasIndex(e => e.ConversationId);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => e.ReservationId);
        });

        // InboundMessageReceipt configuration
        modelBuilder.Entity<InboundMessageReceipt>(entity =>
        {
            entity.HasKey(e => e.InboundMessageReceiptId);
            entity.Property(e => e.Provider).IsRequired().HasMaxLength(30);
            entity.Property(e => e.ProviderMessageId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.UserNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CustomerName).HasMaxLength(200);
            entity.Property(e => e.RawEntryJson).HasColumnType("NVARCHAR(MAX)");
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.LastError).HasMaxLength(4000);
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.BusinessId, e.Provider, e.ProviderMessageId }).IsUnique();
            entity.HasIndex(e => new { e.Status, e.ProcessingStartedAtUtc });
            entity.HasIndex(e => new { e.BusinessId, e.Provider, e.UserNumber, e.Status, e.ReceivedAtUtc });
        });

        // Enrollment configuration
        modelBuilder.Entity<Enrollment>(entity =>
        {
            entity.HasKey(e => e.EnrollmentId);
            entity.Property(e => e.CustomerName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.CustomerPhone).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CustomerEmail).HasMaxLength(200);
            entity.Property(e => e.FixedScheduleLabel).HasMaxLength(500);
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
            entity.Property(e => e.PosOfflinePasswordSalt).HasMaxLength(16);
            entity.Property(e => e.PosOfflinePasswordHash).HasMaxLength(32);
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
            entity.Property(e => e.Action).IsRequired().HasMaxLength(300);
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

        // â”€â”€ Generic Flow Engine â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        modelBuilder.Entity<SubscriptionPlan>(entity =>
        {
            entity.HasKey(e => e.SubscriptionPlanId);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.MonthlyPriceCop).HasPrecision(18, 2);
            entity.Property(e => e.MaxVariableCostCop).HasPrecision(18, 2);
            entity.Property(e => e.MaxVariableCostPercent).HasPrecision(5, 2);
            entity.Property(e => e.FeaturesJson).HasColumnType("NVARCHAR(MAX)");
            entity.HasIndex(e => e.Code).IsUnique();
        });

        modelBuilder.Entity<BusinessSubscription>(entity =>
        {
            entity.HasKey(e => e.BusinessSubscriptionId);
            entity.Property(e => e.PlanCodeSnapshot).IsRequired().HasMaxLength(50);
            entity.Property(e => e.PlanNameSnapshot).IsRequired().HasMaxLength(100);
            entity.Property(e => e.MonthlyPriceCop).HasPrecision(18, 2);
            entity.Property(e => e.MaxVariableCostCop).HasPrecision(18, 2);
            entity.Property(e => e.MaxVariableCostPercent).HasPrecision(5, 2);
            entity.Property(e => e.ExtraVariableCostCop).HasPrecision(18, 2);
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SubscriptionPlan)
                .WithMany(p => p.BusinessSubscriptions)
                .HasForeignKey(e => e.SubscriptionPlanId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => new { e.BusinessId, e.Status });
        });

        modelBuilder.Entity<BusinessUsagePeriod>(entity =>
        {
            entity.HasKey(e => e.BusinessUsagePeriodId);
            entity.Property(e => e.VariableCostLimitCop).HasPrecision(18, 2);
            entity.Property(e => e.VariableCostExtraCop).HasPrecision(18, 2);
            entity.Property(e => e.VariableCostUsedCop).HasPrecision(18, 2);
            entity.HasOne(e => e.BusinessSubscription)
                .WithMany(s => s.UsagePeriods)
                .HasForeignKey(e => e.BusinessSubscriptionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.BusinessSubscriptionId, e.PeriodStart, e.PeriodEnd }).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.PeriodStart, e.PeriodEnd });
        });

        modelBuilder.Entity<UsageLedgerEntry>(entity =>
        {
            entity.HasKey(e => e.UsageLedgerEntryId);
            entity.Property(e => e.OperationType).HasConversion<int>();
            entity.Property(e => e.EstimatedCostCop).HasPrecision(18, 4);
            entity.Property(e => e.ActualCostCop).HasPrecision(18, 4);
            entity.Property(e => e.Model).HasMaxLength(100);
            entity.Property(e => e.MetadataJson).HasColumnType("NVARCHAR(MAX)");
            entity.HasOne(e => e.BusinessUsagePeriod)
                .WithMany(p => p.LedgerEntries)
                .HasForeignKey(e => e.BusinessUsagePeriodId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Agent)
                .WithMany()
                .HasForeignKey(e => e.AgentId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
            entity.HasOne(e => e.Conversation)
                .WithMany()
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => e.BusinessUsagePeriodId);
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<UsageCostRate>(entity =>
        {
            entity.HasKey(e => e.UsageCostRateId);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(100);
            entity.Property(e => e.OperationType).HasConversion<int>();
            entity.Property(e => e.Unit).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CostUsd).HasPrecision(18, 8);
            entity.Property(e => e.CostCop).HasPrecision(18, 4);
            entity.HasIndex(e => new { e.Code, e.OperationType, e.EffectiveFrom });
        });

        modelBuilder.Entity<AgentType>(entity =>
        {
            entity.HasKey(e => e.AgentTypeId);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<AgentTemplate>(entity =>
        {
            entity.HasKey(e => e.AgentTemplateId);
            entity.Property(e => e.Key).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.SettingsJson).HasColumnType("NVARCHAR(MAX)");
            entity.HasIndex(e => e.Key).IsUnique();
            entity.HasIndex(e => e.Kind);
        });

        modelBuilder.Entity<BusinessInboundContact>(entity =>
        {
            entity.HasKey(e => e.BusinessInboundContactId);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Key).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Role).HasMaxLength(100);
            entity.Property(e => e.PhoneNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.PhoneNormalized).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CapabilitiesJson).HasColumnType("NVARCHAR(MAX)");
            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.InboundAgent)
                .WithMany()
                .HasForeignKey(e => e.InboundAgentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Employee)
                .WithMany()
                .HasForeignKey(e => e.EmployeeId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
            entity.HasIndex(e => new { e.BusinessId, e.PhoneNormalized }).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.Type });
            entity.HasIndex(e => e.InboundAgentId);
        });
        modelBuilder.Entity<Agent>(entity =>
        {
            entity.HasKey(e => e.AgentId);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(50).HasDefaultValue("customer");
            entity.Property(e => e.SettingsJson).HasColumnType("NVARCHAR(MAX)");
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
