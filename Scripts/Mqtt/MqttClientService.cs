using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

#if MQTTNET_ENABLED
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
#endif

namespace ViitorCloud.Games.DigitalTwinFuturotec.Mqtt {
    /// <summary>
    /// MQTTnet-backed implementation of IMqttClientService. Compiled in real mode
    /// only when MQTTNET_ENABLED is defined - see DigitalTwinFuturotec.Mqtt.asmdef,
    /// which defines that symbol automatically once an MQTTnet assembly is present
    /// in the project (Unity "Version Defines"). Until MQTTnet is installed every
    /// member below is a logging no-op so the rest of the codebase still compiles
    /// and failures are loud instead of silent.
    ///
    /// Verified via live reflection against MQTTnet 4.3.7.1207 (the version this
    /// project has installed): MqttFactory, MqttClientOptionsBuilder,
    /// MqttApplicationMessageBuilder and all *Async event delegates match exactly.
    /// Reads ApplicationMessage.PayloadSegment rather than the (obsolete in this
    /// version) Payload property. Re-verify if the installed MQTTnet version ever
    /// changes - see docs/decisions.md for why 4.3.7.1207 specifically (5.x has no
    /// Unity-compatible build).
    /// </summary>
    public sealed class MqttClientService : IMqttClientService, IDisposable {
        public event Action Connected;
        public event Action<string> Disconnected;
        public event Action<MqttMessage> MessageReceived;
        public event Action<string> ConnectionError;

        public MqttConnectionState State { get; private set; } = MqttConnectionState.Disconnected;

        private MqttBrokerSettings settings;
        private string username;
        private string password;
        private bool intentionalDisconnect;
        private CancellationTokenSource reconnectCts;

#if MQTTNET_ENABLED
        private readonly IMqttClient mqttClient;

        public MqttClientService() {
            MqttFactory factory = new MqttFactory();
            mqttClient = factory.CreateMqttClient();
            mqttClient.ConnectedAsync += HandleConnectedAsync;
            mqttClient.DisconnectedAsync += HandleDisconnectedAsync;
            mqttClient.ApplicationMessageReceivedAsync += HandleApplicationMessageReceivedAsync;
        }
#endif

        public void Configure(MqttBrokerSettings settings, string username, string password) {
            if (settings == null) {
                MqttLog.Error("MqttClientService: Configure called with a null MqttBrokerSettings.");
                return;
            }

            this.settings = settings;
            this.username = username;
            this.password = password;
        }

        public async Task ConnectAsync(CancellationToken cancellationToken) {
#if MQTTNET_ENABLED
            if (settings == null) {
                MqttLog.Error("MqttClientService: ConnectAsync called before Configure().");
                return;
            }

            if (string.IsNullOrWhiteSpace(settings.BrokerHost)) {
                MqttLog.Error("MqttClientService: ConnectAsync called with an empty broker host.");
                return;
            }

            intentionalDisconnect = false;
            State = MqttConnectionState.Connecting;

            string clientId = settings.ClientIdPrefix + "-" + SystemInfo.deviceUniqueIdentifier;
            bool useWebSocket = IsWebSocketUri(settings.BrokerHost);
            MqttLog.Info("MqttClientService: connecting to " + (useWebSocket ? settings.BrokerHost : settings.BrokerHost + ":" + settings.BrokerPort) + " as \"" + clientId + "\" (" + (useWebSocket ? "WebSocket" : "TCP") + ")...");

            MqttClientOptionsBuilder optionsBuilder = new MqttClientOptionsBuilder()
                .WithClientId(clientId)
                .WithCleanSession(settings.CleanSession)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(settings.KeepAliveSeconds))
                .WithTimeout(TimeSpan.FromSeconds(settings.ConnectTimeoutSeconds));

            if (useWebSocket) {
                optionsBuilder = optionsBuilder.WithWebSocketServer(webSocketOptions => webSocketOptions.WithUri(settings.BrokerHost));
            } else {
                optionsBuilder = optionsBuilder.WithTcpServer(settings.BrokerHost, settings.BrokerPort);
            }

            if (!string.IsNullOrEmpty(username)) {
                optionsBuilder = optionsBuilder.WithCredentials(username, password);
            }

            if (settings.UseTls) {
                X509Certificate2 trustedCertificate = string.IsNullOrEmpty(settings.TrustedCertificatePath) ? null : LoadTrustedCertificate(settings.TrustedCertificatePath);
                bool allowUntrusted = settings.AllowUntrustedCertificates;

                optionsBuilder = optionsBuilder.WithTlsOptions(tlsOptions => {
                    tlsOptions.UseTls();

                    if (allowUntrusted) {
                        MqttLog.Warning("MqttClientService: TLS certificate validation is DISABLED (AllowUntrustedCertificates) - local testing only, never use this in production.");
                        tlsOptions.WithAllowUntrustedCertificates(true);
                        tlsOptions.WithIgnoreCertificateChainErrors(true);
                        tlsOptions.WithIgnoreCertificateRevocationErrors(true);
                    } else if (trustedCertificate != null) {
                        tlsOptions.WithCertificateValidationHandler(context => ValidateAgainstTrustedCertificate(context, trustedCertificate));
                    }
                });
            }

