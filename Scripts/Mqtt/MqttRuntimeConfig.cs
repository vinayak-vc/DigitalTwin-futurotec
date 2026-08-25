namespace ViitorCloud.Games.DigitalTwinFuturotec.Mqtt {
    /// <summary>
    /// Deserialization target for the StreamingAssets runtime config file (see
    /// MqttRuntimeConfigLoader). Every field is nullable/optional so a config file
    /// can override just the fields it cares about - anything omitted leaves
    /// MqttBrokerSettings' existing (Inspector-configured) value untouched. This
    /// class never touches Unity's own serialization system - it exists purely as
    /// a Newtonsoft.Json deserialization target, kept separate from
    /// MqttBrokerSettings so the on-disk JSON shape and the Inspector-facing
    /// ScriptableObject shape can evolve independently.
    /// </summary>
    public class MqttRuntimeConfig {
        public string BrokerHost { get; set; }
        public int? BrokerPort { get; set; }
        public bool? UseTls { get; set; }
        public string ClientIdPrefix { get; set; }
        public int? KeepAliveSeconds { get; set; }
        public bool? CleanSession { get; set; }
        public int? ConnectTimeoutSeconds { get; set; }
        public bool? AutoReconnect { get; set; }
        public float? ReconnectInitialDelaySeconds { get; set; }
        public float? ReconnectMaxDelaySeconds { get; set; }

        /// <summary> Not stored on MqttBrokerSettings (that asset is committed to source control) - MqttManager forwards these straight into SetCredentials(...) instead. </summary>
        public string Username { get; set; }
        public string Password { get; set; }

        /// <summary> Path (absolute, or relative to the project root) to a PEM/CRT certificate to pin-trust for TLS - see MqttBrokerSettings.TrustedCertificatePath. </summary>
        public string TrustedCertificatePath { get; set; }

        /// <summary> Skips ALL TLS certificate validation. Local testing only. </summary>
        public bool? AllowUntrustedCertificates { get; set; }

        /// <summary> Null means "config file didn't specify topics, keep the Inspector list". An empty array means "explicitly clear the topic list". </summary>
        public MqttTopicConfig[] Topics { get; set; }
    }

    /// <summary> One topic entry inside MqttRuntimeConfig.Topics. QualityOfService is a string ("AtMostOnce"/"AtLeastOnce"/"ExactlyOnce") so the file stays readable when hand-edited after a build. </summary>
    public class MqttTopicConfig {
        public string Topic { get; set; }
        public string QualityOfService { get; set; } = "AtLeastOnce";
        public bool SubscribeOnConnect { get; set; } = true;
    }
}
