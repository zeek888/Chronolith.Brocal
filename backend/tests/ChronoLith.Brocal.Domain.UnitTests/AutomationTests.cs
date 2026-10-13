using ChronoLith.Brocal.Domain.Automation;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Domain.UnitTests;

public sealed class RuleConditionTests
{
    private static readonly AutomationDocumentDefinition Ticket = AutomationCatalog.Of(AutomationDocument.ServiceTicket);
    private static readonly AutomationDocumentDefinition Order = AutomationCatalog.Of(AutomationDocument.WorkOrder);

    [Fact]
    public void Every_document_offers_its_triggers_and_unique_facts()
    {
        foreach (var document in AutomationCatalog.All)
        {
            Assert.NotEmpty(document.Triggers);
            Assert.Equal(document.Facts.Count, document.Facts.Select(f => f.Key).Distinct().Count());
            Assert.True(BrocalModules.Exists(document.Module), document.Module);
            Assert.All(document.Facts.Where(f => f.Type == FactType.Enum), f => Assert.NotEmpty(f.Values!));
            Assert.All(document.Facts.Where(f => f.Type == FactType.Reference), f => Assert.False(string.IsNullOrEmpty(f.Catalog)));
            if (document.Workflow is { } gate)
            {
                Assert.Contains(gate.Start, document.Triggers);
            }
        }

        Assert.All(Enum.GetValues<AutomationTrigger>(), t => Assert.True(AutomationCatalog.TriggerNames.ContainsKey(t)));
    }

    [Fact]
    public void Conditions_are_normalized_against_the_facts_of_the_document()
    {
        var result = RuleConditions.Normalize(Order, [
            new RuleCondition("TOTAL", ConditionOperator.GreaterThan, "1"),
        ], isGlobal: false);
        Assert.True(result.IsFailure);
        Assert.Equal("Automation.UnknownField", result.Error.Code);

        var normalized = RuleConditions.Normalize(Order, [
            new RuleCondition("Priority", ConditionOperator.In, "HIGH, urgent"),
            new RuleCondition("durationHours", ConditionOperator.GreaterOrEqual, " 12.50 "),
            new RuleCondition("hasCrew", ConditionOperator.IsFalse, "ignored")
        ], isGlobal: false).Value;

        Assert.Equal(new RuleCondition("priority", ConditionOperator.In, "high,urgent"), normalized[0]);
        Assert.Equal("12.50", normalized[1].Value);
        Assert.Null(normalized[2].Value);
    }

    [Theory]
    [InlineData("hasSignature", ConditionOperator.GreaterThan, "1")]
    [InlineData("total", ConditionOperator.In, "1,2")]
    [InlineData("total", ConditionOperator.GreaterThan, "mucho")]
    [InlineData("customerId", ConditionOperator.Equal, "no-es-un-id")]
    public void Invalid_operators_and_values_are_rejected(string field, ConditionOperator op, string value)
    {
        Assert.True(RuleConditions.Normalize(Ticket, [new RuleCondition(field, op, value)], isGlobal: false).IsFailure);
    }

    [Fact]
    public void Global_definitions_cannot_use_records_of_one_company()
    {
        var condition = new RuleCondition("customerId", ConditionOperator.Equal, Guid.NewGuid().ToString());

        Assert.True(RuleConditions.Normalize(Ticket, [condition], isGlobal: false).IsSuccess);
        Assert.Equal("Automation.CompanyOnlyField", RuleConditions.Normalize(Ticket, [condition], isGlobal: true).Error.Code);
    }

    [Fact]
    public void Conditions_match_all_or_any_and_missing_facts_never_compare()
    {
        var facts = new Dictionary<string, object?> { ["total"] = 75_000m, ["hasSignature"] = false, ["priority"] = "high", ["deviationPercent"] = null };
        RuleCondition[] conditions =
        [
            new("total", ConditionOperator.GreaterThan, "50000"),
            new("hasSignature", ConditionOperator.IsTrue, null)
        ];

        Assert.False(RuleConditions.Matches(conditions, ConditionMatch.All, facts));
        Assert.True(RuleConditions.Matches(conditions, ConditionMatch.Any, facts));
        Assert.True(RuleConditions.Matches([], ConditionMatch.All, facts));

        Assert.True(RuleConditions.Evaluate(new RuleCondition("priority", ConditionOperator.In, "high,urgent"), facts));
        Assert.False(RuleConditions.Evaluate(new RuleCondition("priority", ConditionOperator.NotIn, "high,urgent"), facts));
        Assert.True(RuleConditions.Evaluate(new RuleCondition("hasSignature", ConditionOperator.IsFalse, null), facts));
        Assert.False(RuleConditions.Evaluate(new RuleCondition("deviationPercent", ConditionOperator.LessThan, "0"), facts));
        Assert.True(RuleConditions.Evaluate(new RuleCondition("deviationPercent", ConditionOperator.NotEqual, "5"), facts));
        Assert.True(RuleConditions.Evaluate(new RuleCondition("total", ConditionOperator.Equal, "75000.00"), facts));
    }

