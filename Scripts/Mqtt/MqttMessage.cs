using System;
using System.Text;

namespace ViitorCloud.Games.DigitalTwinFuturotec.Mqtt {
    /// <summary> Immutable snapshot of a single MQTT message, either received from a subscription or about to be published. </summary>
    public sealed class MqttMessage {
        public string Topic { get; }
        public byte[] Payload { get; }
        public MqttQualityOfService QualityOfService { get; }
        public bool Retain { get; }

        public MqttMessage(string topic, byte[] payload, MqttQualityOfService qualityOfService, bool retain) {
            Topic = topic;
            Payload = payload ?? Array.Empty<byte>();
            QualityOfService = qualityOfService;
            Retain = retain;
        }

        public string PayloadAsString() {
            return Encoding.UTF8.GetString(Payload);
        }
    }
}
