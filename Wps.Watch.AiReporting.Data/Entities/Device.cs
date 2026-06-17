namespace Wps.Watch.AiReporting.Data.Entities;

/// <summary>
/// Narrow Device projection. PII columns NOT declared (ImeiNumber, SimCardNumber,
/// PhoneNumber, ForwarderPhoneNumber) per v5 S-V5-MUST-28.
/// SerialNumber is included — it's a device identifier, not PII.
/// </summary>
public class Device
{
    public int DeviceId { get; set; }
    public int DeviceTypeId { get; set; }
    public int OrganizationId { get; set; }
    public int DeviceSourceId { get; set; }
    public string? Name { get; set; }
    public DateTime? DecommissionDate { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public DateTime? RetirementDate { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? SerialNumber { get; set; }
    public string? CarrierName { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int DeviceMakeId { get; set; }
    public int DeviceModelId { get; set; }
    public string? ApnInfo { get; set; }
    public bool WpsSponsoredSim { get; set; }
    public int? SimCardDataPlanId { get; set; }
    public int? PrepaidDataLimitTermId { get; set; }
    public DateTime? SimContractRenewalDate { get; set; }
    public DateTime? PrepaidDataPlanRenewal { get; set; }
    public decimal? PrepaidDataLimitGb { get; set; }
}
