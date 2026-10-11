using System.Globalization;
using System.Xml.Linq;
using ChronoLith.Brocal.Application.Abstractions;

namespace ChronoLith.Brocal.Infrastructure.Fiscal;

/// <summary>
/// Builds the CFDI 4.0 XML (ingreso, traslado or pago) with the Carta Porte 3.1 and Pagos 2.0 complements.
/// Seal and certificate are left to the PAC (the "timbrado" adds the TimbreFiscalDigital).
/// </summary>
internal static class CfdiXmlBuilder
{
    private static readonly XNamespace Cfdi = "http://www.sat.gob.mx/cfd/4";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly XNamespace Tfd = "http://www.sat.gob.mx/TimbreFiscalDigital";
    private static readonly XNamespace CartaPorte = "http://www.sat.gob.mx/CartaPorte31";
    private static readonly XNamespace Pagos = "http://www.sat.gob.mx/Pagos20";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static XDocument Build(CfdiDocument document)
    {
        var schemaLocation = "http://www.sat.gob.mx/cfd/4 http://www.sat.gob.mx/sitio_internet/cfd/4/cfdv40.xsd";
        if (document.CartaPorte is not null)
        {
            schemaLocation += " http://www.sat.gob.mx/CartaPorte31 http://www.sat.gob.mx/sitio_internet/cfd/CartaPorte/CartaPorte31.xsd";
        }

        if (document.Payment is not null)
        {
            schemaLocation += " http://www.sat.gob.mx/Pagos20 http://www.sat.gob.mx/sitio_internet/cfd/Pagos/Pagos20.xsd";
        }

        var isAmountless = document.Type is "T" or "P";
        var comprobante = new XElement(Cfdi + "Comprobante",
            new XAttribute(XNamespace.Xmlns + "cfdi", Cfdi),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
            new XAttribute(Xsi + "schemaLocation", schemaLocation),
            new XAttribute("Version", "4.0"),
            new XAttribute("Serie", document.Series),
            new XAttribute("Folio", document.Folio),
            new XAttribute("Fecha", document.Date.ToString("yyyy-MM-ddTHH:mm:ss", Invariant)),
            new XAttribute("Sello", string.Empty),
            new XAttribute("NoCertificado", string.Empty),
            new XAttribute("Certificado", string.Empty),
            isAmountless ? null : new XAttribute("FormaPago", document.PaymentForm ?? "99"),
            new XAttribute("SubTotal", Amount(isAmountless ? 0 : document.Subtotal, document.Type)),
            document.Discount > 0 && !isAmountless ? new XAttribute("Descuento", Amount(document.Discount, document.Type)) : null,
            new XAttribute("Moneda", isAmountless ? "XXX" : document.Currency),
            !isAmountless && document.Currency != "MXN" ? new XAttribute("TipoCambio", document.ExchangeRate.ToString("0.######", Invariant)) : null,
            new XAttribute("Total", Amount(isAmountless ? 0 : document.Total, document.Type)),
            new XAttribute("TipoDeComprobante", document.Type),
            new XAttribute("Exportacion", "01"),
            isAmountless ? null : new XAttribute("MetodoPago", document.PaymentMethod ?? "PPD"),
            new XAttribute("LugarExpedicion", document.ExpeditionPostalCode));

        comprobante.Add(new XElement(Cfdi + "Emisor",
            new XAttribute("Rfc", document.Issuer.Rfc),
            new XAttribute("Nombre", document.Issuer.Name),
            new XAttribute("RegimenFiscal", document.Issuer.TaxRegime)));

        comprobante.Add(new XElement(Cfdi + "Receptor",
            new XAttribute("Rfc", document.Receiver.Rfc),
            new XAttribute("Nombre", document.Receiver.Name),
            new XAttribute("DomicilioFiscalReceptor", document.Receiver.PostalCode ?? document.ExpeditionPostalCode),
            new XAttribute("RegimenFiscalReceptor", document.Receiver.TaxRegime),
            new XAttribute("UsoCFDI", document.Type == "P" ? "CP01" : document.Type == "T" ? "S01" : document.Receiver.CfdiUse ?? "G03")));

        var conceptos = new XElement(Cfdi + "Conceptos");
        foreach (var concept in document.Concepts)
        {
            var element = new XElement(Cfdi + "Concepto",
                new XAttribute("ClaveProdServ", concept.SatProductCode),
                new XAttribute("Cantidad", concept.Quantity.ToString("0.######", Invariant)),
                new XAttribute("ClaveUnidad", concept.SatUnitCode),
                new XAttribute("Unidad", concept.UnitName),
                new XAttribute("Descripcion", concept.Description),
                new XAttribute("ValorUnitario", isAmountless ? "0" : concept.UnitPrice.ToString("0.00####", Invariant)),
                new XAttribute("Importe", isAmountless ? "0" : Amount(concept.Amount, document.Type)),
                concept.Discount > 0 && !isAmountless ? new XAttribute("Descuento", Amount(concept.Discount, document.Type)) : null,
                new XAttribute("ObjetoImp", isAmountless ? "01" : concept.TaxObject));

            if (!isAmountless && concept.TaxObject == "02")
            {
                element.Add(new XElement(Cfdi + "Impuestos",
                    new XElement(Cfdi + "Traslados",
                        Traslado(concept.Amount - concept.Discount, concept.VatRate, concept.Vat))));
            }

            conceptos.Add(element);
        }

        comprobante.Add(conceptos);

        if (!isAmountless)
        {
            var taxed = document.Concepts.Where(c => c.TaxObject == "02").GroupBy(c => c.VatRate).ToList();
            if (taxed.Count > 0)
            {
                comprobante.Add(new XElement(Cfdi + "Impuestos",
                    new XAttribute("TotalImpuestosTrasladados", Amount(document.Vat, document.Type)),
                    new XElement(Cfdi + "Traslados",
                        taxed.Select(g => Traslado(g.Sum(c => c.Amount - c.Discount), g.Key, g.Sum(c => c.Vat))))));
            }
        }

        var complemento = new XElement(Cfdi + "Complemento");
        if (document.CartaPorte is { } cp)
        {
            complemento.Add(BuildCartaPorte(cp));
        }

        if (document.Payment is { } payment)
        {
            complemento.Add(BuildPayment(payment));
        }

        if (complemento.HasElements)
        {
            comprobante.Add(complemento);
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null), comprobante);
    }

    /// <summary>Adds the TimbreFiscalDigital produced by the PAC (or the simulated one).</summary>
    public static void AddStamp(XDocument document, string uuid, DateTimeOffset stampedAt, string pacRfc, bool simulated)
    {
        var root = document.Root!;
        var complemento = root.Element(Cfdi + "Complemento");
        if (complemento is null)
        {
            complemento = new XElement(Cfdi + "Complemento");
            root.Add(complemento);
        }

        complemento.Add(new XElement(Tfd + "TimbreFiscalDigital",
            new XAttribute(XNamespace.Xmlns + "tfd", Tfd),
            new XAttribute(Xsi + "schemaLocation", "http://www.sat.gob.mx/TimbreFiscalDigital http://www.sat.gob.mx/sitio_internet/cfd/TimbreFiscalDigital/TimbreFiscalDigitalv11.xsd"),
            new XAttribute("Version", "1.1"),
            new XAttribute("UUID", uuid),
            new XAttribute("FechaTimbrado", stampedAt.ToString("yyyy-MM-ddTHH:mm:ss", Invariant)),
            new XAttribute("RfcProvCertif", pacRfc),
            new XAttribute("SelloCFD", simulated ? "SIMULADO" : string.Empty),
            new XAttribute("NoCertificadoSAT", simulated ? "00000000000000000000" : string.Empty),
            new XAttribute("SelloSAT", simulated ? "SIMULADO-SIN-VALIDEZ-FISCAL" : string.Empty)));
    }

    private static XElement Traslado(decimal baseAmount, decimal rate, decimal amount) => new(Cfdi + "Traslado",
        new XAttribute("Base", baseAmount.ToString("0.00", Invariant)),
        new XAttribute("Impuesto", "002"),
        new XAttribute("TipoFactor", "Tasa"),
        new XAttribute("TasaOCuota", rate.ToString("0.000000", Invariant)),
        new XAttribute("Importe", amount.ToString("0.00", Invariant)));

    private static XElement BuildCartaPorte(CfdiCartaPorte cp)
    {
        var mercancias = new XElement(CartaPorte + "Mercancias",
            new XAttribute("PesoBrutoTotal", cp.TotalWeightKg.ToString("0.000", Invariant)),
            new XAttribute("UnidadPeso", "KGM"),
            new XAttribute("NumTotalMercancias", cp.Goods.Count));

        foreach (var goods in cp.Goods)
        {
            mercancias.Add(new XElement(CartaPorte + "Mercancia",
                new XAttribute("BienesTransp", goods.SatGoodsCode),
                new XAttribute("Descripcion", goods.Description),
                new XAttribute("Cantidad", goods.Quantity.ToString("0.######", Invariant)),
                new XAttribute("ClaveUnidad", goods.SatUnitCode),
                new XAttribute("MaterialPeligroso", goods.IsHazardous ? "Sí" : "No"),
                goods.IsHazardous ? new XAttribute("CveMaterialPeligroso", goods.HazardousCode ?? string.Empty) : null,
                goods.IsHazardous && goods.PackagingCode is not null ? new XAttribute("Embalaje", goods.PackagingCode) : null,
                new XAttribute("PesoEnKg", goods.WeightKg.ToString("0.000", Invariant))));
        }

        var autotransporte = new XElement(CartaPorte + "Autotransporte",
            new XAttribute("PermSCT", cp.SctPermitType),
            new XAttribute("NumPermisoSCT", cp.SctPermitNumber),
            new XElement(CartaPorte + "IdentificacionVehicular",
                new XAttribute("ConfigVehicular", cp.VehicleConfig),
                new XAttribute("PesoBrutoVehicular", cp.GrossWeightTons.ToString("0.##", Invariant)),
                new XAttribute("PlacaVM", cp.Plates),
                new XAttribute("AnioModeloVM", cp.ModelYear)),
            new XElement(CartaPorte + "Seguros",
                new XAttribute("AseguraRespCivil", cp.Insurer),
                new XAttribute("PolizaRespCivil", cp.Policy),
                cp.EnvironmentalInsurer is null ? null : new XAttribute("AseguraMedAmbiente", cp.EnvironmentalInsurer),
                cp.EnvironmentalPolicy is null ? null : new XAttribute("PolizaMedAmbiente", cp.EnvironmentalPolicy)));

        if (cp.TrailerPlates is not null)
        {
            autotransporte.Add(new XElement(CartaPorte + "Remolques",
                new XElement(CartaPorte + "Remolque", new XAttribute("SubTipoRem", "CTR004"), new XAttribute("Placa", cp.TrailerPlates))));
        }

        mercancias.Add(autotransporte);

        return new XElement(CartaPorte + "CartaPorte",
            new XAttribute(XNamespace.Xmlns + "cartaporte31", CartaPorte),
            new XAttribute("Version", "3.1"),
            new XAttribute("IdCCP", cp.IdCcp),
            new XAttribute("TranspInternac", "No"),
            new XAttribute("TotalDistRec", cp.DistanceKm.ToString("0.##", Invariant)),
            new XElement(CartaPorte + "Ubicaciones",
                Location("Origen", "OR000001", cp.OriginRfc, cp.Departure, null, cp.OriginState, cp.OriginPostalCode),
                Location("Destino", "DE000001", cp.DestinationRfc, cp.Arrival, cp.DistanceKm, cp.DestinationState, cp.DestinationPostalCode)),
            mercancias,
            new XElement(CartaPorte + "FiguraTransporte",
                new XElement(CartaPorte + "TiposFigura",
                    new XAttribute("TipoFigura", "01"),
                    new XAttribute("RFCFigura", cp.DriverRfc),
                    new XAttribute("NumLicencia", cp.DriverLicense),
                    new XAttribute("NombreFigura", cp.DriverName))));
    }

    private static XElement Location(string type, string id, string? rfc, DateTimeOffset at, decimal? distance, string state, string postalCode) => new(
        CartaPorte + "Ubicacion",
        new XAttribute("TipoUbicacion", type),
        new XAttribute("IDUbicacion", id),
        new XAttribute("RFCRemitenteDestinatario", string.IsNullOrWhiteSpace(rfc) ? "XAXX010101000" : rfc),
        new XAttribute("FechaHoraSalidaLlegada", at.ToString("yyyy-MM-ddTHH:mm:ss", Invariant)),
        distance is null ? null : new XAttribute("DistanciaRecorrida", distance.Value.ToString("0.##", Invariant)),
        new XElement(CartaPorte + "Domicilio",
            new XAttribute("Estado", state),
            new XAttribute("Pais", "MEX"),
            new XAttribute("CodigoPostal", postalCode)));

    private static XElement BuildPayment(CfdiPayment payment)
    {
        var vatBase = payment.Documents.Sum(d => d.VatBase);
        var vat = payment.Documents.Sum(d => d.Vat);
        var pago = new XElement(Pagos + "Pago",
            new XAttribute("FechaPago", payment.PaidAt.ToString("yyyy-MM-ddTHH:mm:ss", Invariant)),
            new XAttribute("FormaDePagoP", payment.PaymentForm),
            new XAttribute("MonedaP", payment.Currency),
            new XAttribute("TipoCambioP", "1"),
            new XAttribute("Monto", payment.Amount.ToString("0.00", Invariant)));

        foreach (var document in payment.Documents)
        {
            pago.Add(new XElement(Pagos + "DoctoRelacionado",
                new XAttribute("IdDocumento", document.Uuid),
                new XAttribute("Serie", document.Series),
                new XAttribute("Folio", document.Folio),
                new XAttribute("MonedaDR", document.Currency),
                new XAttribute("EquivalenciaDR", "1"),
                new XAttribute("NumParcialidad", document.Partiality),
                new XAttribute("ImpSaldoAnt", document.PreviousBalance.ToString("0.00", Invariant)),
                new XAttribute("ImpPagado", document.Paid.ToString("0.00", Invariant)),
                new XAttribute("ImpSaldoInsoluto", document.Remaining.ToString("0.00", Invariant)),
                new XAttribute("ObjetoImpDR", "02"),
                new XElement(Pagos + "ImpuestosDR",
                    new XElement(Pagos + "TrasladosDR",
                        new XElement(Pagos + "TrasladoDR",
                            new XAttribute("BaseDR", document.VatBase.ToString("0.00", Invariant)),
                            new XAttribute("ImpuestoDR", "002"),
                            new XAttribute("TipoFactorDR", "Tasa"),
                            new XAttribute("TasaOCuotaDR", "0.160000"),
                            new XAttribute("ImporteDR", document.Vat.ToString("0.00", Invariant)))))));
        }

        pago.Add(new XElement(Pagos + "ImpuestosP",
            new XElement(Pagos + "TrasladosP",
                new XElement(Pagos + "TrasladoP",
                    new XAttribute("BaseP", vatBase.ToString("0.00", Invariant)),
                    new XAttribute("ImpuestoP", "002"),
                    new XAttribute("TipoFactorP", "Tasa"),
                    new XAttribute("TasaOCuotaP", "0.160000"),
                    new XAttribute("ImporteP", vat.ToString("0.00", Invariant))))));

        return new XElement(Pagos + "Pagos",
            new XAttribute(XNamespace.Xmlns + "pago20", Pagos),
            new XAttribute("Version", "2.0"),
            new XElement(Pagos + "Totales",
                new XAttribute("TotalTrasladosBaseIVA16", vatBase.ToString("0.00", Invariant)),
                new XAttribute("TotalTrasladosImpuestoIVA16", vat.ToString("0.00", Invariant)),
                new XAttribute("MontoTotalPagos", payment.Amount.ToString("0.00", Invariant))),
            pago);
    }

    private static string Amount(decimal value, string type) => type is "T" or "P" ? "0" : value.ToString("0.00", Invariant);
}
