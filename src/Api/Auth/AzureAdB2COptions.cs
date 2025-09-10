// src/Api/Auth/AzureAdB2COptions.cs
namespace Api.Auth;

public class AzureAdB2COptions
{
    public string Instance { get; set; } = "";      // e.g. https://<tenant>.b2clogin.com
    public string Domain { get; set; } = "";        // e.g. <tenant>.onmicrosoft.com
    public string ClientId { get; set; } = "";
    public string SignUpSignInPolicyId { get; set; } = ""; // e.g. B2C_1_susi
    public string Audience { get; set; } = "";      // usually same as ClientId
}