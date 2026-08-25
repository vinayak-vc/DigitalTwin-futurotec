using TMPro;

using UnityEngine;
using UnityEngine.UI;

namespace ViitorCloud.Games.DigitalTwinFuturotec.Mqtt {
    /// <summary>
    /// Connect/Disconnect controls plus a manual connection-settings form.
    /// The form is shown whenever MqttManager isn't Connected (covers both
    /// "auto-connect hasn't succeeded yet" and "just disconnected") and hidden
    /// once it is, pre-filled with whatever settings are currently in effect
    /// so a failed auto-connect attempt shows exactly what it tried. Editing a
    /// field and pressing Connect applies the values onto MqttManager's
    /// runtime clone (never the committed MqttBrokerSettings asset) and
    /// retries; a successful connect made this way gets saved back to the
    /// StreamingAssets config file (see MqttManager.saveConfigOnSuccessfulConnect)
    /// so it persists across restarts.
    /// </summary>
    public class MqttConnectionPanelUI : MonoBehaviour {
        [Header("Buttons")]
        [SerializeField] private Button connectButton;
        [SerializeField] private Button disconnectButton;

        [Header("Manual Settings Form")]
        [Tooltip("Shown only while not connected.")]
        [SerializeField] private GameObject formRoot;
        [SerializeField] private TMP_InputField brokerHostInput;
        [SerializeField] private TMP_InputField brokerPortInput;
        [SerializeField] private Toggle useTlsToggle;
        [SerializeField] private TMP_InputField usernameInput;
        [SerializeField] private TMP_InputField passwordInput;
        [SerializeField] private TMP_InputField clientIdPrefixInput;
        [SerializeField] private TMP_InputField trustedCertificatePathInput;

        [Header("Feedback")]
        [SerializeField] private TextMeshProUGUI feedbackText;

        private MqttManager boundManager;

        private void Update() {
            if (boundManager == null) {
                if (MqttManager.Instance == null) {
                    return;
                }
                Bind(MqttManager.Instance);
            }

            bool connected = boundManager.State == MqttConnectionState.Connected;
            if (connectButton != null) {
                connectButton.interactable = !connected;
            }
            if (disconnectButton != null) {
                disconnectButton.interactable = connected;
            }
            if (formRoot != null) {
                formRoot.SetActive(!connected);
            }
        }

        private void OnDestroy() {
            if (boundManager != null) {
                boundManager.Connected -= HandleConnected;
                boundManager.Disconnected -= HandleDisconnected;
                boundManager.ConnectionError -= HandleConnectionError;
            }
            if (connectButton != null) {
                connectButton.onClick.RemoveListener(HandleConnectClicked);
            }
            if (disconnectButton != null) {
                disconnectButton.onClick.RemoveListener(HandleDisconnectClicked);
            }
        }

        private void Bind(MqttManager manager) {
            boundManager = manager;
            manager.Connected += HandleConnected;
            manager.Disconnected += HandleDisconnected;
            manager.ConnectionError += HandleConnectionError;

            if (connectButton != null) {
                connectButton.onClick.AddListener(HandleConnectClicked);
            }
            if (disconnectButton != null) {
                disconnectButton.onClick.AddListener(HandleDisconnectClicked);
            }

            // Binding can happen before the StreamingAssets config load finishes (BrokerSettings
            // still holds Inspector defaults at that point) - this initial call is best-effort.
            // The real fix for "form shows blank" is refreshing again on Disconnected/
            // ConnectionError below, which fire only once the actual settings are known/attempted.
            PopulateFieldsFromCurrentSettings();
        }

        /// <summary>
        /// Refreshes every field from the settings currently in effect. Called
        /// once on Bind (best-effort, may predate the config load) and again on
        /// every Disconnected/ConnectionError - i.e. every time the form is
        /// about to become relevant again - so it never shows stale or blank
        /// values from whatever was in effect at startup.
        /// </summary>
        private void PopulateFieldsFromCurrentSettings() {
            MqttBrokerSettings settings = boundManager.BrokerSettings;
            if (settings == null) {
                return;
            }

            if (brokerHostInput != null) {
                brokerHostInput.text = settings.BrokerHost;
            }
            if (brokerPortInput != null) {
                brokerPortInput.text = settings.BrokerPort.ToString();
            }
            if (useTlsToggle != null) {
                useTlsToggle.isOn = settings.UseTls;
            }
            if (usernameInput != null) {
                usernameInput.text = boundManager.LastUsername;
            }
            if (clientIdPrefixInput != null) {
                clientIdPrefixInput.text = settings.ClientIdPrefix;
            }
            if (trustedCertificatePathInput != null) {
                trustedCertificatePathInput.text = settings.TrustedCertificatePath;
            }
            // Password is intentionally left blank even though it's known in memory -
            // avoid redisplaying a secret back into a UI field once it's been entered.
        }

        private void HandleConnectClicked() {
            MqttRuntimeConfig overrideValues = BuildOverrideFromFields();
            boundManager.ApplyManualOverride(overrideValues);
            boundManager.SetCredentials(
                usernameInput != null ? usernameInput.text : string.Empty,
                passwordInput != null ? passwordInput.text : string.Empty
            );

            SetFeedback("Connecting to " + overrideValues.BrokerHost + "...");
            boundManager.Connect();
        }

        private void HandleDisconnectClicked() {
            boundManager.Disconnect();
        }

        private MqttRuntimeConfig BuildOverrideFromFields() {
            MqttBrokerSettings currentSettings = boundManager.BrokerSettings;

            int port = currentSettings != null ? currentSettings.BrokerPort : 8883;
            if (brokerPortInput != null) {
                int.TryParse(brokerPortInput.text, out port);
            }

            return new MqttRuntimeConfig {
                BrokerHost = brokerHostInput != null ? brokerHostInput.text : string.Empty,
                BrokerPort = port,
                UseTls = useTlsToggle != null ? useTlsToggle.isOn : (bool?)null,
                ClientIdPrefix = clientIdPrefixInput != null ? clientIdPrefixInput.text : null,
                TrustedCertificatePath = trustedCertificatePathInput != null ? trustedCertificatePathInput.text : null
            };
        }

        private void HandleConnected() {
            SetFeedback("Connected.");
        }

        private void HandleDisconnected(string reason) {
            PopulateFieldsFromCurrentSettings();
            SetFeedback("Disconnected (" + reason + "). Check the settings below and press Connect.");
        }

        private void HandleConnectionError(string error) {
            PopulateFieldsFromCurrentSettings();
            SetFeedback("Connect failed: " + error + ". Check the settings below and try again.");
        }

        private void SetFeedback(string message) {
            if (feedbackText != null) {
                feedbackText.text = message;
            }
        }
    }
}
