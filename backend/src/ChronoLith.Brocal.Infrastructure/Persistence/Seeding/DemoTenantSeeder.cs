using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Features.Platform;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Compliance;
using ChronoLith.Brocal.Domain.Fiscal;
using ChronoLith.Brocal.Domain.Fleet;
using ChronoLith.Brocal.Domain.Hse;
using ChronoLith.Brocal.Domain.Inventory;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.People;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChronoLith.Brocal.Infrastructure.Persistence.Seeding;

/// <summary>
/// Sample company "demo" (a contractor of water transport and pumping services in Tabasco) with master data,
/// fleet, personnel with expiring documents, work orders, signed tickets, invoices, collections and HSE records,
/// so every screen has meaningful data on a fresh installation. Runs only when the company does not exist.
/// </summary>
internal sealed class DemoTenantSeeder(
    BrocalDbContext db,
    ITenantContext tenantContext,
    IFileService files,
    IPacGateway pac,
    TimeProvider timeProvider,
    ILogger<DemoTenantSeeder> logger)
{
    public const string Slug = "demo";

    /// <summary>Sample dates are local times of Tabasco (UTC-6).</summary>
    private static readonly TimeSpan Local = TimeSpan.FromHours(-6);

    public async Task SeedAsync(string? adminEmail, CancellationToken cancellationToken)
    {
        if (await db.Tenants.AnyAsync(t => t.Slug == Slug, cancellationToken))
        {
            return;
        }

        logger.LogInformation("Creando la empresa de demostración '{Slug}'", Slug);
        var tenant = Tenant.Create(Slug, "Servicios Petroleros del Sureste (demo)", "Empresarial", BrocalModules.All.Select(m => m.Code)).Value;
        tenant.UpdateProfile(tenant.Name, "SERVICIOS PETROLEROS DEL SURESTE SA DE CV", "SPS120315AB1", "contacto@sps-demo.mx", "+52 993 000 0000", "Empresarial");
        tenant.UpdateBranding(TenantBranding.Default("Servicios Petroleros del Sureste") with
        {
            UseChronoLithTheme = false,
            Palette = new ThemePalette("#0F4C5C", "#1B998B", "#F4A259", "#5E6872", "#C8553D", "#2D936C", "#E9C46A", "#3A86FF"),
            Tagline = "Transporte de fluidos y bombeo · Tabasco",
            DocumentFooter = "Servicios Petroleros del Sureste · Av. Ruiz Cortines 1200, Villahermosa, Tab. · Documento generado con Brocal"
        });
        tenant.UpdateFiscalProfile(new TenantFiscalProfile("SPS120315AB1", "SERVICIOS PETROLEROS DEL SURESTE", "601", "86035", "A", "P", TenantFiscalProfile.SimulatedPac, null, null, true));
        tenant.UpdateQuotas(TenantQuotas.Unlimited);
        db.Tenants.Add(tenant);

        using (tenantContext.BeginScope(Application.Abstractions.TenantSnapshot.From(tenant)))
        {
            TenantStarterKit.Apply(db);
            if (!string.IsNullOrWhiteSpace(adminEmail))
            {
                db.Memberships.Add(TenantMembership.Invite(adminEmail, "Administrador de la demo", true, []).Value);
            }

            await db.SaveChangesAsync(cancellationToken);
            await SeedOperationAsync(tenant, cancellationToken);
        }
    }

    private async Task SeedOperationAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        // ---- Customers, contracts, sites -------------------------------------------------------------------
        var pemex = Customer.Create(new CustomerData("PEP", "Pemex Exploración y Producción", "PEMEX EXPLORACION Y PRODUCCION", "PEP920716AB1", "603", "86030", "G03", "99", "PPD", 60,
            "facturacion.pep@demo.mx", "+52 993 310 0000", "Av. Adolfo Ruiz Cortines s/n, Villahermosa, Tab.", "#0B6E4F", null, true,
            [new CustomerContact("Ing. Rocío Pérez", "Residente de obra", "rocio.perez@demo.mx", "+52 993 111 2233", true, true, false)], null)).Value;
        var operadora = Customer.Create(new CustomerData("OPG", "Operadora Petrolera del Golfo", "OPERADORA PETROLERA DEL GOLFO SA DE CV", "OPG150101XY2", "601", "86100", "G03", "03", "PUE", 30,
            "cuentasxpagar@opg-demo.mx", null, null, "#7B2CBF", null, false,
            [new CustomerContact("Lic. Mario Gómez", "Cuentas por pagar", "mario.gomez@opg-demo.mx", null, false, true, true)], null)).Value;
        db.Customers.AddRange(pemex, operadora);

        var contract = Contract.Create(new ContractData("640-SVC-2026", "Servicios de transporte de fluidos y bombeo", pemex.Id, today.AddMonths(-8), today.AddMonths(16), 48_000_000m, "MXN",
            ContractStatus.Active, "Ing. Rocío Pérez", null)).Value;
        db.Contracts.Add(contract);

        var sites = new[]
        {
            Site.Create(new SiteData("SEN-101", "Pozo Sen-101", SiteType.Well, pemex.Id, "Campo Sen", "Nacajuca", "Tabasco", "86220", 18.2312m, -93.0154m, null, null)).Value,
            Site.Create(new SiteData("SAM-845", "Pozo Samaria-845", SiteType.Well, pemex.Id, "Campo Samaria", "Cunduacán", "Tabasco", "86690", 18.0645m, -93.2401m, null, null)).Value,
            Site.Create(new SiteData("CUN-12", "Pozo Cunduacán-12", SiteType.Well, pemex.Id, "Campo Cunduacán", "Cunduacán", "Tabasco", "86690", 18.0521m, -93.1702m, null, null)).Value,
            Site.Create(new SiteData("BAT-PAR", "Batería Paredón", SiteType.Facility, operadora.Id, "Campo Paredón", "Huimanguillo", "Tabasco", "86400", 17.8473m, -93.3856m, null, null)).Value,
            Site.Create(new SiteData("PATIO-VHSA", "Patio Villahermosa", SiteType.Yard, null, null, "Centro", "Tabasco", "86035", 17.9895m, -92.9475m, null, null)).Value
        };
        db.Sites.AddRange(sites);

        // ---- Services, prices, service types ---------------------------------------------------------------
        var rental = Service.Create(new ServiceData("AT-DIA", "Renta de autotanque de 20 m³ con operador", "Incluye operador y combustible.", "Transporte", "Día", "78101800", "DAY", "02", 0.16m, 9_500m, null)).Value;
        var transport = Service.Create(new ServiceData("TRANS-M3", "Transporte de agua congénita", "Por metro cúbico transportado.", "Transporte", "m³", "78101800", "MTQ", "02", 0.16m, 185m, null)).Value;
        var pumping = Service.Create(new ServiceData("BOMB-HP", "Servicio de bombeo de alta presión", "Unidad de bombeo con cuadrilla.", "Bombeo", "Hora", "71121000", "HUR", "02", 0.16m, 4_200m, null)).Value;
        var cleaning = Service.Create(new ServiceData("LIMP-PRESA", "Limpieza de presa de lodos", "Incluye manejo de residuos.", "Ambiental", "Servicio", "71121000", "E48", "02", 0.16m, 28_000m, null)).Value;
        var water = Service.Create(new ServiceData("AGUA-TRAT", "Suministro de agua tratada", null, "Suministro", "m³", "83101500", "MTQ", "02", 0.16m, 95m, null)).Value;
        db.Services.AddRange(rental, transport, pumping, cleaning, water);

        db.PriceLists.Add(PriceList.Create(new PriceListData("GRAL-2026", "Tarifario general 2026", null, null, "MXN", new DateOnly(today.Year, 1, 1), null,
        [
            new(rental.Id, 9_500m, null), new(transport.Id, 185m, null), new(pumping.Id, 4_200m, null), new(cleaning.Id, 28_000m, null), new(water.Id, 95m, null)
        ], null)).Value);
        db.PriceLists.Add(PriceList.Create(new PriceListData("PEP-640", "Tarifas contrato 640-SVC-2026", pemex.Id, contract.Id, "MXN", contract.StartDate, contract.EndDate,
        [
            new(rental.Id, 8_750m, "Precio unitario del anexo C"), new(transport.Id, 168.5m, null), new(pumping.Id, 3_980m, null), new(cleaning.Id, 26_400m, null)
        ], null)).Value);

        var fluidTransport = ServiceType.Create(new ServiceTypeData("TF", "Transporte de fluidos", "Traslado de agua congénita, agua tratada o diésel en autotanque.", "local_shipping", "#1B998B", true, 0,
        [
            new FormFieldDefinition("fluido", "Fluido", FormFieldType.Select, true, ["Agua congénita", "Agua tratada", "Diésel", "Salmuera", "Lodo base agua"], null, 1),
            new FormFieldDefinition("volumen", "Volumen transportado", FormFieldType.Number, true, null, "m³", 2),
            new FormFieldDefinition("viajes", "Número de viajes", FormFieldType.Number, true, null, null, 3),
            new FormFieldDefinition("origen", "Origen", FormFieldType.Text, true, null, null, 4),
            new FormFieldDefinition("destino", "Destino", FormFieldType.Text, true, null, null, 5),
            new FormFieldDefinition("densidad", "Densidad", FormFieldType.Number, false, null, "g/cm³", 6)
        ], [transport.Id], null)).Value;
        var highPressure = ServiceType.Create(new ServiceTypeData("BAP", "Bombeo de alta presión", "Pruebas de hermeticidad, inducciones y bombeo a pozo.", "water_pump", "#3A86FF", true, 0,
        [
            new FormFieldDefinition("presion", "Presión máxima", FormFieldType.Number, true, null, "psi", 1),
            new FormFieldDefinition("gasto", "Gasto", FormFieldType.Number, false, null, "bpm", 2),
            new FormFieldDefinition("horas", "Horas de bombeo", FormFieldType.Number, true, null, "h", 3),
            new FormFieldDefinition("hermeticidad", "Prueba de hermeticidad satisfactoria", FormFieldType.Checkbox, false, null, null, 4)
        ], [pumping.Id], null)).Value;
        var pitCleaning = ServiceType.Create(new ServiceTypeData("LP", "Limpieza de presas", "Retiro de lodos y recortes con manifiesto de residuos.", "cleaning_services", "#F4A259", true, 0,
        [
            new FormFieldDefinition("volumen", "Volumen retirado", FormFieldType.Number, true, null, "m³", 1),
            new FormFieldDefinition("residuo", "Tipo de residuo", FormFieldType.Select, true, ["Lodo base agua", "Lodo base aceite", "Recortes"], null, 2),
            new FormFieldDefinition("manifiesto", "Manifiesto SEMARNAT", FormFieldType.Text, false, null, null, 3)
        ], [cleaning.Id], null)).Value;
        db.ServiceTypes.AddRange(fluidTransport, highPressure, pitCleaning);

        // ---- Personnel and crews ---------------------------------------------------------------------------
        var people = new[]
        {
            Employee.Create(new EmployeeData("E-001", "Juan Carlos", "Hernández López", null, "HELJ850312AB1", null, "Operador de autotanque", EmployeeCategory.Driver, today.AddYears(-6), "+52 993 100 0001", null, null, "O+", "LFT-123456", "E", today.AddDays(200), 95m, null, null)).Value,
            Employee.Create(new EmployeeData("E-002", "Miguel Ángel", "Torres Ramírez", null, "TORM900101CD2", null, "Operador de autotanque", EmployeeCategory.Driver, today.AddYears(-3), "+52 993 100 0002", null, null, "A+", "LFT-223344", "E", today.AddDays(18), 95m, null, null)).Value,
            Employee.Create(new EmployeeData("E-003", "Luis Fernando", "Pérez Cruz", null, "PECL880505EF3", null, "Supervisor de campo", EmployeeCategory.Supervisor, today.AddYears(-8), "+52 993 100 0003", null, null, "B+", null, null, null, 160m, null, null)).Value,
            Employee.Create(new EmployeeData("E-004", "Roberto", "Sánchez Díaz", null, null, null, "Operador de bombeo", EmployeeCategory.Operator, today.AddYears(-2), null, null, null, "O-", null, null, null, 110m, null, null)).Value,
            Employee.Create(new EmployeeData("E-005", "José Antonio", "Martínez Gil", null, null, null, "Ayudante general", EmployeeCategory.Helper, today.AddMonths(-10), null, null, null, "O+", null, null, null, 70m, null, null)).Value,
            Employee.Create(new EmployeeData("E-006", "Ana Laura", "Domínguez Ruiz", null, null, null, "Coordinadora HSE", EmployeeCategory.Hse, today.AddYears(-4), null, "ana.dominguez@sps-demo.mx", null, "A-", null, null, null, 140m, null, null)).Value
        };
        db.Employees.AddRange(people);

        var transportCrew = Crew.Create(new CrewData("CUAD-01", "Cuadrilla 1 · Transporte", people[2].Id, "Diurno", [people[0].Id, people[1].Id, people[4].Id], [], null)).Value;
        var pumpingCrew = Crew.Create(new CrewData("CUAD-02", "Cuadrilla 2 · Bombeo", people[2].Id, "Diurno", [people[3].Id, people[4].Id], [], null)).Value;
        db.Crews.AddRange(transportCrew, pumpingCrew);

        // ---- Fleet -------------------------------------------------------------------------------------------
        Unit TankTruck(int n, int year, decimal km) => Unit.Create(new UnitData($"AT-{n:00}", $"Autotanque Kenworth T800 20 m³ #{n}", UnitType.TankTruck, "Kenworth", "T800", year, $"WX-{4200 + n}-B",
            $"3WKDD40X{n}F00{n}123", MeterKind.Odometer, 20, "m³", "Diésel", 400, 2.6m, "C3", 26, "TPAF02", $"0927SPS{n:000}", "Seguros del Golfo", $"RC-{90000 + n}", "Seguros del Golfo", $"AMB-{70000 + n}",
            people[n % 2].Id, null, new DateOnly(year, 3, 1), 2_350_000m, null)).Value;

        var units = new List<Unit> { TankTruck(1, 2021, 182_400), TankTruck(2, 2020, 210_950), TankTruck(3, 2022, 98_300), TankTruck(4, 2019, 305_100) };
        units.Add(Unit.Create(new UnitData("BOM-01", "Unidad de bombeo de alta presión 1,200 HP", UnitType.PumpUnit, "Stewart & Stevenson", "SPS-1200", 2018, "WX-5510-B", null, MeterKind.HourMeter,
            null, null, "Diésel", 600, 38m, "C3", 30, "TPAF02", "0927SPS501", "Seguros del Golfo", "RC-95001", null, null, people[3].Id, null, null, null, null)).Value);
        units.Add(Unit.Create(new UnitData("VAC-01", "Camión de vacío 15 m³", UnitType.VacuumTruck, "International", "7400", 2020, "WX-5620-B", null, MeterKind.Both,
            15, "m³", "Diésel", 300, 2.9m, "C2", 18, "TPAF02", "0927SPS601", "Seguros del Golfo", "RC-96001", "Seguros del Golfo", "AMB-76001", null, null, null, null, null)).Value);
        units.Add(Unit.Create(new UnitData("CAM-01", "Camioneta de supervisión Hilux", UnitType.Pickup, "Toyota", "Hilux", 2023, "XYZ-123-A", null, MeterKind.Odometer,
            null, null, "Gasolina", 80, 9m, null, null, null, null, "Seguros del Golfo", "RC-97001", null, null, people[2].Id, null, null, null, null)).Value);
        db.Units.AddRange(units);

        units[0].RecordMeters(182_400, null, now);
        units[1].RecordMeters(210_950, null, now);
        units[2].RecordMeters(98_300, null, now);
        units[3].RecordMeters(305_100, null, now);
        units[4].RecordMeters(null, 4_820, now);
        units[5].RecordMeters(76_200, 2_310, now);
        units[6].RecordMeters(31_800, null, now);
        units[0].ChangeStatus(UnitStatus.Operating);
        units[1].ChangeStatus(UnitStatus.Operating);
        units[3].ChangeStatus(UnitStatus.Workshop);
        transportCrew.Update(new CrewData(transportCrew.Code, transportCrew.Name, transportCrew.LeaderId, transportCrew.Shift, transportCrew.MemberIds, [units[0].Id, units[1].Id], null));
        pumpingCrew.Update(new CrewData(pumpingCrew.Code, pumpingCrew.Name, pumpingCrew.LeaderId, pumpingCrew.Shift, pumpingCrew.MemberIds, [units[4].Id], null));

        await db.SaveChangesAsync(cancellationToken);

        // ---- Compliance documents with mixed expiries (traffic lights) ---------------------------------------
        var types = await db.DocumentTypes.ToDictionaryAsync(t => t.Code, cancellationToken);
        void UnitDoc(Unit unit, string type, int daysToExpire, string number) =>
            db.ComplianceDocuments.Add(ComplianceDocument.Create(DocumentSubject.Unit, unit.Id,
                new ComplianceDocumentData(types[type].Id, number, types[type].Authority, today.AddDays(daysToExpire - 365), today.AddDays(daysToExpire), null, null, null)).Value);
        void PersonDoc(Employee employee, string type, int daysToExpire, string issuer, decimal? hours = null) =>
            db.ComplianceDocuments.Add(ComplianceDocument.Create(DocumentSubject.Employee, employee.Id,
                new ComplianceDocumentData(types[type].Id, $"{type}-{employee.Code}", issuer, today.AddDays(daysToExpire - 365), today.AddDays(daysToExpire), hours, null, null)).Value);

        foreach (var (unit, offset) in units.Take(4).Select((u, i) => (u, i)))
        {
            UnitDoc(unit, "TC", 150 + offset * 20, $"TC-{unit.Code}");
            UnitDoc(unit, "POL-RC", offset == 1 ? 12 : 210, $"RC-{unit.Code}");
            UnitDoc(unit, "VFM", offset == 2 ? -5 : 90, $"VFM-{unit.Code}");
            UnitDoc(unit, "VER-EMI", 60 + offset * 15, $"VE-{unit.Code}");
        }

        UnitDoc(units[4], "POL-RC", 25, "RC-BOM-01");
        foreach (var (person, offset) in people.Select((p, i) => (p, i)))
        {
            PersonDoc(person, "H2S", offset == 0 ? 8 : 120 + offset * 10, "Centro de Capacitación Petrolera", 8);
            PersonDoc(person, "MED", offset == 3 ? -12 : 200, "Clínica Laboral del Sureste");
            PersonDoc(person, "SSPA", 160 - offset * 25, "Pemex", 16);
        }

        db.ComplianceRequirements.Add(ComplianceRequirement.Create(new ComplianceRequirementData("PEP-640", "Paquete documental contrato 640-SVC-2026", pemex.Id, contract.Id,
            [types["H2S"].Id, types["MED"].Id, types["SSPA"].Id], [types["TC"].Id, types["POL-RC"].Id, types["VFM"].Id],
            "Documentos que Pemex solicita para el ingreso del personal y las unidades a sus instalaciones.")).Value);

        // ---- Maintenance plans, workshop order, fuel -------------------------------------------------------
        db.MaintenancePlans.Add(MaintenancePlan.Create(new MaintenancePlanData("SERV-10K", "Servicio preventivo cada 10,000 km", MaintenanceTrigger.Kilometers, 10_000, 1_000,
            ["Cambio de aceite y filtros", "Revisión de frenos", "Engrasado general", "Revisión de llantas"], 14_500m,
            units.Take(4).Select((u, i) => new MaintenancePlanUnitData(u.Id, today.AddDays(-40 - i * 10), u.CurrentOdometer - 9_400 + i * 2_000, null)).ToList(), null)).Value);
        db.MaintenancePlans.Add(MaintenancePlan.Create(new MaintenancePlanData("SERV-250H", "Servicio a unidad de bombeo cada 250 h", MaintenanceTrigger.Hours, 250, 25,
            ["Cambio de aceite de motor", "Revisión de válvulas", "Calibración de manómetros"], 22_000m,
            [new MaintenancePlanUnitData(units[4].Id, today.AddDays(-20), null, 4_600)], null)).Value);

        var workshop = MaintenanceOrder.Open("MT-000001", 1, new MaintenanceOrderData(units[3].Id, MaintenanceType.Corrective, null, now.AddDays(-2),
            "Fuga de aire en sistema de frenos", "Válvula relevadora dañada", [new MaintenanceTask("Reemplazar válvula relevadora", false), new MaintenanceTask("Prueba de frenado", false)],
            [new MaintenancePartData(null, null, "Válvula relevadora Bendix", 1, 3_850m)], 6, 350m, "Taller interno", null, null, 0, null)).Value;
        db.MaintenanceOrders.Add(workshop);

        var odometer = 181_200m;
        foreach (var day in Enumerable.Range(0, 6))
        {
            odometer += 210 + day * 15;
            var load = FuelLoad.Create(new FuelLoadData(units[0].Id, now.AddDays(-12 + day * 2), day == 4 ? 118 : 82 + day, 24.35m, odometer, null, true, "Gasolinera Pemex Carr. Cárdenas",
                FuelPaymentMethod.Card, people[0].Id, null, null, null)).Value;
            db.FuelLoads.Add(load);
        }

        // ---- Inventory -----------------------------------------------------------------------------------
        var warehouse = await db.Warehouses.FirstAsync(cancellationToken);
        var items = new[]
        {
            InventoryItem.Create(new InventoryItemData("FIL-ACE-01", "Filtro de aceite motor Cummins", ItemCategory.SparePart, "Pieza", "Fleetguard", "LF9009", 6, 30, 480m, null)).Value,
            InventoryItem.Create(new InventoryItemData("ACE-15W40", "Aceite 15W40 (cubeta 19 l)", ItemCategory.Consumable, "Cubeta", "Mobil", null, 4, 20, 1_650m, null)).Value,
            InventoryItem.Create(new InventoryItemData("MANG-3", "Manguera de succión 3\" (tramo 6 m)", ItemCategory.SparePart, "Tramo", null, null, 2, 10, 3_200m, null)).Value,
            InventoryItem.Create(new InventoryItemData("H2S-MON", "Monitor personal de H2S", ItemCategory.Ppe, "Pieza", "Honeywell", "BW Clip", 3, 12, 6_900m, null)).Value,
            InventoryItem.Create(new InventoryItemData("LLAVE-36", "Llave stilson 36\"", ItemCategory.Tool, "Pieza", "Truper", null, 1, 6, 1_150m, null)).Value
        };
        db.InventoryItems.AddRange(items);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var (item, quantity) in items.Zip(new decimal[] { 4, 12, 3, 8, 5 }))
        {
            var stock = new StockLevel(item.Id, warehouse.Id);
            stock.Add(quantity);
            db.StockLevels.Add(stock);
            db.InventoryMovements.Add(InventoryMovement.Create(new MovementData(MovementType.Receipt, item.Id, warehouse.Id, null, quantity, item.StandardCost, now.AddDays(-30),
                "Inventario inicial", null, null, null, null, null), item.StandardCost ?? 0).Value);

            // Tools and H2S monitors in custody of the transport crew (resguardo).
            if (item.Category is ItemCategory.Tool or ItemCategory.Ppe)
            {
                var assigned = item.Category == ItemCategory.Tool ? 1 : 3;
                stock.Remove(assigned);
                db.InventoryMovements.Add(InventoryMovement.Create(new MovementData(MovementType.CrewIssue, item.Id, warehouse.Id, null, assigned, item.StandardCost, now.AddDays(-20),
                    "Resguardo cuadrilla 1", null, transportCrew.Id, null, null, null), item.StandardCost ?? 0).Value);
                db.ToolAssignments.Add(ToolAssignment.Create(new ToolAssignmentData(item.Id, warehouse.Id, assigned, item.Category == ItemCategory.Tool ? "STL-36-0021" : null,
                    transportCrew.Id, null, now.AddDays(-20), today.AddDays(item.Category == ItemCategory.Tool ? -2 : 70), "Entregado al jefe de cuadrilla")).Value);
            }
        }

        // ---- Work orders and tickets -----------------------------------------------------------------------
        var orders = new List<WorkOrder>();
        WorkOrder Order(int number, Site site, ServiceType type, string description, int startOffsetDays, int hours, Unit[] orderUnits, Crew crew, PlannedService[] planned)
        {
            var start = now.Date.AddDays(startOffsetDays).AddHours(7);
            var order = WorkOrder.Create(FolioSequence.Format(tenant.Settings.WorkOrderPrefix, number), number, new WorkOrderData(pemex.Id, contract.Id, site.Id, type.Id, description,
                WorkOrderPriority.Normal, new DateTimeOffset(start, Local), new DateTimeOffset(start.AddHours(hours), Local), orderUnits.Select(u => u.Id).ToList(), crew.Id, [],
                planned, "Presentarse con EPP completo y monitor de H2S.", $"SOL-{4400 + number}")).Value;
            orders.Add(order);
            return order;
        }

        var o1 = Order(1, sites[0], fluidTransport, "Transporte de agua congénita de Sen-101 a batería Samaria", -9, 10, [units[0], units[1]], transportCrew, [new PlannedService(transport.Id, 120)]);
        var o2 = Order(2, sites[1], highPressure, "Prueba de hermeticidad a TR 7\"", -6, 8, [units[4]], pumpingCrew, [new PlannedService(pumping.Id, 6)]);
        var o3 = Order(3, sites[2], fluidTransport, "Renta de autotanque para recuperación de fluidos", -3, 24, [units[1]], transportCrew, [new PlannedService(rental.Id, 1)]);
        var o4 = Order(4, sites[0], highPressure, "Inducción con nitrógeno (apoyo con bombeo)", 0, 9, [units[4]], pumpingCrew, [new PlannedService(pumping.Id, 8)]);
        var o5 = Order(5, sites[1], fluidTransport, "Transporte de agua tratada para preparación de fluidos", 2, 10, [units[0], units[2]], transportCrew, [new PlannedService(transport.Id, 80)]);
        var o6 = Order(6, sites[2], pitCleaning, "Limpieza de presa de lodos al término de la intervención", 5, 16, [units[5]], transportCrew, [new PlannedService(cleaning.Id, 1)]);
        foreach (var order in orders.Take(5))
        {
            order.Dispatch(now.AddDays(-10));
        }

        o1.Complete(now.AddDays(-8));
        o2.Complete(now.AddDays(-5));
        o3.Start(now.AddDays(-3));
        db.WorkOrders.AddRange(orders);
        await db.SaveChangesAsync(cancellationToken);

        var prices = new Dictionary<Guid, decimal> { [rental.Id] = 8_750m, [transport.Id] = 168.5m, [pumping.Id] = 3_980m, [cleaning.Id] = 26_400m };
        var serviceById = new[] { rental, transport, pumping, cleaning, water }.ToDictionary(s => s.Id);
        var folio = 0L;

        async Task<ServiceTicket> Ticket(WorkOrder? order, Site site, ServiceType type, DateOnly date, (Service Service, decimal Quantity)[] lines, Dictionary<string, string?> answers,
            Employee[] crew, Unit[] ticketUnits, bool sign, string? observations = null)
        {
            folio++;
            var ticket = ServiceTicket.Create(Guid.CreateVersion7(), FolioSequence.Format(tenant.Settings.TicketPrefix, folio), folio, null,
                new TicketData(pemex.Id, contract.Id, site.Id, type.Id, order?.Id, date, new DateTimeOffset(date.ToDateTime(new TimeOnly(7, 30)), Local),
                    new DateTimeOffset(date.ToDateTime(new TimeOnly(17, 0)), Local), ticketUnits.Select(u => u.Id).ToList(), order?.CrewId, crew.Select(c => c.Id).ToList(),
                    order?.Description ?? type.Description, observations, "Ing. Rocío Pérez", answers, new GeoPoint(site.Latitude ?? 0, site.Longitude ?? 0)),
                lines.Select(l => new PricedLine(l.Service.Id, l.Service.Name, l.Quantity, l.Service.UnitName, prices.GetValueOrDefault(l.Service.Id, l.Service.BasePrice ?? 0), PriceSource.Contract, l.Service.VatRate)).ToList(),
                null, crew.FirstOrDefault()?.Name ?? "Supervisor", "MXN").Value;

            if (sign)
            {
                var signature = (await files.SaveBytesAsync(DemoImages.Signature((int)folio), $"firma-{ticket.Folio}.png", "image/png", FileCategory.Signature, cancellationToken)).Value;
                ticket.Sign(type, 0, "Ing. Rocío Pérez", "Residente de obra Pemex", signature.Id, new DateTimeOffset(date.ToDateTime(new TimeOnly(17, 15)), Local),
                    new GeoPoint(site.Latitude ?? 0, site.Longitude ?? 0));
            }

            db.Tickets.Add(ticket);
            return ticket;
        }

        var t1 = await Ticket(o1, sites[0], fluidTransport, today.AddDays(-9), [(transport, 64)],
            new() { ["fluido"] = "Agua congénita", ["volumen"] = "64", ["viajes"] = "4", ["origen"] = "Pozo Sen-101", ["destino"] = "Batería Samaria II", ["densidad"] = "1.08" },
            [people[0], people[1], people[4]], [units[0], units[1]], sign: true);
        var t2 = await Ticket(o1, sites[0], fluidTransport, today.AddDays(-8), [(transport, 56)],
            new() { ["fluido"] = "Agua congénita", ["volumen"] = "56", ["viajes"] = "3", ["origen"] = "Pozo Sen-101", ["destino"] = "Batería Samaria II" },
            [people[0], people[1]], [units[0], units[1]], sign: true);
        var t3 = await Ticket(o2, sites[1], highPressure, today.AddDays(-6), [(pumping, 6.5m)],
            new() { ["presion"] = "5000", ["gasto"] = "2.5", ["horas"] = "6.5", ["hermeticidad"] = "true" },
            [people[2], people[3], people[4]], [units[4]], sign: true, "Prueba satisfactoria a 5,000 psi durante 30 minutos.");
        var t4 = await Ticket(o3, sites[2], fluidTransport, today.AddDays(-3), [(rental, 1)],
            new() { ["fluido"] = "Lodo base agua", ["volumen"] = "18", ["viajes"] = "1", ["origen"] = "Pozo Cunduacán-12", ["destino"] = "Presa de recortes" },
            [people[1]], [units[1]], sign: true);
        var t5 = await Ticket(o3, sites[2], fluidTransport, today.AddDays(-2), [(rental, 1)],
            new() { ["fluido"] = "Lodo base agua", ["volumen"] = "20", ["viajes"] = "1", ["origen"] = "Pozo Cunduacán-12" },
            [people[1]], [units[1]], sign: false);
        var older = new List<ServiceTicket>();
        for (var i = 0; i < 6; i++)
        {
            older.Add(await Ticket(null, sites[i % 3], fluidTransport, today.AddDays(-78 - i * 3), [(transport, 40 + i * 6)],
                new() { ["fluido"] = "Agua congénita", ["volumen"] = $"{40 + i * 6}", ["viajes"] = "3", ["origen"] = sites[i % 3].Name, ["destino"] = "Batería Samaria II" },
                [people[0], people[4]], [units[i % 3]], sign: true));
        }

        t1.Approve("Oficina (demo)", now.AddDays(-7));
        t2.Approve("Oficina (demo)", now.AddDays(-7));
        t3.Approve("Oficina (demo)", now.AddDays(-4));
        older.ForEach(t => t.Approve("Oficina (demo)", now.AddDays(-76)));
        await db.SaveChangesAsync(cancellationToken);

        db.Set<FolioSequence>().Add(new FolioSequence(tenant.Id, FolioKeys.Ticket, folio + 1));
        db.Set<FolioSequence>().Add(new FolioSequence(tenant.Id, FolioKeys.WorkOrder, orders.Count + 1));
        db.Set<FolioSequence>().Add(new FolioSequence(tenant.Id, FolioKeys.Maintenance, 2));

        // ---- Invoices (simulated PAC), payment and a Pemex estimate with COPADE ---------------------------
        var invoiceFolio = 0L;
        async Task<Invoice> Invoice(IReadOnlyList<ServiceTicket> tickets, int daysAgo, string? copade)
        {
            invoiceFolio++;
            var issue = today.AddDays(-daysAgo);
            var lines = tickets.SelectMany(t => t.Lines.Select(l => new InvoiceLineData(t.Id, l.ServiceId, serviceById[l.ServiceId].SatProductCode, serviceById[l.ServiceId].SatUnitCode,
                l.UnitName, $"{l.Description} · boleta {t.Folio}", l.Quantity, l.UnitPrice, 0, "02", l.VatRate))).ToList();
            var invoice = Domain.Fiscal.Invoice.CreateDraft("A", new InvoiceDraftData(pemex.Id, new InvoiceReceiver(pemex.Rfc, pemex.LegalName, pemex.TaxRegime, pemex.PostalCode, pemex.CfdiUse),
                CfdiType.Income, "99", "PPD", "MXN", 1, issue, pemex.CreditDays, lines, tickets.Select(t => t.Id).ToList(), null, null, copade, null)).Value;

            // Same CFDI the stamping flow builds, dated on the issue day, through the simulated PAC.
            var issuer = new InvoiceIssuer(tenant.Fiscal.Rfc!, tenant.Fiscal.LegalName!, tenant.Fiscal.TaxRegime!, tenant.Fiscal.PostalCode!);
            var stamp = await pac.StampAsync(tenant.Fiscal, new CfdiDocument("I", "A", invoiceFolio.ToString(System.Globalization.CultureInfo.InvariantCulture),
                new DateTimeOffset(issue.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero), invoice.PaymentForm, invoice.PaymentMethod, invoice.Currency, invoice.ExchangeRate,
                invoice.Subtotal, invoice.Discount, invoice.Vat, invoice.Total, issuer.ExpeditionPostalCode,
                new CfdiParty(issuer.Rfc, issuer.Name, issuer.TaxRegime, issuer.ExpeditionPostalCode, null),
                new CfdiParty(invoice.Receiver.Rfc, invoice.Receiver.Name, invoice.Receiver.TaxRegime!, invoice.Receiver.PostalCode, invoice.Receiver.CfdiUse),
                invoice.Lines.OrderBy(l => l.Order).Select(l => new CfdiConcept(l.SatProductCode, l.Quantity, l.SatUnitCode, l.UnitName, l.Description, l.UnitPrice, l.Amount, l.Discount,
                    l.TaxObject, l.VatRate, l.Vat)).ToList(),
                null), cancellationToken);
            var xml = await files.SaveBytesAsync(System.Text.Encoding.UTF8.GetBytes(stamp.Xml!), $"A-{invoiceFolio}_{stamp.Uuid}.xml", "application/xml", FileCategory.InvoiceXml,
                cancellationToken, nameof(Domain.Fiscal.Invoice), invoice.Id);
            invoice.MarkStamped(invoiceFolio, issuer, stamp.Uuid!, new DateTimeOffset(issue.ToDateTime(new TimeOnly(12, 0)), Local), TenantFiscalProfile.SimulatedPac, xml.Value.Id);
            foreach (var ticket in tickets)
            {
                ticket.MarkInvoiced(invoice.Id, now.AddDays(-daysAgo), false);
            }

            db.Invoices.Add(invoice);
            return invoice;
        }

        var paid = await Invoice(older.Take(3).ToList(), 70, "COP-2026-08812");
        var overdue = await Invoice(older.Skip(3).ToList(), 65, null);
        await Invoice([t1, t2], 5, null);
        db.Set<FolioSequence>().Add(new FolioSequence(tenant.Id, FolioKeys.Invoice("A"), invoiceFolio + 1));

        var partial = Math.Round(paid.Total * 0.6m, 2);
        var payment = Payment.Register(pemex.Id, now.AddDays(-12), "MXN", "03", "SPEI 0045821", "Pago parcial COPADE 08812",
            [new PaymentApplicationData(paid.Id, partial, 1, paid.Balance, true)]).Value;
        paid.ApplyPayment(partial, now.AddDays(-12));
        db.Payments.Add(payment);

        var estimate = Estimate.Create("EST-000001", 1, new EstimateData(pemex.Id, contract.Id, today.AddDays(-40), today.AddDays(-10), "Estimación 7 · transporte de fluidos",
            [t1.Id, t2.Id, t3.Id], t1.Subtotal + t2.Subtotal + t3.Subtotal, null)).Value;
        estimate.Submit(now.AddDays(-6));
        estimate.Approve(now.AddDays(-4));
        estimate.AssignCopade("COP-2026-09431", now.AddDays(-2));
        db.Estimates.Add(estimate);
        db.Set<FolioSequence>().Add(new FolioSequence(tenant.Id, FolioKeys.Estimate, 2));
        db.CollectionNotes.Add(CollectionNote.Create(new CollectionNoteData(pemex.Id, overdue.Id, null, now.AddDays(-3), ContactChannel.Phone, "Ing. Rocío Pérez",
            "Confirma que la factura está en revisión de COPADE; compromete pago en la siguiente quincena.", today.AddDays(12), today.AddDays(5))).Value);

        // ---- Quotes, HSE and expenses ------------------------------------------------------------------------
        var quote = Quote.Create("COT-000001", 1, new QuoteData(operadora.Id, null, sites[3].Id, pitCleaning.Id, today.AddDays(-5), today.AddDays(25), "MXN",
            "Limpieza de presas en Batería Paredón", "Precios más IVA. Vigencia 30 días. Pago a 30 días.",
            [new QuoteLineData(cleaning.Id, cleaning.Name, 2, "Servicio", 27_500m, 5, 0.16m), new QuoteLineData(transport.Id, transport.Name, 60, "m³", 180m, 0, 0.16m)], null)).Value;
        quote.MarkSent(now.AddDays(-5));
        db.Quotes.Add(quote);
        db.Set<FolioSequence>().Add(new FolioSequence(tenant.Id, FolioKeys.Quote, 2));

        var nearMiss = Incident.Report("INC-000001", 1, new IncidentData(now.AddDays(-15), IncidentType.NearMiss, IncidentSeverity.Low, sites[0].Id, units[0].Id, o1.Id, "Área de carga",
            [people[0].Id], "Manguera de descarga se desacopló sin presión durante la maniobra.", "Se detuvo la maniobra y se aseguró el acople.", "Abrazadera desgastada",
            null, [new CorrectiveAction("Sustituir abrazaderas de todas las mangueras", "Mantenimiento", today.AddDays(-10), ActionStatus.Done, today.AddDays(-11))], []), null, "Ana Laura Domínguez").Value;
        nearMiss.Close(now.AddDays(-10));
        var spill = Incident.Report("INC-000002", 2, new IncidentData(now.AddDays(-2), IncidentType.Spill, IncidentSeverity.Medium, sites[2].Id, units[1].Id, o3.Id, "Presa de recortes",
            [people[1].Id], "Derrame aproximado de 50 litros de lodo base agua por sobrellenado.", "Contención con material absorbente y limpieza del área.", null, null,
            [new CorrectiveAction("Instalar indicador de nivel en autotanque AT-02", "Mantenimiento", today.AddDays(10), ActionStatus.Pending, null)], []), null, "Luis Fernando Pérez").Value;
        db.Incidents.AddRange(nearMiss, spill);
        db.Set<FolioSequence>().Add(new FolioSequence(tenant.Id, FolioKeys.Incident, 3));

        db.SafetyTalks.Add(SafetyTalk.Create(new SafetyTalkData(now.AddDays(-1), "Riesgos por ácido sulfhídrico (H2S) en locación", 15, sites[0].Id, o4.Id, "Ana Laura Domínguez",
            people.Select(p => p.Id).ToList(), null, [], null)).Value);

        var expense = ExpenseReport.Create("VI-000001", 1, new ExpenseReportData(people[2].Id, o2.Id, today.AddDays(-7), today.AddDays(-5), "Supervisión de prueba de hermeticidad en Samaria-845", 3_000m,
        [
            new ExpenseItemData(today.AddDays(-7), ExpenseCategory.Meals, "Alimentos cuadrilla", "Restaurante El Pozo", 1_180m, true, null, null),
            new ExpenseItemData(today.AddDays(-6), ExpenseCategory.Lodging, "Hospedaje 1 noche", "Hotel Cunduacán", 1_350m, true, null, null),
            new ExpenseItemData(today.AddDays(-6), ExpenseCategory.Tolls, "Casetas", null, 286m, false, null, null)
        ])).Value;
        expense.Submit(now.AddDays(-4));
        db.ExpenseReports.Add(expense);
        db.Set<FolioSequence>().Add(new FolioSequence(tenant.Id, FolioKeys.Expense, 2));

        foreach (var day in Enumerable.Range(1, 5))
        {
            var date = today.AddDays(-day);
            foreach (var person in new[] { people[0], people[1], people[4] })
            {
                db.Attendance.Add(AttendanceRecord.Create(new AttendanceData(person.Id, date, AttendanceStatus.Present, transportCrew.Id, null, null, sites[day % 3].Id,
                    new DateTimeOffset(date.ToDateTime(new TimeOnly(13, 0)), Local), new DateTimeOffset(date.ToDateTime(new TimeOnly(23, 0)), Local), null, day == 2 ? 2 : 0, null, null)).Value);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await SeedDemoUsersAsync(pemex, people, cancellationToken);
        logger.LogInformation("Empresa de demostración creada con {Tickets} boletas y {Units} unidades", folio, units.Count);
    }

    /// <summary>
    /// Invitations for the sample accounts of the standalone realm (infra/keycloak/import/brocal-realm.json):
    /// they bind on first sign-in through their verified e-mail.
    /// </summary>
    private async Task SeedDemoUsersAsync(Customer pemex, Employee[] people, CancellationToken cancellationToken)
    {
        var roles = await db.Roles.ToDictionaryAsync(r => r.TemplateKey ?? r.Name, r => r.Id, cancellationToken);
        TenantMembership Member(string email, string name, string[] templates, Guid? employeeId = null, Guid? customerId = null)
        {
            var membership = TenantMembership.Invite(email, name, false, templates.Select(t => roles[t])).Value;
            membership.Update(name, false, templates.Select(t => roles[t]), customerId, employeeId);
            return membership;
        }

        db.Memberships.AddRange(
            Member("oficina@sps-demo.mx", "Patricia Oficina Demo", ["office", "billing"]),
            Member("supervisor@sps-demo.mx", people[2].Name, ["supervisor"], people[2].Id),
            Member("operador@sps-demo.mx", people[0].Name, ["operator"], people[0].Id),
            Member("direccion@sps-demo.mx", "Ricardo Director Demo", ["management"]),
            Member("residente@pemex-demo.mx", "Rocío Pérez (Pemex)", ["customer"], customerId: pemex.Id));

        await db.SaveChangesAsync(cancellationToken);
    }
}
