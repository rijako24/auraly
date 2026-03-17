using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Identity.DTOs;

public record UpdateBusinessConfigurationRequest(
    Dictionary<BusinessConfigurationKey, string> Configurations);