            MqttClientOptions options = optionsBuilder.Build();

            try {
                await mqttClient.ConnectAsync(options, cancellationToken);
            } catch (Exception exception) {
                State = MqttConnectionState.Disconnected;
                MqttLog.Error("MqttClientService: connect failed: " + exception.Message);
                ConnectionError?.Invoke(exception.Message);
                ScheduleReconnect();
            }
#else
            MqttLog.Error("MqttClientService: MQTTNET_ENABLED is not defined. Install the MQTTnet package before connecting - see docs/ai_handoff.md.");
            await Task.CompletedTask;
#endif
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken) {
#if MQTTNET_ENABLED
            intentionalDisconnect = true;
            reconnectCts?.Cancel();
            State = MqttConnectionState.Disconnecting;
            MqttLog.Info("MqttClientService: disconnecting...");

            MqttClientDisconnectOptions disconnectOptions = new MqttClientDisconnectOptionsBuilder()
                .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                .Build();

            try {
                await mqttClient.DisconnectAsync(disconnectOptions, cancellationToken);
            } catch (Exception exception) {
                MqttLog.Error("MqttClientService: disconnect failed: " + exception.Message);
            } finally {
                State = MqttConnectionState.Disconnected;
            }
#else
            await Task.CompletedTask;
#endif
        }

        public async Task PublishAsync(string topic, byte[] payload, MqttQualityOfService qualityOfService, bool retain, CancellationToken cancellationToken) {
#if MQTTNET_ENABLED
            if (State != MqttConnectionState.Connected) {
                MqttLog.Error("MqttClientService: PublishAsync(\"" + topic + "\") called while not connected. Message was dropped.");
                return;
            }

            MqttApplicationMessage message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(ConvertQualityOfService(qualityOfService))
                .WithRetainFlag(retain)
                .Build();

            try {
                await mqttClient.PublishAsync(message, cancellationToken);
                MqttLog.Info("MqttClientService: published to \"" + topic + "\" (" + (payload == null ? 0 : payload.Length) + " byte(s), " + qualityOfService + ").");
            } catch (Exception exception) {
                MqttLog.Error("MqttClientService: publish to \"" + topic + "\" failed: " + exception.Message);
                ConnectionError?.Invoke(exception.Message);
            }
#else
            await Task.CompletedTask;
#endif
        }

        public async Task SubscribeAsync(string topic, MqttQualityOfService qualityOfService, CancellationToken cancellationToken) {
#if MQTTNET_ENABLED
            if (State != MqttConnectionState.Connected) {
                MqttLog.Error("MqttClientService: SubscribeAsync(\"" + topic + "\") called while not connected.");
                return;
            }

            MqttClientSubscribeOptions subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(filter => filter.WithTopic(topic).WithQualityOfServiceLevel(ConvertQualityOfService(qualityOfService)))
                .Build();

            try {
                await mqttClient.SubscribeAsync(subscribeOptions, cancellationToken);
                MqttLog.Info("MqttClientService: subscribed to \"" + topic + "\" (" + qualityOfService + ").");
            } catch (Exception exception) {
                MqttLog.Error("MqttClientService: subscribe to \"" + topic + "\" failed: " + exception.Message);
                ConnectionError?.Invoke(exception.Message);
            }
#else
            await Task.CompletedTask;
#endif
        }

        public async Task UnsubscribeAsync(string topic, CancellationToken cancellationToken) {
#if MQTTNET_ENABLED
            MqttClientUnsubscribeOptions unsubscribeOptions = new MqttClientUnsubscribeOptionsBuilder()
                .WithTopicFilter(topic)
                .Build();

            try {
                await mqttClient.UnsubscribeAsync(unsubscribeOptions, cancellationToken);
                MqttLog.Info("MqttClientService: unsubscribed from \"" + topic + "\".");
            } catch (Exception exception) {
                MqttLog.Error("MqttClientService: unsubscribe from \"" + topic + "\" failed: " + exception.Message);
            }
#else
            await Task.CompletedTask;
#endif
        }

        public void Dispose() {
#if MQTTNET_ENABLED
            reconnectCts?.Cancel();
            reconnectCts?.Dispose();
            mqttClient.ConnectedAsync -= HandleConnectedAsync;
            mqttClient.DisconnectedAsync -= HandleDisconnectedAsync;
            mqttClient.ApplicationMessageReceivedAsync -= HandleApplicationMessageReceivedAsync;
            mqttClient.Dispose();
#endif
        }

#if MQTTNET_ENABLED
        private Task HandleConnectedAsync(MqttClientConnectedEventArgs eventArgs) {
            State = MqttConnectionState.Connected;
            MqttLog.Info("MqttClientService: connected to " + (IsWebSocketUri(settings.BrokerHost) ? settings.BrokerHost : settings.BrokerHost + ":" + settings.BrokerPort) + ".");
            Connected?.Invoke();
            return Task.CompletedTask;
        }

