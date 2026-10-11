using System.Reflection;
using ChronoLith.Brocal.Domain.Abstractions;
using NetArchTest.Rules;

namespace ChronoLith.Brocal.ArchitectureTests;

/// <summary>Guards the Clean Architecture dependency rule and the multi-tenant conventions of the model.</summary>
public sealed class LayerDependencyTests
{
    private const string Domain = "ChronoLith.Brocal.Domain";
    private const string Application = "ChronoLith.Brocal.Application";
    private const string Infrastructure = "ChronoLith.Brocal.Infrastructure";
    private const string Api = "ChronoLith.Brocal.Api";

    private static readonly Assembly DomainAssembly = typeof(Result).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Application.DependencyInjection).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Infrastructure.DependencyInjection).Assembly;

    [Fact]
    public void Domain_does_not_depend_on_other_layers()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(Application, Infrastructure, Api, "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_api()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(Infrastructure, Api, "Microsoft.AspNetCore", "Npgsql", "ClosedXML", "MigraDoc", "PdfSharp", "MailKit")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_api()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn(Api)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Handlers_are_internal_and_sealed()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveNameEndingWith("Handler")
            .Should()
            .NotBePublic()
            .And()
            .BeSealed()
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Commands_and_queries_are_sealed_records()
    {
        var offenders = ApplicationAssembly.GetTypes()
            .Where(t => t is { IsPublic: true, IsInterface: false } && (t.Name.EndsWith("Command", StringComparison.Ordinal) || t.Name.EndsWith("Query", StringComparison.Ordinal)))
            .Where(t => !t.IsSealed || !IsRecord(t))
            .Select(t => t.Name)
            .ToList();

        Assert.True(offenders.Count == 0, $"Comandos o consultas que no son records sellados: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Domain_entities_have_no_public_setters()
    {
        var offenders = DomainAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Namespace?.StartsWith(Domain, StringComparison.Ordinal) == true)
            .Where(t => !IsRecord(t))
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(p => p.SetMethod?.IsPublic == true)
            .Select(p => $"{p.DeclaringType?.Name}.{p.Name}")
            .ToList();

        Assert.True(offenders.Count == 0, $"Propiedades con setter público: {string.Join(", ", offenders)}");
    }

    /// <summary>The company of a record is stamped by the persistence interceptor; nothing else may assign it.</summary>
    [Fact]
    public void Tenant_id_cannot_be_assigned_from_outside_the_entity()
    {
        var offenders = DomainAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ITenantOwned).IsAssignableFrom(t))
            .Select(t => t.GetProperty(nameof(ITenantOwned.TenantId))!)
            .Where(p => p.SetMethod is { IsPrivate: false })
            .Select(p => p.DeclaringType!.Name)
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0, $"TenantId con setter no privado: {string.Join(", ", offenders)}");
    }

    private static bool IsRecord(Type type) => type.GetMethod("<Clone>$") is not null;

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        result.IsSuccessful ? string.Empty : $"Tipos que violan la regla: {string.Join(", ", result.FailingTypeNames ?? [])}";
}
