using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using RumiVtsController.Models;

namespace RumiVtsController
{
    internal sealed class VtsClient : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<VtsResponseEnvelope>> _pending = new();
        private ClientWebSocket? _socket;
        private Task? _receiveTask;
        private string? _envPath;

        public bool IsConnected => _socket?.State == WebSocketState.Open;
        public bool IsAuthenticated { get; private set; }
        public event Action<ApiErrorInfo>? ApiErrorReceived;
        public event Action<ModelOutlineInfo>? ModelOutlineReceived;

        public async Task ConnectAsync(Config.VtsConfig config, string envPath, CancellationToken cancellationToken)
        {
            if (_socket != null)
            {
                return;
            }

            _envPath = envPath;
            _socket = new ClientWebSocket();
            var uri = new Uri($"ws://127.0.0.1:{config.Port}");
            await _socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

            _receiveTask = Task.Run(() => ReceiveLoopAsync(cancellationToken));
            await EnsureAuthenticatedAsync(config, cancellationToken).ConfigureAwait(false);
        }

        public async Task DisconnectAsync()
        {
            IsAuthenticated = false;
            if (_socket == null)
            {
                return;
            }

            try
            {
                if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect", CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch
            {
            }
            finally
            {
                try
                {
                    _socket.Dispose();
                }
                catch
                {
                }

                _socket = null;
                _receiveTask = null;
                FailAllPending(new OperationCanceledException("VTS connection closed."));
            }
        }

        public void Dispose()
        {
            IsAuthenticated = false;
            try
            {
                _socket?.Dispose();
            }
            catch
            {
            }
        }

        public async Task SendParamsAsync(IReadOnlyList<InjectedParameterValue> parameters, bool faceFound, CancellationToken cancellationToken)
        {
            if (parameters == null)
            {
                return;
            }

            var data = new InjectParameterDataRequestData
            {
                FaceFound = faceFound,
                ParameterValues = parameters as List<InjectedParameterValue> ?? new List<InjectedParameterValue>(parameters)
            };

            await SendFireAndForgetAsync("InjectParameterDataRequest", data, cancellationToken).ConfigureAwait(false);
        }

        public Task SendNeutralParamsAsync(Config config, bool faceFound, CancellationToken cancellationToken)
        {
            var parameters = new List<InjectedParameterValue>
            {
                new() { Id = config.Eye.ParamX,          Value = 0f, Weight = 1f },
                new() { Id = config.Eye.ParamY,          Value = 0f, Weight = 1f },
                new() { Id = config.Head.ParamX,         Value = 0f, Weight = 1f },
                new() { Id = config.Head.ParamY,         Value = 0f, Weight = 1f },
                new() { Id = config.Head.ParamZ,         Value = 0f, Weight = 1f },
                new() { Id = config.Body.ParamX,         Value = 0f, Weight = 1f },
                new() { Id = config.Body.ParamY,         Value = 0f, Weight = 1f },
                new() { Id = config.Body.ParamZ,         Value = 0f, Weight = 1f },
                new() { Id = config.Face.Blink.ParamLeft,  Value = 0f, Weight = 1f },
                new() { Id = config.Face.Blink.ParamRight, Value = 0f, Weight = 1f },
                new() { Id = config.Face.Smile.ParamLeft,  Value = 0f, Weight = 1f },
                new() { Id = config.Face.Smile.ParamRight, Value = 0f, Weight = 1f },
            };
            return SendParamsAsync(parameters, faceFound, cancellationToken);
        }

        public Task MoveModelAsync(float positionX, float positionY, float rotation, float size, CancellationToken cancellationToken)
        {
            var data = new MoveModelRequestData
            {
                TimeInSeconds = 0f,
                ValuesAreRelativeToModel = false,
                PositionX = positionX,
                PositionY = positionY,
                Rotation = rotation,
                Size = size
            };
            return SendFireAndForgetAsync("MoveModelRequest", data, cancellationToken);
        }

        public Task SubscribeModelOutlineAsync(bool subscribe, CancellationToken cancellationToken)
        {
            var data = new EventSubscriptionRequestData
            {
                EventName = "ModelOutlineEvent",
                Subscribe = subscribe
            };
            return SendFireAndForgetAsync("EventSubscriptionRequest", data, cancellationToken);
        }

        public async Task<ModelInfo?> TryGetCurrentModelAsync(CancellationToken cancellationToken)
        {
            var emptyPayload = new { };
            VtsResponseEnvelope response;
            try
            {
                response = await SendRequestAsync("CurrentModelRequest", emptyPayload, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (IsUnknownMessageType(ex))
            {
                try
                {
                    response = await SendRequestAsync("GetCurrentModelRequest", emptyPayload, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }

            var modelInfo = ParseModelInfo(response.Data);
            if (!modelInfo.HasValue)
            {
                return null;
            }

            var info = modelInfo.Value;
            var needsPosition = !info.PositionX.HasValue || !info.PositionY.HasValue;
            var needsSize = !info.Size.HasValue;
            if ((needsPosition || needsSize) && !string.IsNullOrWhiteSpace(info.ModelId))
            {
                var posInfo = await TryGetModelPositionAsync(info.ModelId, cancellationToken).ConfigureAwait(false);
                if (posInfo.HasValue)
                {
                    var pos = posInfo.Value;
                    return new ModelInfo(
                        pos.PositionX ?? info.PositionX,
                        pos.PositionY ?? info.PositionY,
                        pos.Size ?? info.Size,
                        info.ModelId ?? pos.ModelId,
                        info.ModelName);
                }
            }

            return info;
        }

        public async Task<StatisticsInfo?> TryGetStatisticsAsync(CancellationToken cancellationToken)
        {
            var emptyPayload = new { };
            VtsResponseEnvelope response;
            try
            {
                response = await SendRequestAsync("StatisticsRequest", emptyPayload, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (IsUnknownMessageType(ex))
            {
                try
                {
                    response = await SendRequestAsync("GetStatisticsRequest", emptyPayload, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }

            return ParseStatistics(response.Data);
        }

        public async Task TryCreateCustomParametersAsync(IEnumerable<string> parameterIds, CancellationToken cancellationToken)
        {
            if (parameterIds == null)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameterId in parameterIds)
            {
                if (string.IsNullOrWhiteSpace(parameterId) || !seen.Add(parameterId))
                {
                    continue;
                }

                var data = new ParameterCreationRequestData
                {
                    ParameterName = parameterId,
                    Explanation = "Created by RumiVtsController",
                    Min = -1.0f,
                    Max = 1.0f,
                    DefaultValue = 0.0f
                };

                try
                {
                    await SendRequestAsync("ParameterCreationRequest", data, cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex) when (IsUnknownMessageType(ex))
                {
                    try
                    {
                        await SendRequestAsync("CreateParameterRequest", data, cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
                catch
                {
                }
            }
        }

        private async Task<ModelInfo?> TryGetModelPositionAsync(string modelId, CancellationToken cancellationToken)
        {
            object data = new { };
            if (!string.IsNullOrWhiteSpace(modelId))
            {
                data = new ModelPositionRequestData { ModelId = modelId };
            }

            VtsResponseEnvelope response;
            try
            {
                response = await SendRequestAsync("ModelPositionRequest", data, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (IsUnknownMessageType(ex))
            {
                try
                {
                    response = await SendRequestAsync("GetModelPositionRequest", data, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }

            return ParseModelInfo(response.Data);
        }

        public async Task TriggerHotkeyAsync(string hotkeyId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(hotkeyId))
            {
                return;
            }

            var data = new HotkeyTriggerRequestData { HotkeyId = hotkeyId };
            await SendFireAndForgetAsync("HotkeyTriggerRequest", data, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<AvailableHotkey>?> TryGetHotkeysInCurrentModelAsync(CancellationToken cancellationToken)
        {
            VtsResponseEnvelope response;
            try
            {
                response = await SendRequestAsync("HotkeysInCurrentModelRequest", new { }, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (IsUnknownMessageType(ex))
            {
                return null;
            }
            catch
            {
                return null;
            }

            var data = response.Data.Deserialize<HotkeysInCurrentModelResponseData>(JsonOptions);
            if (data == null || data.AvailableHotkeys.Count == 0)
            {
                return Array.Empty<AvailableHotkey>();
            }

            return data.AvailableHotkeys;
        }

        private async Task EnsureAuthenticatedAsync(Config.VtsConfig config, CancellationToken cancellationToken)
        {
            var token = EnvFile.GetToken();
            if (string.IsNullOrWhiteSpace(token))
            {
                token = await RequestAuthTokenAsync(config, cancellationToken).ConfigureAwait(false);
                SaveToken(token);
            }

            var authenticated = await AuthenticateWithRetryAsync(config, token, cancellationToken).ConfigureAwait(false);
            if (!authenticated)
            {
                Environment.SetEnvironmentVariable(EnvFile.TokenVar, null);
                token = await RequestAuthTokenAsync(config, cancellationToken).ConfigureAwait(false);
                SaveToken(token);
                authenticated = await AuthenticateWithRetryAsync(config, token, cancellationToken).ConfigureAwait(false);
            }

            if (!authenticated)
            {
                throw new InvalidOperationException("Failed to authenticate with VTS. Accept the permission prompt and retry.");
            }

            IsAuthenticated = true;
        }

        private async Task<string> RequestAuthTokenAsync(Config.VtsConfig config, CancellationToken cancellationToken)
        {
            var data = new AuthenticationTokenRequestData
            {
                PluginName = config.PluginName,
                PluginDeveloper = "Deer"
            };

            var response = await SendRequestAsync("AuthenticationTokenRequest", data, cancellationToken).ConfigureAwait(false);
            var responseData = response.Data.Deserialize<AuthenticationTokenResponseData>(JsonOptions);
            if (responseData == null || string.IsNullOrWhiteSpace(responseData.AuthenticationToken))
            {
                throw new InvalidOperationException("VTS did not return an authentication token.");
            }

            return responseData.AuthenticationToken;
        }

        private async Task<bool> AuthenticateWithRetryAsync(Config.VtsConfig config, string token, CancellationToken cancellationToken)
        {
            const int maxAttempts = 8;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (await AuthenticateAsync(config, token, cancellationToken).ConfigureAwait(false))
                {
                    return true;
                }

                await Task.Delay(750, cancellationToken).ConfigureAwait(false);
            }

            return false;
        }

        private async Task<bool> AuthenticateAsync(Config.VtsConfig config, string token, CancellationToken cancellationToken)
        {
            var data = new AuthenticationRequestData
            {
                PluginName = config.PluginName,
                PluginDeveloper = "Deer",
                AuthenticationToken = token
            };

            var response = await SendRequestAsync("AuthenticationRequest", data, cancellationToken).ConfigureAwait(false);
            var responseData = response.Data.Deserialize<AuthenticationResponseData>(JsonOptions);
            return responseData?.Authenticated ?? false;
        }

        private async Task<VtsResponseEnvelope> SendRequestAsync(string messageType, object data, CancellationToken cancellationToken)
        {
            if (_socket == null)
            {
                throw new InvalidOperationException("VTS socket not connected.");
            }

            var requestId = Guid.NewGuid().ToString("N");
            var request = new VtsRequestEnvelope
            {
                RequestId = requestId,
                MessageType = messageType,
                Data = data
            };

            var payload = JsonSerializer.Serialize(request, JsonOptions);
            var tcs = new TaskCompletionSource<VtsResponseEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[requestId] = tcs;

            await SendStringAsync(payload, cancellationToken).ConfigureAwait(false);
            using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            return await tcs.Task.ConfigureAwait(false);
        }

        private async Task SendFireAndForgetAsync(string messageType, object data, CancellationToken cancellationToken)
        {
            if (_socket == null)
            {
                throw new InvalidOperationException("VTS socket not connected.");
            }

            var request = new VtsRequestEnvelope
            {
                RequestId = Guid.NewGuid().ToString("N"),
                MessageType = messageType,
                Data = data
            };

            var payload = JsonSerializer.Serialize(request, JsonOptions);
            await SendStringAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        private async Task SendStringAsync(string payload, CancellationToken cancellationToken)
        {
            if (_socket == null)
            {
                throw new InvalidOperationException("VTS socket not connected.");
            }

            var bytes = Encoding.UTF8.GetBytes(payload);
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            if (_socket == null)
            {
                return;
            }

            var buffer = new byte[8192];
            var stream = new MemoryStream();

            try
            {
                while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    stream.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", cancellationToken)
                                .ConfigureAwait(false);
                            return;
                        }

                        stream.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    var json = Encoding.UTF8.GetString(stream.ToArray());
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        continue;
                    }

                    var envelope = JsonSerializer.Deserialize<VtsResponseEnvelope>(json, JsonOptions);
                    if (envelope == null)
                    {
                        continue;
                    }

                    if (string.Equals(envelope.MessageType, "APIError", StringComparison.OrdinalIgnoreCase))
                    {
                        var error = envelope.Data.Deserialize<ApiErrorData>(JsonOptions);
                        var message = error == null
                            ? "VTS API error: Unknown error."
                            : $"VTS API error {error.ErrorId}: {error.Message}";
                        var errorId = error?.ErrorId ?? 0;
                        ApiErrorReceived?.Invoke(new ApiErrorInfo(errorId, message));
                    }

                    if (string.Equals(envelope.MessageType, "ModelOutlineEvent", StringComparison.OrdinalIgnoreCase))
                    {
                        var outline = ParseModelOutline(envelope.Data);
                        if (outline.HasValue)
                        {
                            ModelOutlineReceived?.Invoke(outline.Value);
                        }
                    }

                    if (_pending.TryRemove(envelope.RequestId, out var tcs))
                    {
                        if (string.Equals(envelope.MessageType, "APIError", StringComparison.OrdinalIgnoreCase))
                        {
                            var error = envelope.Data.Deserialize<ApiErrorData>(JsonOptions);
                            var message = error == null
                                ? "Unknown API error."
                                : $"VTS API error {error.ErrorId}: {error.Message}";
                            tcs.TrySetException(new InvalidOperationException(message));
                        }
                        else
                        {
                            tcs.TrySetResult(envelope);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FailAllPending(ex);
            }
        }

        private void FailAllPending(Exception ex)
        {
            foreach (var entry in _pending)
            {
                if (_pending.TryRemove(entry.Key, out var tcs))
                {
                    tcs.TrySetException(ex);
                }
            }
        }

        private void SaveToken(string token)
        {
            if (string.IsNullOrWhiteSpace(_envPath))
            {
                return;
            }

            EnvFile.SaveToken(_envPath, token);
        }

        private static bool IsUnknownMessageType(InvalidOperationException ex)
        {
            if (ex.Message.Contains("UnknownMessageType", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return ex.Message.Contains("error 7", StringComparison.OrdinalIgnoreCase);
        }

        private static ModelInfo? ParseModelInfo(JsonElement data)
        {
            var positionElement = data;
            if (TryGetProperty(data, "modelPosition", out var modelPosition))
            {
                positionElement = modelPosition;
            }
            else if (TryFindPositionElement(data, out var foundPosition))
            {
                positionElement = foundPosition;
            }

            var posX = TryGetFloat(positionElement, "positionX", "posX", "x");
            var posY = TryGetFloat(positionElement, "positionY", "posY", "y");
            var size = TryGetFloat(positionElement, "size", "scale");

            if (!posX.HasValue || !posY.HasValue)
            {
                posX ??= TryGetFloat(data, "positionX", "posX", "x");
                posY ??= TryGetFloat(data, "positionY", "posY", "y");
            }

            size ??= TryGetFloat(data, "size", "scale", "modelScale");

            var modelId = TryFindString(data, "modelID", "modelId");
            var modelName = TryFindString(data, "modelName", "name");

            if (!posX.HasValue && !posY.HasValue && !size.HasValue
                && string.IsNullOrWhiteSpace(modelId)
                && string.IsNullOrWhiteSpace(modelName))
            {
                return null;
            }

            return new ModelInfo(posX, posY, size, modelId, modelName);
        }

        private static ModelOutlineInfo? ParseModelOutline(JsonElement data)
        {
            var hasCenter = TryGetOutlineCenter(data, out var centerX, out var centerY);
            var hasHull = TryGetHullPoints(data, out var points);
            if (!hasCenter && !hasHull)
            {
                return null;
            }

            if (!hasCenter && hasHull && points.Count > 0)
            {
                var minX = float.PositiveInfinity;
                var maxX = float.NegativeInfinity;
                var minY = float.PositiveInfinity;
                var maxY = float.NegativeInfinity;
                foreach (var point in points)
                {
                    minX = MathF.Min(minX, point.X);
                    maxX = MathF.Max(maxX, point.X);
                    minY = MathF.Min(minY, point.Y);
                    maxY = MathF.Max(maxY, point.Y);
                }

                centerX = (minX + maxX) * 0.5f;
                centerY = (minY + maxY) * 0.5f;
                hasCenter = true;
            }

            var hullHeight = 0.0f;
            var hasHullHeight = false;
            if (hasHull && points.Count > 0)
            {
                var minY = float.PositiveInfinity;
                var maxY = float.NegativeInfinity;
                foreach (var point in points)
                {
                    minY = MathF.Min(minY, point.Y);
                    maxY = MathF.Max(maxY, point.Y);
                }

                hullHeight = maxY - minY;
                hasHullHeight = hullHeight > 0.0f;
            }

            var maxAbs = 0.0f;
            if (hasCenter)
            {
                maxAbs = MathF.Max(maxAbs, MathF.Abs(centerX));
                maxAbs = MathF.Max(maxAbs, MathF.Abs(centerY));
            }
            if (hasHull && points.Count > 0)
            {
                foreach (var point in points)
                {
                    maxAbs = MathF.Max(maxAbs, MathF.Abs(point.X));
                    maxAbs = MathF.Max(maxAbs, MathF.Abs(point.Y));
                }
            }

            var isNormalized = maxAbs <= 10.0f;
            var windowWidth = TryGetWindowSize(data, out var windowHeight);
            return new ModelOutlineInfo(
                hasCenter,
                centerX,
                centerY,
                hasHullHeight,
                hullHeight,
                isNormalized,
                windowWidth,
                windowHeight);
        }

        private static int TryGetWindowSize(JsonElement data, out int windowHeight)
        {
            windowHeight = 0;
            var width = TryGetFloat(data, "windowWidth", "window_width", "windowSizeX", "windowResolutionX");
            var height = TryGetFloat(data, "windowHeight", "window_height", "windowSizeY", "windowResolutionY");
            if (!width.HasValue || !height.HasValue)
            {
                if (TryFindPropertyRecursive(data, "windowSize", out var windowSize)
                    || TryFindPropertyRecursive(data, "window_size", out windowSize))
                {
                    width ??= TryGetFloat(windowSize, "width", "x");
                    height ??= TryGetFloat(windowSize, "height", "y");
                }
            }

            var windowWidth = width.HasValue ? (int)Math.Round(width.Value) : 0;
            windowHeight = height.HasValue ? (int)Math.Round(height.Value) : 0;
            return windowWidth;
        }

        private static bool TryGetOutlineCenter(JsonElement data, out float centerX, out float centerY)
        {
            centerX = 0.0f;
            centerY = 0.0f;
            if (TryFindPropertyRecursive(data, "convexHullCenter", out var center)
                || TryFindPropertyRecursive(data, "convex_hull_center", out center)
                || TryFindPropertyRecursive(data, "convexHullCentre", out center))
            {
                return TryReadPoint(center, out centerX, out centerY);
            }

            return false;
        }

        private static bool TryGetHullPoints(JsonElement data, out List<(float X, float Y)> points)
        {
            points = new List<(float X, float Y)>();
            if (!TryFindPropertyRecursive(data, "convexHull", out var hull)
                && !TryFindPropertyRecursive(data, "convex_hull", out hull)
                && !TryFindPropertyRecursive(data, "convexHullPoints", out hull)
                && !TryFindPropertyRecursive(data, "convex_hull_points", out hull))
            {
                return false;
            }

            if (hull.ValueKind == JsonValueKind.Object && TryGetProperty(hull, "points", out var pointContainer))
            {
                hull = pointContainer;
            }

            if (hull.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var element in hull.EnumerateArray())
            {
                if (TryReadPoint(element, out var x, out var y))
                {
                    points.Add((x, y));
                }
            }

            return points.Count > 0;
        }

        private static bool TryReadPoint(JsonElement element, out float x, out float y)
        {
            x = 0.0f;
            y = 0.0f;

            if (element.ValueKind == JsonValueKind.Object)
            {
                var xValue = TryGetFloat(element, "x", "X");
                var yValue = TryGetFloat(element, "y", "Y");
                if (xValue.HasValue && yValue.HasValue)
                {
                    x = xValue.Value;
                    y = yValue.Value;
                    return true;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                using var enumerator = element.EnumerateArray();
                if (enumerator.MoveNext())
                {
                    var first = enumerator.Current;
                    if (enumerator.MoveNext())
                    {
                        var second = enumerator.Current;
                        var xValue = TryReadFloat(first);
                        var yValue = TryReadFloat(second);
                        if (xValue.HasValue && yValue.HasValue)
                        {
                            x = xValue.Value;
                            y = yValue.Value;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static StatisticsInfo? ParseStatistics(JsonElement data)
        {
            var width = TryGetFloat(data, "windowWidth", "windowWidthPx", "windowResolutionX", "windowSizeX");
            var height = TryGetFloat(data, "windowHeight", "windowHeightPx", "windowResolutionY", "windowSizeY");
            if (!width.HasValue && !height.HasValue)
            {
                return null;
            }

            var windowWidth = width.HasValue ? (int)Math.Round(width.Value) : (int?)null;
            var windowHeight = height.HasValue ? (int)Math.Round(height.Value) : (int?)null;
            return new StatisticsInfo(windowWidth, windowHeight);
        }

        private static float? TryGetFloat(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (TryGetProperty(element, name, out var value))
                {
                    var floatValue = TryReadFloat(value);
                    if (floatValue.HasValue)
                    {
                        return floatValue.Value;
                    }
                }
            }

            return null;
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                value = default;
                return false;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static bool TryFindPositionElement(JsonElement element, out JsonElement positionElement)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (HasPositionKeys(element))
                {
                    positionElement = element;
                    return true;
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (TryFindPositionElement(property.Value, out positionElement))
                    {
                        return true;
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (TryFindPositionElement(item, out positionElement))
                    {
                        return true;
                    }
                }
            }

            positionElement = default;
            return false;
        }

        private static bool HasPositionKeys(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var hasX = false;
            var hasY = false;
            foreach (var property in element.EnumerateObject())
            {
                if (IsPositionX(property.Name))
                {
                    hasX = true;
                }
                else if (IsPositionY(property.Name))
                {
                    hasY = true;
                }

                if (hasX && hasY)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPositionX(string name)
        {
            return name.Equals("positionX", StringComparison.OrdinalIgnoreCase)
                || name.Equals("posX", StringComparison.OrdinalIgnoreCase)
                || name.Equals("x", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPositionY(string name)
        {
            return name.Equals("positionY", StringComparison.OrdinalIgnoreCase)
                || name.Equals("posY", StringComparison.OrdinalIgnoreCase)
                || name.Equals("y", StringComparison.OrdinalIgnoreCase);
        }

        private static string? TryFindString(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (TryFindPropertyRecursive(element, name, out var value))
                {
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        var text = value.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text;
                        }
                    }
                }
            }

            return null;
        }

        private static bool TryFindPropertyRecursive(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }

                    if (TryFindPropertyRecursive(property.Value, name, out value))
                    {
                        return true;
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (TryFindPropertyRecursive(item, name, out value))
                    {
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        private static float? TryReadFloat(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Number:
                    if (element.TryGetSingle(out var number))
                    {
                        return number;
                    }

                    if (element.TryGetDouble(out var doubleValue))
                    {
                        return (float)doubleValue;
                    }

                    break;
                case JsonValueKind.String:
                    var text = element.GetString();
                    if (!string.IsNullOrWhiteSpace(text) && float.TryParse(text, out var parsed))
                    {
                        return parsed;
                    }

                    break;
            }

            return null;
        }

        internal readonly struct ModelInfo
        {
            public ModelInfo(float? positionX, float? positionY, float? size, string? modelId, string? modelName)
            {
                PositionX = positionX;
                PositionY = positionY;
                Size = size;
                ModelId = modelId;
                ModelName = modelName;
            }

            public float? PositionX { get; }
            public float? PositionY { get; }
            public float? Size { get; }
            public string? ModelId { get; }
            public string? ModelName { get; }
        }

        internal readonly struct ModelOutlineInfo
        {
            public ModelOutlineInfo(
                bool hasCenter,
                float centerX,
                float centerY,
                bool hasHullHeight,
                float hullHeight,
                bool isNormalized,
                int windowWidth,
                int windowHeight)
            {
                HasCenter = hasCenter;
                CenterX = centerX;
                CenterY = centerY;
                HasHullHeight = hasHullHeight;
                HullHeight = hullHeight;
                IsNormalized = isNormalized;
                WindowWidth = windowWidth;
                WindowHeight = windowHeight;
            }

            public bool HasCenter { get; }
            public float CenterX { get; }
            public float CenterY { get; }
            public bool HasHullHeight { get; }
            public float HullHeight { get; }
            public bool IsNormalized { get; }
            public int WindowWidth { get; }
            public int WindowHeight { get; }
        }

        internal readonly struct StatisticsInfo
        {
            public StatisticsInfo(int? windowWidth, int? windowHeight)
            {
                WindowWidth = windowWidth;
                WindowHeight = windowHeight;
            }

            public int? WindowWidth { get; }
            public int? WindowHeight { get; }
        }

        internal readonly struct ApiErrorInfo
        {
            public ApiErrorInfo(int errorId, string message)
            {
                ErrorId = errorId;
                Message = message;
            }

            public int ErrorId { get; }
            public string Message { get; }
        }
    }
}