    [Fact]
    public void The_fingerprint_ignores_facts_that_change_with_time()
    {
        var facts = new Dictionary<string, object?> { ["total"] = 1_000m, ["daysSinceService"] = 1m };
        var later = new Dictionary<string, object?>(facts) { ["daysSinceService"] = 9m };
        var edited = new Dictionary<string, object?>(facts) { ["total"] = 1_500m };

        Assert.Equal(RuleConditions.Hash(Ticket, facts), RuleConditions.Hash(Ticket, later));
        Assert.NotEqual(RuleConditions.Hash(Ticket, facts), RuleConditions.Hash(Ticket, edited));
    }
}

public sealed class WorkflowTests
{
    private static readonly Guid Office = Guid.NewGuid();
    private static readonly Guid Director = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();

    private static WorkflowStep Step(string name, string template, int required = 1, bool allowRequester = false) =>
        new(name, new Audience([template], [], [], false), required, 24, allowRequester);

    private static WorkflowDefinition Definition(params WorkflowStep[] steps) =>
        WorkflowDefinition.Create(null, new WorkflowDefinitionData("Viáticos mayores", null, AutomationDocument.ExpenseReport, true, false, 0, ConditionMatch.All,
            [new RuleCondition("total", ConditionOperator.GreaterThan, "10000")], steps)).Value;

    private static WorkflowInstance Request(WorkflowDefinition definition) =>
        WorkflowInstance.Request(definition, Guid.NewGuid(), "VI-000010", new Dictionary<string, object?> { ["total"] = 12_000m }, Requester, "Operador", DateTimeOffset.UtcNow);

    [Fact]
    public void A_workflow_needs_steps_with_approvers_and_only_documents_with_a_gate()
    {
        var data = new WorkflowDefinitionData("Sin pasos", null, AutomationDocument.ExpenseReport, true, false, 0, ConditionMatch.All, [], []);
        Assert.Equal("Automation.InvalidSteps", WorkflowDefinition.Create(null, data).Error.Code);

        var noApprovers = data with { Steps = [new WorkflowStep("Paso", Audience.Empty, 1, null, false)] };
        Assert.Equal("Automation.InvalidSteps", WorkflowDefinition.Create(null, noApprovers).Error.Code);

        var fuel = data with { Document = AutomationDocument.FuelLoad, Steps = [Step("Paso", "management")] };
        Assert.Equal("Automation.NoWorkflow", WorkflowDefinition.Create(null, fuel).Error.Code);
    }

    [Fact]
    public void Global_workflows_use_role_templates_and_only_they_can_be_mandatory()
    {
        var withCompanyRole = new WorkflowDefinitionData("Global", null, AutomationDocument.Quote, true, true, 0, ConditionMatch.All, [],
            [new WorkflowStep("Dirección", new Audience([], [Guid.NewGuid()], [], false), 1, null, false)]);
        Assert.Equal("Automation.CompanyRolesInGlobal", WorkflowDefinition.Create(null, withCompanyRole).Error.Code);

        var company = WorkflowDefinition.Create(Guid.NewGuid(), withCompanyRole).Value;
        Assert.False(company.IsMandatory);
        Assert.False(company.IsGlobal);

        var unknownTemplate = withCompanyRole with { Steps = [Step("Dirección", "no-existe")] };
        Assert.Equal("Automation.UnknownRoleTemplate", WorkflowDefinition.Create(null, unknownTemplate).Error.Code);
    }

    [Fact]
    public void Steps_are_approved_in_order_and_the_last_one_approves_the_request()
    {
        var instance = Request(Definition(Step("Oficina", "office"), Step("Dirección", "management")));
        Assert.IsType<WorkflowRequestedEvent>(Assert.Single(instance.DomainEvents));
        instance.ClearDomainEvents();

        Assert.True(instance.Decide(Office, "Oficina", true, null, DateTimeOffset.UtcNow).IsSuccess);
        Assert.Equal(1, instance.CurrentStep);
        Assert.IsType<WorkflowStepAdvancedEvent>(Assert.Single(instance.DomainEvents));
        instance.ClearDomainEvents();

        Assert.True(instance.Decide(Director, "Dirección", true, "De acuerdo", DateTimeOffset.UtcNow).IsSuccess);
        Assert.Equal(WorkflowStatus.Approved, instance.Status);
        var completed = Assert.IsType<WorkflowCompletedEvent>(Assert.Single(instance.DomainEvents));
        Assert.Equal(Director, completed.DecidedById);
        Assert.Equal(WorkflowErrors.NotPending, instance.Decide(Office, "Oficina", true, null, DateTimeOffset.UtcNow).Error);
    }

