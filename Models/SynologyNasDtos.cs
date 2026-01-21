using System.Text.Json.Serialization;

namespace CrownRFEP_Reader.Models;

public class SynologyResponse<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("error")]
    public SynologyError? Error { get; set; }
}

public class SynologyError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }
}

public class SynologyApiInfoItem
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("minVersion")]
    public int MinVersion { get; set; }

    [JsonPropertyName("maxVersion")]
    public int MaxVersion { get; set; }

    [JsonPropertyName("requestFormat")]
    public string? RequestFormat { get; set; }
}

public class SynologyAuthData
{
    [JsonPropertyName("did")]
    public string? Did { get; set; }

    [JsonPropertyName("is_portal_port")]
    public bool? IsPortalPort { get; set; }

    [JsonPropertyName("sid")]
    public string Sid { get; set; } = "";

    [JsonPropertyName("synotoken")]
    public string SynoToken { get; set; } = "";
}
