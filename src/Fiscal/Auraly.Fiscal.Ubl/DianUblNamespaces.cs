using System.Xml.Linq;

namespace Auraly.Fiscal.Ubl;

public static class DianUblNamespaces
{
    public static readonly XNamespace Invoice = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    public static readonly XNamespace CreditNote = "urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2";
    public static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    public static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    public static readonly XNamespace Ext = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";
    public static readonly XNamespace Sts = "dian:gov:co:facturaelectronica:Structures-2-1";
    public static readonly XNamespace Ds = "http://www.w3.org/2000/09/xmldsig#";
    public static readonly XNamespace Xades = "http://uri.etsi.org/01903/v1.3.2#";
    public static readonly XNamespace Xades141 = "http://uri.etsi.org/01903/v1.4.1#";
    public static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
}