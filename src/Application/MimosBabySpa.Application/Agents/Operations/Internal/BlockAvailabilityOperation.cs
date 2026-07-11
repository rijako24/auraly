using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Operations.Support;
using System.Text.Json;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Operations.Internal;

public sealed class BlockAvailabilityOperation : IAgentOperation
{
private readonly IUnitOfWork _unitOfWork;

    public BlockAvailabilityOperation(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public OperationDescriptor Descriptor => new(Name, ParametersSchema, ["internal.availability_blocked"], [], [], []);

    public async Task<OperationOutcome> ExecuteAsync(JsonElement arguments, OperationContext context, CancellationToken cancellationToken = default)
    {
        var session = context.Session ?? throw new InvalidOperationException("internal.block_availability requires a conversation session.");
        var json = await ExecuteCoreAsync(arguments, session, cancellationToken);
        return OperationJsonResult.Parse(json, "internal.availability_blocked");
    }

    public string Name => "internal.block_availability";

    public string Description => "Blocks a date or time range for the current business or one employee, so the bot stops offering those slots.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "date": { "type": "string", "description": "YYYY-MM-DD, today/hoy, or tomorrow/manana" },
            "end_date": { "type": "string", "description": "Optional YYYY-MM-DD end date, max 31 days" },
            "start_time": { "type": "string", "description": "Optional HH:mm. Omit with end_time for full day" },
            "end_time": { "type": "string", "description": "Optional HH:mm. Omit with start_time for full day" },
            "employee_id": { "type": "string" },
            "employee_name": { "type": "string" },
            "reason": { "type": "string" },
            "preview_only": { "type": "boolean" }
          },
          "required": ["date"]
        }
        """;

    private async Task<string> ExecuteCoreAsync(JsonElement arguments, AgentConversationContext ctx, CancellationToken cancellationToken = default)
    {
        if (!InternalOperationParsing.TryGetDate(arguments, "date", ctx.BusinessToday, out var startDate))
            return OperationJsonHelper.Error("date_required", "date must be provided as YYYY-MM-DD, today/hoy, or tomorrow/manana.");

        var endDate = InternalOperationParsing.TryGetDate(arguments, "end_date", ctx.BusinessToday, out var parsedEnd)
            ? parsedEnd
            : startDate;

        if (endDate < startDate)
            return OperationJsonHelper.Error("invalid_date_range", "end_date must be the same as or after date.");

        if (endDate.DayNumber - startDate.DayNumber > 31)
            return OperationJsonHelper.Error("date_range_too_large", "Availability blocks are limited to 31 days.");

        var hasStart = InternalOperationParsing.TryGetTime(arguments, "start_time", out var startTime);
        var hasEnd = InternalOperationParsing.TryGetTime(arguments, "end_time", out var endTime);
        if (hasStart != hasEnd)
            return OperationJsonHelper.Error("invalid_time_range", "Provide both start_time and end_time, or omit both for a full-day block.");

        if (hasStart && endTime <= startTime)
            return OperationJsonHelper.Error("invalid_time_range", "end_time must be after start_time.");

        OperationJsonHelper.TryGetString(arguments, "reason", out var reason);
        OperationJsonHelper.TryGetBool(arguments, "preview_only", out var previewOnly);

        var employee = await ResolveEmployeeAsync(arguments, ctx.BusinessId, cancellationToken);
        if (employee.Error is not null)
            return OperationJsonHelper.Error(employee.Error.Value.Code, employee.Error.Value.Message);

        var blocks = new List<BusinessAvailabilityBlock>();
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            blocks.Add(new BusinessAvailabilityBlock
            {
                BusinessAvailabilityBlockId = Guid.NewGuid(),
                BusinessId = ctx.BusinessId,
                EmployeeId = employee.Employee?.EmployeeId,
                Date = date,
                StartTime = hasStart ? startTime : null,
                EndTime = hasEnd ? endTime : null,
                Reason = string.IsNullOrWhiteSpace(reason) ? "Bloqueado por operaciones" : reason.Trim(),
                Source = "operations",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (!previewOnly)
        {
            foreach (var block in blocks)
                await _unitOfWork.BusinessAvailabilityBlocks.AddAsync(block, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var data = new
        {
            preview_only = previewOnly,
            blocked = !previewOnly,
            scope = employee.Employee is null ? "business" : "employee",
            employee = employee.Employee is null ? null : new { employee.Employee.EmployeeId, employee.Employee.Name },
            blocks = blocks.Select(b => new
            {
                block_id = previewOnly ? (Guid?)null : b.BusinessAvailabilityBlockId,
                date = b.Date.ToString("yyyy-MM-dd"),
                start_time = b.StartTime?.ToString(@"hh\:mm"),
                end_time = b.EndTime?.ToString(@"hh\:mm"),
                full_day = !b.StartTime.HasValue || !b.EndTime.HasValue,
                reason = b.Reason
            })
        };

        return previewOnly
            ? OperationJsonHelper.Ok(data)
            : OperationJsonHelper.Ok(data, OperationEffectNames.RequestCompleted);
    }

    private async Task<(Employee? Employee, (string Code, string Message)? Error)> ResolveEmployeeAsync(
        JsonElement arguments,
        Guid businessId,
        CancellationToken ct)
    {
        if (OperationJsonHelper.TryGetString(arguments, "employee_id", out var employeeIdRaw))
        {
            if (!Guid.TryParse(employeeIdRaw, out var employeeId))
                return (null, ("invalid_employee_id", "employee_id must be a valid GUID."));

            var employee = await _unitOfWork.Employees.GetByIdAsync(employeeId);
            if (employee is null || employee.BusinessId != businessId || !employee.IsActive)
                return (null, ("employee_not_found", "The employee was not found in this business."));

            return (employee, null);
        }

        if (OperationJsonHelper.TryGetString(arguments, "employee_name", out var employeeName))
        {
            var employees = (await _unitOfWork.Employees.GetActiveByBusinessIdAsync(businessId)).ToList();
            var matches = employees
                .Where(e => e.Name.Contains(employeeName.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
                return (null, ("employee_not_found", "No active employee matched employee_name."));

            if (matches.Count > 1)
                return (null, ("employee_ambiguous", "Multiple employees matched employee_name. Use employee_id."));

            return (matches[0], null);
        }

        return (null, null);
    }
}
