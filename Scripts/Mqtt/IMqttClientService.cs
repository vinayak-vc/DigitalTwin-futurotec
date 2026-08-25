using System;
using System.Threading;
using System.Threading.Tasks;

namespace ViitorCloud.Games.DigitalTwinFuturotec.Mqtt {
    /// <summary> Transport-agnostic MQTT client contract. MqttClientService is the MQTTnet-backed implementation. </summary>
    public interface IMqttClientService {
        MqttConnectionState State { get; }

        event Action Connected;
        event Action<string> Disconnected;
        event Action<MqttMessage> MessageReceived;
        event Action<string> ConnectionError;

        void Configure(MqttBrokerSettings settings, string username, string password);

        Task ConnectAsync(CancellationToken cancellationToken);

        Task DisconnectAsync(CancellationToken cancellationToken);

        Task PublishAsync(string topic, byte[] payload, MqttQualityOfService qualityOfService, bool retain, CancellationToken cancellationToken);

        Task SubscribeAsync(string topic, MqttQualityOfService qualityOfService, CancellationToken cancellationToken);

        Task UnsubscribeAsync(string topic, CancellationToken cancellationToken);
    }
}
