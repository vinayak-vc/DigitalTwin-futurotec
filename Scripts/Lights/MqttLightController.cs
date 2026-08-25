using UnityEngine;

using ViitorCloud.Games.DigitalTwinFuturotec.Mqtt;

namespace ViitorCloud.Games.DigitalTwinFuturotec.Lights {
    /// <summary>
    /// Custom project logic on top of the (now sealed) MQTT module - bridges the
    /// "on-light"/"off-light" topics to 4 scene lights. lightsRoot stays inactive
    /// until MqttManager reports a successful connection (no dependency on any
    /// MQTT UI); each light's own active state is then driven independently by
    /// incoming messages. Payload is a 1-based light index ("1".."4") -
    /// on-light -> that light SetActive(true), off-light -> SetActive(false).
    /// </summary>
    public class MqttLightController : MonoBehaviour {
        [Header("Root")]
        [Tooltip("Parent GameObject holding all 4 lights - stays inactive until MQTT connects.")]
        [SerializeField] private GameObject lightsRoot;

        [Header("Lights (index 0 = light #1, ... index 3 = light #4)")]
        [SerializeField] private GameObject[] lights = new GameObject[4];

        [Header("Topics")]
        [SerializeField] private string onLightTopic = "on-light";
        [SerializeField] private string offLightTopic = "off-light";

        private MqttManager boundManager;

        private void Awake() {
            if (lightsRoot != null) {
                lightsRoot.SetActive(false);
            }
        }

        private void Update() {
            if (boundManager != null) {
                return;
            }
            if (MqttManager.Instance == null) {
                return;
            }
            Bind(MqttManager.Instance);
        }

        private void OnDestroy() {
            if (boundManager == null) {
                return;
            }

            boundManager.Connected -= HandleConnected;
            boundManager.Unsubscribe(onLightTopic, HandleOnLightMessage);
            boundManager.Unsubscribe(offLightTopic, HandleOffLightMessage);
        }

        private void Bind(MqttManager manager) {
            boundManager = manager;
            manager.Connected += HandleConnected;
            manager.Subscribe(onLightTopic, HandleOnLightMessage);
            manager.Subscribe(offLightTopic, HandleOffLightMessage);

            if (manager.State == MqttConnectionState.Connected) {
                HandleConnected();
            }
        }

        private void HandleConnected() {
            if (lightsRoot != null) {
                lightsRoot.SetActive(true);
            }
            MqttLog.Info("MqttLightController: MQTT connected - lights are now live.");
        }

        private void HandleOnLightMessage(MqttMessage message) {
            SetLight(message, true);
        }

        private void HandleOffLightMessage(MqttMessage message) {
            SetLight(message, false);
        }

        private void SetLight(MqttMessage message, bool isOn) {
            string payload = message.PayloadAsString().Trim();
            if (!int.TryParse(payload, out int lightNumber)) {
                MqttLog.Warning("MqttLightController: could not parse a light index from payload \"" + payload + "\" on \"" + message.Topic + "\".");
                return;
            }

            int index = lightNumber - 1;
            if (index < 0 || index >= lights.Length || lights[index] == null) {
                MqttLog.Warning("MqttLightController: light index " + lightNumber + " is out of range (have " + lights.Length + " light(s)).");
                return;
            }

            lights[index].SetActive(isOn);
            MqttLog.Info("MqttLightController: light #" + lightNumber + " -> " + (isOn ? "ON" : "OFF") + ".");
        }
    }
}
