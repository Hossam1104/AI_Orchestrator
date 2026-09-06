using System.Security.Cryptography;
using System.Text;
using AIUsageMonitor.Application.Approvals;

namespace AIUsageMonitor.Infrastructure.Approvals;

/// <summary>
/// Trusted local V1 owner boundary. The ephemeral HMAC key and the only capability issuance path
/// live outside the normal Application orchestration assembly. Production composition registers
/// this object only through IHumanOwnerDecisionVerifier.
/// </summary>
public sealed class LocalSingleOwnerDecisionAuthority : IHumanOwnerDecisionVerifier
{
    private readonly string _ownerReference;
    private readonly string _authorityReference;
    private readonly byte[] _authorityKey = RandomNumberGenerator.GetBytes(32);

    public LocalSingleOwnerDecisionAuthority(string ownerReference, string authorityReference = "local-owner")
    {
        _ownerReference = Required(ownerReference, nameof(ownerReference));
        _authorityReference = Required(authorityReference, nameof(authorityReference));
    }

    /// <summary>
    /// Trusted composition seam for a future explicit interactive owner command. This is not an
    /// Application service and is intentionally not registered as an issuer in production DI.
    /// </summary>
    public HumanOwnerDecisionCapability IssueTrustedOwnerDecisionCapability(HumanOwnerDecisionIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var material = RandomNumberGenerator.GetBytes(32);
        var proof = ComputeProof(_ownerReference, _authorityReference, intent.IntentHash, material, _authorityKey);
        try
        {
            return new HumanOwnerDecisionCapability(
                _ownerReference,
                _authorityReference,
                intent.IntentHash,
                Convert.ToBase64String(material),
                Convert.ToBase64String(proof));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
            CryptographicOperations.ZeroMemory(proof);
        }
    }

    public bool TryVerify(
        HumanOwnerDecisionCapability capability,
        HumanOwnerDecisionIntent intent,
        out string verifiedOwnerReference)
    {
        verifiedOwnerReference = string.Empty;
        if (capability is null || intent is null ||
            !string.Equals(capability.OwnerReference, _ownerReference, StringComparison.Ordinal) ||
            !string.Equals(capability.AuthorityReference, _authorityReference, StringComparison.Ordinal) ||
            !string.Equals(capability.IntentHash, intent.IntentHash, StringComparison.Ordinal))
            return false;

        byte[]? material = null;
        byte[]? suppliedProof = null;
        byte[]? expectedProof = null;
        try
        {
            material = Convert.FromBase64String(capability.Material);
            suppliedProof = Convert.FromBase64String(capability.Proof);
            if (material.Length != 32 || suppliedProof.Length != 32)
                return false;

            expectedProof = ComputeProof(
                capability.OwnerReference,
                capability.AuthorityReference,
                intent.IntentHash,
                material,
                _authorityKey);
            if (!CryptographicOperations.FixedTimeEquals(suppliedProof, expectedProof))
                return false;

            verifiedOwnerReference = _ownerReference;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        finally
        {
            if (material is not null)
                CryptographicOperations.ZeroMemory(material);
            if (suppliedProof is not null)
                CryptographicOperations.ZeroMemory(suppliedProof);
            if (expectedProof is not null)
                CryptographicOperations.ZeroMemory(expectedProof);
        }
    }

    private static byte[] ComputeProof(
        string ownerReference,
        string authorityReference,
        string intentHash,
        byte[] material,
        byte[] authorityKey)
    {
        var binding = Encoding.UTF8.GetBytes($"{ownerReference}\u001f{authorityReference}\u001f{intentHash}");
        var payload = new byte[binding.Length + material.Length];
        Buffer.BlockCopy(binding, 0, payload, 0, binding.Length);
        Buffer.BlockCopy(material, 0, payload, binding.Length, material.Length);
        using var hmac = new HMACSHA256(authorityKey);
        return hmac.ComputeHash(payload);
    }

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("The configured owner authority value is required.", parameterName);
        var normalized = value.Trim();
        if (normalized.Any(static character => char.IsControl(character)))
            throw new ArgumentException("The configured owner authority value cannot contain control characters.", parameterName);
        return normalized;
    }
}
