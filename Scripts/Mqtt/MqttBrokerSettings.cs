using System;

using UnityEngine;

namespace ViitorCloud.Games.DigitalTwinFuturotec.Mqtt {
    /// <summary>
    /// Non-secret MQTT broker connection settings. Credentials are deliberately
    /// NOT stored on this asset since it is committed to source control - inject
    /// them at runtime via MqttManager.SetCredentials(...) from a secure source
    /// (login flow, secrets vault, build pipeline secret, etc).
    ///
    /// Every field here can also be overridden at runtime by a StreamingAssets
    /// JSON file (see MqttRuntimeConfigLoader / MqttManager) via
    /// ApplyRuntimeOverride, so a deployed build can be reconfigured without a
    /// rebuild.
    /// </summary>
    [CreateAssetMenu(fileName = "MqttBrokerSettings", menuName = "DigitalTwin/Mqtt/Broker Settings")]
    public class MqttBrokerSettings : ScriptableObject {
        [Header("Connection")]
        [Tooltip("Either a bare hostname/IP for a raw TCP connection (uses BrokerPort), or a full ws://... / wss://... URI to connect over WebSocket instead (BrokerPort is ignored - the URI carries its own port, e.g. wss://host/mqtt implies 443). MqttClientService picks the transport automatically based on which one this looks like.")]
        [SerializeField] private string brokerHost = string.Empty;
        [SerializeField] private int brokerPort = 8883;
        [SerializeField] private bool useTls = true;
        [Tooltip("Prefix used to build a unique MQTT client ID: '{clientIdPrefix}-{SystemInfo.deviceUniqueIdentifier}'.")]
        [SerializeField] private string clientIdPrefix = "digitaltwin-futurotec";

        [Header("TLS")]
        [Tooltip("Path (absolute, or relative to the project root) to a PEM/CRT certificate to pin-trust for TLS, in addition to the OS trust store - required for a self-signed broker certificate, which the OS trust store will never accept. Empty means use default OS certificate validation only.")]
        [SerializeField] private string trustedCertificatePath = string.Empty;
        [Tooltip("Skips ALL TLS certificate validation. Local testing only - never enable for anything internet-facing or production.")]
        [SerializeField] private bool allowUntrustedCertificates = false;

        [Header("Session")]
        [SerializeField] private ushort keepAliveSeconds = 30;
        [SerializeField] private bool cleanSession = true;
        [SerializeField] private uint connectTimeoutSeconds = 10;

        [Header("Reconnect")]
        [SerializeField] private bool autoReconnect = true;
        [SerializeField] private float reconnectInitialDelaySeconds = 2f;
        [SerializeField] private float reconnectMaxDelaySeconds = 60f;

        [Header("Topics")]
        [Tooltip("Topic filters MqttManager knows about. Entries with SubscribeOnConnect are subscribed automatically once connected.")]
        [SerializeField] private MqttTopicSubscription[] topics = new MqttTopicSubscription[0];

        public string BrokerHost {
            get { return brokerHost; }
        }

        public int BrokerPort {
            get { return brokerPort; }
        }

        public bool UseTls {
            get { return useTls; }
        }

        public string ClientIdPrefix {
            get { return clientIdPrefix; }
        }

        public ushort KeepAliveSeconds {
            get { return keepAliveSeconds; }
        }

        public bool CleanSession {
            get { return cleanSession; }
        }

        public uint ConnectTimeoutSeconds {
            get { return connectTimeoutSeconds; }
        }

        public bool AutoReconnect {
            get { return autoReconnect; }
        }

        public float ReconnectInitialDelaySeconds {
            get { return reconnectInitialDelaySeconds; }
        }

        public float ReconnectMaxDelaySeconds {
            get { return reconnectMaxDelaySeconds; }
        }

        public MqttTopicSubscription[] Topics {
            get { return topics; }
        }

        public string TrustedCertificatePath {
            get { return trustedCertificatePath; }
        }

