using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Domain.Compliance;
using ChronoLith.Brocal.Domain.Hse;
using ChronoLith.Brocal.Domain.Inventory;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Application.Features.Platform;

/// <summary>
/// Default configuration of a new company: roles, notification rules, document types used in the Mexican oil
/// sector (SCT, ASEA, DC-3, H2S, SSPA…), inspection checklists and a general warehouse. Everything can be edited.
/// Must run inside the tenant scope of the new company.
/// </summary>
public static class TenantStarterKit
{
    public static void Apply(IApplicationDbContext db)
    {
        foreach (var template in RoleTemplates.All)
        {
            db.Roles.Add(TenantRole.FromTemplate(template));
        }

        db.NotificationChannels.Add(NotificationChannelSettings.CreateDefault());
        db.AccountingSettings.Add(Domain.Platform.AccountingSettings.CreateDefault());

        foreach (var (code, name, subject, authority, months, mandatory) in DocumentTypes)
        {
            db.DocumentTypes.Add(DocumentType.Create(new DocumentTypeData(code, name, subject, authority, months, null, mandatory, null)).Value);
        }

        db.InspectionTemplates.Add(InspectionTemplate.Create("PREUSO-AT", "Inspección pre-uso de autotanque", InspectionCategory.UnitPreUse,
        [
            new("frenos", "Frenos de servicio y estacionamiento funcionan", true),
            new("llantas", "Llantas sin daños y con presión adecuada", true),
            new("luces", "Luces, direccionales y torreta funcionan", false),
            new("fugas", "Sin fugas en tanque, válvulas y mangueras", true),
            new("extintor", "Extintor cargado y vigente", true),
            new("botiquin", "Botiquín completo", false),
            new("tierra", "Cable de tierra física en buen estado", true),
            new("rombos", "Rombos de identificación de material peligroso visibles", false),
            new("documentos", "Tarjeta de circulación, póliza y permiso SCT a bordo", false)
        ], null).Value);

        db.InspectionTemplates.Add(InspectionTemplate.Create("EPP", "Inspección de equipo de protección personal", InspectionCategory.Ppe,
        [
            new("casco", "Casco en buen estado", true),
            new("lentes", "Lentes de seguridad", false),
            new("botas", "Botas de seguridad con casquillo", true),
            new("guantes", "Guantes adecuados a la actividad", false),
            new("overol", "Overol retardante a la flama", true),
            new("h2s", "Monitor personal de H2S calibrado", true),
            new("auditiva", "Protección auditiva", false)
        ], null).Value);

        db.Warehouses.Add(Warehouse.Create("ALM-GRAL", "Almacén general", null, null, null).Value);
    }

    private static readonly (string Code, string Name, DocumentSubject Subject, string Authority, int? Months, bool Mandatory)[] DocumentTypes =
    [
        // Unidades
        ("TC", "Tarjeta de circulación", DocumentSubject.Unit, "Gobierno estatal / SCT", 12, true),
        ("PERM-SCT", "Permiso de autotransporte federal SCT", DocumentSubject.Unit, "SCT (SICT)", null, true),
        ("POL-RC", "Póliza de seguro de responsabilidad civil", DocumentSubject.Unit, "Aseguradora", 12, true),
        ("POL-AMB", "Póliza de seguro por daño ambiental", DocumentSubject.Unit, "Aseguradora", 12, false),
        ("VFM", "Verificación físico-mecánica", DocumentSubject.Unit, "SCT (SICT)", 6, true),
        ("VER-EMI", "Verificación de emisiones contaminantes", DocumentSubject.Unit, "SEMARNAT", 6, true),
        ("ASEA", "Dictamen o autorización ASEA", DocumentSubject.Unit, "ASEA", 12, false),
        ("SEMARNAT-RP", "Autorización SEMARNAT de transporte de residuos peligrosos", DocumentSubject.Unit, "SEMARNAT", null, false),
        ("CAL-TQ", "Certificado de calibración del tanque", DocumentSubject.Unit, "Unidad de verificación", 12, false),
        ("HIDRO", "Prueba hidrostática", DocumentSubject.Unit, "Unidad de verificación", 60, false),
        // Personal
        ("DC3", "Constancia DC-3 (competencias laborales)", DocumentSubject.Employee, "STPS", null, false),
        ("H2S", "Curso de seguridad en H2S", DocumentSubject.Employee, "Instructor certificado", 12, true),
        ("MED", "Examen médico", DocumentSubject.Employee, "Médico", 12, true),
        ("SSPA", "Curso SSPA / inducción de seguridad", DocumentSubject.Employee, "Pemex", 12, true),
        ("LIC-FED", "Licencia federal de conducir", DocumentSubject.Employee, "SCT (SICT)", 24, false),
        ("ANTIDOP", "Examen antidoping", DocumentSubject.Employee, "Laboratorio", 6, false),
        ("ALTURAS", "Trabajos en altura", DocumentSubject.Employee, "Instructor certificado", 12, false),
        ("ESP-CONF", "Espacios confinados", DocumentSubject.Employee, "Instructor certificado", 12, false),
        ("1AUX", "Primeros auxilios", DocumentSubject.Employee, "Instructor certificado", 24, false),
        ("MANEJO", "Manejo defensivo", DocumentSubject.Employee, "Instructor certificado", 24, false),
        ("PASE", "Pase o credencial de acceso del cliente", DocumentSubject.Employee, "Cliente", 12, false)
    ];
}
