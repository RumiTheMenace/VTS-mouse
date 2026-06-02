using System.Text.Json;
using System.Text.Json.Serialization;

namespace RumiVtsController.Models
{
    internal sealed class VtsRequestEnvelope
    {
        [JsonPropertyName("apiName")]
        public string ApiName { get; set; } = "VTubeStudioPublicAPI";

        [JsonPropertyName("apiVersion")]
        public string ApiVersion { get; set; } = "1.0";

        [JsonPropertyName("requestID")]
        public string RequestId { get; set; } = string.Empty;

        [JsonPropertyName("messageType")]
        public string MessageType { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public object? Data { get; set; }
    }

    internal sealed class VtsResponseEnvelope
    {
        [JsonPropertyName("apiName")]
        public string ApiName { get; set; } = string.Empty;

        [JsonPropertyName("apiVersion")]
        public string ApiVersion { get; set; } = string.Empty;

        [JsonPropertyName("requestID")]
        public string RequestId { get; set; } = string.Empty;

        [JsonPropertyName("messageType")]
        public string MessageType { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public JsonElement Data { get; set; }
    }

    internal sealed class ApiErrorData
    {
        [JsonPropertyName("errorID")]
        public int ErrorId { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    internal sealed class AuthenticationTokenRequestData
    {
        [JsonPropertyName("pluginName")]
        public string PluginName { get; set; } = string.Empty;

        [JsonPropertyName("pluginDeveloper")]
        public string PluginDeveloper { get; set; } = string.Empty;
    }

    internal sealed class AuthenticationTokenResponseData
    {
        [JsonPropertyName("authenticationToken")]
        public string AuthenticationToken { get; set; } = string.Empty;
    }

    internal sealed class AuthenticationRequestData
    {
        [JsonPropertyName("pluginName")]
        public string PluginName { get; set; } = string.Empty;

        [JsonPropertyName("pluginDeveloper")]
        public string PluginDeveloper { get; set; } = string.Empty;

        [JsonPropertyName("authenticationToken")]
        public string AuthenticationToken { get; set; } = string.Empty;
    }

    internal sealed class AuthenticationResponseData
    {
        [JsonPropertyName("authenticated")]
        public bool Authenticated { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }

    internal sealed class InjectParameterDataRequestData
    {
        [JsonPropertyName("faceFound")]
        public bool FaceFound { get; set; } = true;

        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "set";

        [JsonPropertyName("parameterValues")]
        public List<InjectedParameterValue> ParameterValues { get; set; } = new();
    }

    internal sealed class InjectedParameterValue
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public float Value { get; set; }

        [JsonPropertyName("weight")]
        public float Weight { get; set; } = 1.0f;
    }

    internal sealed class ParameterCreationRequestData
    {
        [JsonPropertyName("parameterName")]
        public string ParameterName { get; set; } = string.Empty;

        [JsonPropertyName("explanation")]
        public string Explanation { get; set; } = string.Empty;

        [JsonPropertyName("min")]
        public float Min { get; set; }

        [JsonPropertyName("max")]
        public float Max { get; set; }

        [JsonPropertyName("defaultValue")]
        public float DefaultValue { get; set; }
    }

    internal sealed class ModelPositionRequestData
    {
        [JsonPropertyName("modelID")]
        public string ModelId { get; set; } = string.Empty;
    }

    internal sealed class HotkeyTriggerRequestData
    {
        [JsonPropertyName("hotkeyID")]
        public string HotkeyId { get; set; } = string.Empty;
    }

    internal sealed class HotkeysInCurrentModelResponseData
    {
        [JsonPropertyName("availableHotkeys")]
        public List<AvailableHotkey> AvailableHotkeys { get; set; } = new();
    }

    internal sealed class AvailableHotkey
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("hotkeyID")]
        public string HotkeyId { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
    }

    internal sealed class EventSubscriptionRequestData
    {
        [JsonPropertyName("eventName")]
        public string EventName { get; set; } = string.Empty;

        [JsonPropertyName("subscribe")]
        public bool Subscribe { get; set; }
    }

    internal sealed class MoveModelRequestData
    {
        [JsonPropertyName("timeInSeconds")]
        public float TimeInSeconds { get; set; }

        [JsonPropertyName("valuesAreRelativeToModel")]
        public bool ValuesAreRelativeToModel { get; set; }

        [JsonPropertyName("positionX")]
        public float PositionX { get; set; }

        [JsonPropertyName("positionY")]
        public float PositionY { get; set; }

        [JsonPropertyName("rotation")]
        public float Rotation { get; set; }

        [JsonPropertyName("size")]
        public float Size { get; set; } = -100f;
    }
}