        public bool AllowUntrustedCertificates {
            get { return allowUntrustedCertificates; }
        }

        /// <summary> Applies a StreamingAssets-sourced override on top of the Inspector-configured values. Only fields present (non-null) in config are changed. Called by MqttManager before connecting - see MqttRuntimeConfigLoader. </summary>
        public void ApplyRuntimeOverride(MqttRuntimeConfig config) {
            if (config == null) {
                return;
            }

            if (!string.IsNullOrEmpty(config.BrokerHost)) {
                brokerHost = config.BrokerHost;
            }
            if (config.BrokerPort.HasValue) {
                brokerPort = config.BrokerPort.Value;
            }
            if (config.UseTls.HasValue) {
                useTls = config.UseTls.Value;
            }
            if (!string.IsNullOrEmpty(config.ClientIdPrefix)) {
                clientIdPrefix = config.ClientIdPrefix;
            }
            if (config.KeepAliveSeconds.HasValue) {
                keepAliveSeconds = (ushort)config.KeepAliveSeconds.Value;
            }
            if (config.CleanSession.HasValue) {
                cleanSession = config.CleanSession.Value;
            }
            if (config.ConnectTimeoutSeconds.HasValue) {
                connectTimeoutSeconds = (uint)config.ConnectTimeoutSeconds.Value;
            }
            if (config.AutoReconnect.HasValue) {
                autoReconnect = config.AutoReconnect.Value;
            }
            if (config.ReconnectInitialDelaySeconds.HasValue) {
                reconnectInitialDelaySeconds = config.ReconnectInitialDelaySeconds.Value;
            }
            if (config.ReconnectMaxDelaySeconds.HasValue) {
                reconnectMaxDelaySeconds = config.ReconnectMaxDelaySeconds.Value;
            }
            if (config.Topics != null) {
                topics = BuildTopicsFromConfig(config.Topics);
            }
            if (!string.IsNullOrEmpty(config.TrustedCertificatePath)) {
                trustedCertificatePath = config.TrustedCertificatePath;
            }
            if (config.AllowUntrustedCertificates.HasValue) {
                allowUntrustedCertificates = config.AllowUntrustedCertificates.Value;
            }
        }

        private static MqttTopicSubscription[] BuildTopicsFromConfig(MqttTopicConfig[] configTopics) {
            MqttTopicSubscription[] result = new MqttTopicSubscription[configTopics.Length];
            for (int i = 0; i < configTopics.Length; i++) {
                MqttTopicConfig topicConfig = configTopics[i];
                MqttQualityOfService qualityOfService = MqttQualityOfService.AtLeastOnce;
                if (!string.IsNullOrEmpty(topicConfig.QualityOfService)) {
                    Enum.TryParse(topicConfig.QualityOfService, true, out qualityOfService);
                }
                result[i] = new MqttTopicSubscription(topicConfig.Topic, qualityOfService, topicConfig.SubscribeOnConnect);
            }
            return result;
        }

        /// <summary>
        /// Editor convenience button: applies MqttConfig.json onto THIS asset
        /// directly, for previewing what a runtime override would look like.
        /// MqttManager itself never calls this - at runtime it always applies
        /// the override onto a throwaway clone (effectiveSettings), never onto
        /// this committed asset. Using this button IS committing the override
        /// to the asset - only use it if you actually want to promote the JSON
        /// values into the checked-in defaults, then review the diff.
        /// </summary>
        [ContextMenu("Load From JSON (overwrites this asset - see tooltip)")]
        private void LoadFromJson() {
            MqttRuntimeConfig config = MqttRuntimeConfigLoader.LoadFromStreamingAssets();
            if (config != null) {
                ApplyRuntimeOverride(config);
                MqttLog.Info("MqttBrokerSettings: loaded MqttConfig.json and applied it onto this asset. Review the diff before committing.", this);
            } else {
                MqttLog.Warning("MqttBrokerSettings: no MQTT runtime config found in StreamingAssets.", this);
            }
        }
    }
}
