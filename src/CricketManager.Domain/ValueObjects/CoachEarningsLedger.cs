using System.Text.Json.Serialization;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Phase 12 (§4.2): a coach's worldwide earnings, split by job type. A modern coaching career is a
/// portfolio - a domestic club salary, perhaps a national contract, and a franchise campaign fee
/// or two on top - and this is the number a job offer is actually weighed against.
/// </summary>
public sealed class CoachEarningsLedger
{
    [JsonInclude] public double Domestic { get; private set; }
    [JsonInclude] public double National { get; private set; }
    [JsonInclude] public double Franchise { get; private set; }

    public double Total => Domestic + National + Franchise;

    public void CreditDomestic(double amount) => Domestic += Math.Max(0, amount);
    public void CreditNational(double amount) => National += Math.Max(0, amount);
    public void CreditFranchise(double amount) => Franchise += Math.Max(0, amount);
}