    [Fact]
    public void A_step_can_require_several_distinct_approvals()
    {
        var instance = Request(Definition(Step("Comité", "management", required: 2)));

        Assert.True(instance.Decide(Director, "Dirección", true, null, DateTimeOffset.UtcNow).IsSuccess);
        Assert.Equal("Workflow.AlreadyDecided", instance.Decide(Director, "Dirección", true, null, DateTimeOffset.UtcNow).Error.Code);
        Assert.True(instance.IsPending);

        Assert.True(instance.Decide(Office, "Oficina", true, null, DateTimeOffset.UtcNow).IsSuccess);
        Assert.Equal(WorkflowStatus.Approved, instance.Status);
    }

    [Fact]
    public void A_rejection_needs_a_reason_and_ends_the_request()
    {
        var instance = Request(Definition(Step("Oficina", "office"), Step("Dirección", "management")));

        Assert.Equal("Workflow.ReasonRequired", instance.Decide(Office, "Oficina", false, " ", DateTimeOffset.UtcNow).Error.Code);
        Assert.True(instance.Decide(Office, "Oficina", false, "Faltan comprobantes", DateTimeOffset.UtcNow).IsSuccess);

        Assert.Equal(WorkflowStatus.Rejected, instance.Status);
        Assert.Equal("Faltan comprobantes", instance.ClosingComment);
        Assert.False(instance.Cancel(Requester, "Operador", "Ya no aplica", DateTimeOffset.UtcNow).IsSuccess);
    }

    [Fact]
    public void Requests_copy_the_steps_and_the_data_they_were_requested_with()
    {
        var definition = Definition(Step("Oficina", "office"));
        var instance = Request(definition);

        definition.Update(new WorkflowDefinitionData("Otro nombre", null, AutomationDocument.ExpenseReport, true, false, 0, ConditionMatch.All, [],
            [Step("Uno", "office"), Step("Dos", "management")]));

        Assert.Single(instance.Steps);
        Assert.Equal("Viáticos mayores", instance.DefinitionName);
        Assert.Equal("12000", instance.Facts["total"]);
        Assert.Equal(WorkflowCompletion.Execute, instance.Completion);
    }
}

public sealed class BusinessRuleTests
{
    private static BusinessRuleData Data(RuleEffect effect, RuleRecipients? recipients = null) => new(
        "Boletas sin evidencia", null, AutomationDocument.ServiceTicket, [AutomationTrigger.Close], true, false, 0, ConditionMatch.All,
        [new RuleCondition("photoCount", ConditionOperator.LessThan, "1")], effect, "La boleta {folio} necesita fotos (tiene {photoCount}).",
        NotificationSeverity.Warning, recipients);

    [Fact]
    public void A_rule_needs_conditions_valid_moments_and_recipients_when_it_notifies()
    {
        Assert.True(BusinessRule.Create(null, Data(RuleEffect.Block)).IsSuccess);
        Assert.Equal("Automation.RecipientsRequired", BusinessRule.Create(null, Data(RuleEffect.Notify)).Error.Code);
        Assert.True(BusinessRule.Create(null, Data(RuleEffect.Notify, new RuleRecipients(Audience.Empty, ["HSE@Empresa.mx"], false))).IsSuccess);

        Assert.Equal("Automation.ConditionsRequired", BusinessRule.Create(null, Data(RuleEffect.Block) with { Conditions = [] }).Error.Code);
        Assert.Equal("Automation.InvalidTriggers", BusinessRule.Create(null, Data(RuleEffect.Block) with { Triggers = [AutomationTrigger.Stamp] }).Error.Code);
    }

    [Fact]
    public void The_message_takes_the_folio_and_the_values_of_the_document()
    {
        var rule = BusinessRule.Create(Guid.NewGuid(), Data(RuleEffect.Block)).Value;

        Assert.Equal("La boleta BS-000123 necesita fotos (tiene 0).", rule.Render("BS-000123", new Dictionary<string, object?> { ["photoCount"] = 0m }));
    }

    [Fact]
    public void Recipient_addresses_are_normalized()
    {
        var rule = BusinessRule.Create(null, Data(RuleEffect.Notify, new RuleRecipients(Audience.Empty, ["HSE@Empresa.mx", "hse@empresa.mx"], true))).Value;

        Assert.Equal(new[] { "hse@empresa.mx" }, rule.Recipients.Emails);
        Assert.True(rule.IsGlobal);
    }
}
