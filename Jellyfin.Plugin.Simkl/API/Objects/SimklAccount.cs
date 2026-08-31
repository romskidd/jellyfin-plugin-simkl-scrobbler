using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl account information, part of the <c>/users/settings</c> response.
    /// </summary>
    public class SimklAccount
    {
        /// <summary>
        /// Gets or sets the numeric Simkl account id.
        /// </summary>
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        /// <summary>
        /// Gets or sets the account type ("free", "pro" or "vip").
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>
        /// Gets or sets the account timezone.
        /// </summary>
        [JsonPropertyName("timezone")]
        public string? Timezone { get; set; }
    }
}