        /// <summary> BrokerHost doubles as either a bare hostname/IP (TCP transport) or a full ws://.../wss://... URI (WebSocket transport) - see MqttBrokerSettings.BrokerHost. </summary>
        private static bool IsWebSocketUri(string brokerHost) {
            return brokerHost.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) || brokerHost.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);
        }

        private Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs eventArgs) {
            State = MqttConnectionState.Disconnected;
            MqttLog.Warning("MqttClientService: disconnected (" + eventArgs.Reason + ")" + (intentionalDisconnect ? " - requested." : " - unexpected."));
            Disconnected?.Invoke(eventArgs.Reason.ToString());

            if (!intentionalDisconnect && settings != null && settings.AutoReconnect) {
                ScheduleReconnect();
            }

            return Task.CompletedTask;
        }

        private Task HandleApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs eventArgs) {
            ArraySegment<byte> payloadSegment = eventArgs.ApplicationMessage.PayloadSegment;
            byte[] payload = new byte[payloadSegment.Count];
            Array.Copy(payloadSegment.Array, payloadSegment.Offset, payload, 0, payloadSegment.Count);

            MqttMessage message = new MqttMessage(
                eventArgs.ApplicationMessage.Topic,
                payload,
                ConvertQualityOfService(eventArgs.ApplicationMessage.QualityOfServiceLevel),
                eventArgs.ApplicationMessage.Retain
            );
            MqttLog.Info("MqttClientService: message received on \"" + message.Topic + "\" (" + payload.Length + " byte(s)): " + TruncateForLog(message.PayloadAsString()));
            MessageReceived?.Invoke(message);
            return Task.CompletedTask;
        }

        private static string TruncateForLog(string payloadText) {
            const int maxLength = 200;
            if (string.IsNullOrEmpty(payloadText) || payloadText.Length <= maxLength) {
                return payloadText;
            }
            return payloadText.Substring(0, maxLength) + "...";
        }

        private static X509Certificate2 LoadTrustedCertificate(string path) {
            try {
                if (!File.Exists(path)) {
                    MqttLog.Error("MqttClientService: trusted certificate not found at \"" + path + "\".");
                    return null;
                }
                return new X509Certificate2(path);
            } catch (Exception exception) {
                MqttLog.Error("MqttClientService: failed to load trusted certificate from \"" + path + "\": " + exception.Message);
                return null;
            }
        }

        /// <summary> Certificate pinning: accepts the connection only if the broker presents exactly this certificate, regardless of normal chain/CA validation - the correct approach for a self-signed broker certificate, which will never pass default OS trust. </summary>
        private static bool ValidateAgainstTrustedCertificate(MqttClientCertificateValidationEventArgs context, X509Certificate2 trustedCertificate) {
            using (X509Certificate2 presentedCertificate = new X509Certificate2(context.Certificate)) {
                bool trusted = string.Equals(presentedCertificate.Thumbprint, trustedCertificate.Thumbprint, StringComparison.OrdinalIgnoreCase);
                if (!trusted) {
                    MqttLog.Error("MqttClientService: server certificate thumbprint does not match the trusted certificate. SslPolicyErrors: " + context.SslPolicyErrors);
                }
                return trusted;
            }
        }

        private void ScheduleReconnect() {
            reconnectCts?.Cancel();
            reconnectCts = new CancellationTokenSource();
            CancellationToken token = reconnectCts.Token;
            _ = ReconnectLoopAsync(token);
        }

        private async Task ReconnectLoopAsync(CancellationToken cancellationToken) {
            float delaySeconds = settings.ReconnectInitialDelaySeconds;
            State = MqttConnectionState.Reconnecting;

            while (!cancellationToken.IsCancellationRequested && State != MqttConnectionState.Connected) {
                MqttLog.Info("MqttClientService: reconnecting in " + delaySeconds + "s...");
                try {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                } catch (TaskCanceledException) {
                    return;
                }

                if (cancellationToken.IsCancellationRequested) {
                    return;
                }

                await ConnectAsync(cancellationToken);

                delaySeconds = Mathf.Min(delaySeconds * 2f, settings.ReconnectMaxDelaySeconds);
            }
        }

        private static MqttQualityOfServiceLevel ConvertQualityOfService(MqttQualityOfService qualityOfService) {
            switch (qualityOfService) {
                case MqttQualityOfService.AtLeastOnce:
                    return MqttQualityOfServiceLevel.AtLeastOnce;
                case MqttQualityOfService.ExactlyOnce:
                    return MqttQualityOfServiceLevel.ExactlyOnce;
                default:
                    return MqttQualityOfServiceLevel.AtMostOnce;
            }
        }

        private static MqttQualityOfService ConvertQualityOfService(MqttQualityOfServiceLevel level) {
            switch (level) {
                case MqttQualityOfServiceLevel.AtLeastOnce:
                    return MqttQualityOfService.AtLeastOnce;
                case MqttQualityOfServiceLevel.ExactlyOnce:
                    return MqttQualityOfService.ExactlyOnce;
                default:
                    return MqttQualityOfService.AtMostOnce;
            }
        }
#endif
    }
}
