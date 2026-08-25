using System;
using System.Collections.Generic;
using System.Text;

using TMPro;

using UnityEngine;

namespace ViitorCloud.Games.DigitalTwinFuturotec.Mqtt {
    /// <summary>
    /// Live diagnostic overlay: connection state, broker target, and the most
    /// recent message received per topic (via MqttManager.AnyMessageReceived,
    /// independent of whether any game code has actually Subscribe()'d to it).
    /// Not end-user UI - a visualization aid for verifying the MQTT pipeline is
    /// actually receiving traffic. Binds to MqttManager.Instance lazily since
    /// script execution order between GameObjects in the same scene isn't
    /// guaranteed.
    /// </summary>
    public class MqttDebugUI : MonoBehaviour {
        [Header("Bindings")]
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI topicsText;

        [Header("State Colors")]
        [SerializeField] private Color connectedColor = Color.green;
        [SerializeField] private Color connectingColor = Color.yellow;
        [SerializeField] private Color disconnectedColor = Color.red;

        [Header("Display")]
        [Tooltip("Longest payload preview shown per topic before truncating.")]
        [SerializeField] private int maxPayloadPreviewLength = 300;

        private readonly Dictionary<string, MqttMessage> lastMessageByTopic = new Dictionary<string, MqttMessage>();
        private readonly Dictionary<string, DateTime> lastReceivedAtByTopic = new Dictionary<string, DateTime>();
        private readonly List<string> topicDisplayOrder = new List<string>();
        private MqttManager boundManager;
        private string connectionNote = "Waiting for MqttManager...";

        private void Update() {
            if (boundManager == null) {
                if (MqttManager.Instance == null) {
                    return;
                }
                Bind(MqttManager.Instance);
            }

            RefreshStatusText();
            RefreshTopicsText();
        }

        private void OnDestroy() {
            if (boundManager != null) {
                Unbind(boundManager);
            }
        }

        private void Bind(MqttManager manager) {
            boundManager = manager;
            manager.Connected += HandleConnected;
            manager.Disconnected += HandleDisconnected;
            manager.ConnectionError += HandleConnectionError;
            manager.AnyMessageReceived += HandleMessageReceived;
        }

        private void Unbind(MqttManager manager) {
            manager.Connected -= HandleConnected;
            manager.Disconnected -= HandleDisconnected;
            manager.ConnectionError -= HandleConnectionError;
            manager.AnyMessageReceived -= HandleMessageReceived;
        }

        private void HandleConnected() {
            connectionNote = "Connected at " + DateTime.Now.ToString("HH:mm:ss");
        }

        private void HandleDisconnected(string reason) {
            connectionNote = "Disconnected (" + reason + ") at " + DateTime.Now.ToString("HH:mm:ss");
        }

        private void HandleConnectionError(string error) {
            connectionNote = "Error: " + error;
        }

        private void HandleMessageReceived(MqttMessage message) {
            if (!lastMessageByTopic.ContainsKey(message.Topic)) {
                topicDisplayOrder.Add(message.Topic);
            }
            lastMessageByTopic[message.Topic] = message;
            lastReceivedAtByTopic[message.Topic] = DateTime.Now;
        }

        private void RefreshStatusText() {
            if (statusText == null) {
                return;
            }

            MqttConnectionState state = boundManager.State;
            MqttBrokerSettings settings = boundManager.BrokerSettings;
            string broker = settings == null ? "(no settings)" : settings.BrokerHost + (settings.BrokerHost.StartsWith("ws", StringComparison.OrdinalIgnoreCase) ? string.Empty : ":" + settings.BrokerPort);

            statusText.color = GetColorForState(state);
            statusText.text = "MQTT: " + state + "\nBroker: " + broker + "\n" + connectionNote;
        }

        private void RefreshTopicsText() {
            if (topicsText == null) {
                return;
            }

            if (topicDisplayOrder.Count == 0) {
                topicsText.text = "No messages received yet.";
                return;
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < topicDisplayOrder.Count; i++) {
                string topic = topicDisplayOrder[i];
                MqttMessage message = lastMessageByTopic[topic];
                DateTime receivedAt = lastReceivedAtByTopic[topic];

                builder.Append("<b>").Append(topic).Append("</b>  (").Append(receivedAt.ToString("HH:mm:ss")).Append(")\n");
                builder.Append(TruncatePayload(message.PayloadAsString())).Append("\n\n");
            }

            topicsText.text = builder.ToString();
        }

        private Color GetColorForState(MqttConnectionState state) {
            switch (state) {
                case MqttConnectionState.Connected:
                    return connectedColor;
                case MqttConnectionState.Connecting:
                case MqttConnectionState.Reconnecting:
                    return connectingColor;
                default:
                    return disconnectedColor;
            }
        }

        private string TruncatePayload(string payloadText) {
            if (string.IsNullOrEmpty(payloadText) || payloadText.Length <= maxPayloadPreviewLength) {
                return payloadText;
            }
            return payloadText.Substring(0, maxPayloadPreviewLength) + "...";
        }
    }
}
