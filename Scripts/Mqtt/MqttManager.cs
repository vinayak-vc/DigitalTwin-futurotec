using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

namespace ViitorCloud.Games.DigitalTwinFuturotec.Mqtt {
    /// <summary>
    /// Boot-once MonoBehaviour that owns the app's single MqttClientService:
    /// wires connection lifecycle, marshals every MQTTnet callback (which fire on
    /// background I/O threads) onto the Unity main thread, and routes incoming
    /// messages to per-topic-filter subscribers with '+'/'#' wildcard matching.
    /// Drop one instance on a scene that loads at boot; assign brokerSettings.
    /// </summary>
    public class MqttManager : MonoBehaviour {
        [Header("Configuration")]
        [SerializeField] private MqttBrokerSettings brokerSettings;

        [Header("Main Thread Dispatch")]
        [Tooltip("Upper bound on how many queued MQTT callbacks are processed per frame, so a burst of incoming messages cannot spike frame time.")]
        [SerializeField] private int maxDispatchedPerFrame = 200;

        [Header("Lifecycle")]
        [Tooltip("Disconnect gracefully when the app is paused/backgrounded (mobile), reconnect on resume if it was connected before.")]
        [SerializeField] private bool disconnectOnPause = true;

        [Header("Runtime Config (StreamingAssets)")]
        [Tooltip("If true, brokerSettings is overridden by a JSON file under StreamingAssets before the first Connect() actually runs - lets a built player be reconfigured without a rebuild. Only takes effect where StreamingAssets ships as loose files (Standalone); on Android/iOS the file is packed into the app binary at build time.")]
        [SerializeField] private bool loadConfigFromStreamingAssets = true;
        [SerializeField] private string streamingAssetsConfigRelativePath = "DigitalTwinFuturotec/MqttConfig.json";
        [Tooltip("Connect automatically once this GameObject wakes (after any StreamingAssets config load finishes). If it fails, ConnectionError/Disconnected still fire normally - a connection UI can use those to prompt for corrected settings.")]
        [SerializeField] private bool autoConnectOnStart = true;
        [Tooltip("On every successful connect, write the settings actually used (including whatever credentials were set via SetCredentials/ApplyManualOverride) back to the StreamingAssets config file, so a manually-corrected connection persists across restarts. Only takes effect where the file is writable at runtime (Standalone/Editor).")]
        [SerializeField] private bool saveConfigOnSuccessfulConnect = true;

        public static MqttManager Instance { get; private set; }

        public MqttConnectionState State {
            get { return client == null ? MqttConnectionState.Disconnected : client.State; }
        }

        /// <summary> Read-only access to the connection target/topic config for diagnostics UI - never exposes credentials, those never live on this asset. This is the runtime clone (see effectiveSettings), not the source-controlled asset. </summary>
        public MqttBrokerSettings BrokerSettings {
            get { return effectiveSettings; }
        }

        /// <summary> The username last set via SetCredentials/ApplyManualOverride, held in memory only - useful for a connection UI to pre-fill without re-displaying the password too. </summary>
        public string LastUsername {
            get { return lastUsername; }
        }

        public event Action Connected;
        public event Action<string> Disconnected;
        public event Action<string> ConnectionError;

        /// <summary> Fires for every message this client receives, regardless of whether any listener is subscribed to it via Subscribe(...). Diagnostics/visualization only - use Subscribe(...) for real message handling. </summary>
        public event Action<MqttMessage> AnyMessageReceived;

        private enum ConfigLoadState {
            NotStarted,
            Loading,
            Ready
        }

        private IMqttClientService client;
        private readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();
        private readonly Dictionary<string, List<Action<MqttMessage>>> subscriptions = new Dictionary<string, List<Action<MqttMessage>>>();
        private readonly Dictionary<string, MqttQualityOfService> subscriptionQualityOfService = new Dictionary<string, MqttQualityOfService>();
        private bool wasConnectedBeforePause;
        private ConfigLoadState configLoadState = ConfigLoadState.NotStarted;
        private bool connectRequestedWhilePending;

        /// <summary>
        /// Runtime-only clone of brokerSettings (see Awake). Unlike GameObjects/
        /// components in a scene, Unity does NOT revert field changes made to a
        /// ScriptableObject asset during Play Mode when Play stops - mutating
        /// brokerSettings directly (as ApplyRuntimeOverride used to) risked
        /// permanently baking a runtime JSON override into the committed asset the
        /// moment anything (including this class's own latest-edit-wins check)
        /// called AssetDatabase.SaveAssetIfDirty. All runtime code below operates
        /// on this clone; brokerSettings itself is never written to after Awake.
        /// </summary>
        private MqttBrokerSettings effectiveSettings;
        private string lastUsername = string.Empty;
        private string lastPassword = string.Empty;

        private void Awake() {
            if (Instance != null && Instance != this) {
                MqttLog.Warning("MqttManager: duplicate instance destroyed.");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (brokerSettings == null) {
                MqttLog.Error("MqttManager: brokerSettings is not assigned. MQTT will not connect.");
            } else {
                effectiveSettings = Instantiate(brokerSettings);
            }

            client = new MqttClientService();
            client.Configure(effectiveSettings, string.Empty, string.Empty);
            client.Connected += HandleClientConnected;
            client.Disconnected += HandleClientDisconnected;
            client.ConnectionError += HandleClientConnectionError;
            client.MessageReceived += HandleClientMessageReceived;

            if (effectiveSettings != null && loadConfigFromStreamingAssets) {
                configLoadState = ConfigLoadState.Loading;
                MqttLog.Info("MqttManager: loading " + streamingAssetsConfigRelativePath + " before first connect...");
                StartCoroutine(LoadRuntimeConfigCoroutine());
            } else {
                configLoadState = ConfigLoadState.Ready;
                if (autoConnectOnStart) {
                    Connect();
                }
            }
        }

        private void Update() {
            int dispatched = 0;
            while (dispatched < maxDispatchedPerFrame && mainThreadQueue.TryDequeue(out Action action)) {
                action.Invoke();
                dispatched++;
            }
        }

        private void OnApplicationPause(bool pauseStatus) {
            if (!disconnectOnPause) {
                return;
            }

            if (pauseStatus) {
                wasConnectedBeforePause = State == MqttConnectionState.Connected;
                if (wasConnectedBeforePause) {
                    Disconnect();
                }
            } else if (wasConnectedBeforePause) {
                Connect();
            }
        }

        private void OnApplicationQuit() {
            if (client != null && State == MqttConnectionState.Connected) {
                Task disconnectTask = client.DisconnectAsync(CancellationToken.None);
                disconnectTask.Wait(TimeSpan.FromSeconds(3));
            }
        }

        private void OnDestroy() {
            if (client == null) {
                return;
            }

            client.Connected -= HandleClientConnected;
            client.Disconnected -= HandleClientDisconnected;
            client.ConnectionError -= HandleClientConnectionError;
            client.MessageReceived -= HandleClientMessageReceived;

            if (client is IDisposable disposableClient) {
                disposableClient.Dispose();
            }

            if (effectiveSettings != null) {
                Destroy(effectiveSettings);
            }
        }

        /// <summary> Must be called before Connect() if the broker requires authentication. Credentials are held in memory only, never serialized to an asset - only ever written to disk via SaveEffectiveConfigToStreamingAssets, and only into the StreamingAssets config file, never MqttBrokerSettings. </summary>
        public void SetCredentials(string username, string password) {
            lastUsername = username ?? string.Empty;
            lastPassword = password ?? string.Empty;
            client.Configure(effectiveSettings, username, password);
        }

        /// <summary>
        /// Applies a manual connection-settings correction (e.g. from a
        /// connection UI) onto the runtime clone - never onto the committed
        /// MqttBrokerSettings asset. Call Connect() afterward to try it.
        /// </summary>
        public void ApplyManualOverride(MqttRuntimeConfig overrideValues) {
            if (effectiveSettings == null || overrideValues == null) {
                return;
            }

            effectiveSettings.ApplyRuntimeOverride(overrideValues);
            client.Configure(effectiveSettings, lastUsername, lastPassword);
        }

        /// <summary> Snapshots the current effective settings + last-used credentials and writes them to the StreamingAssets config file, so a manually-corrected connection persists across restarts. No-ops (with a clear log) where that file isn't writable at runtime - see MqttRuntimeConfigLoader.SaveToStreamingAssets. </summary>
        public void SaveEffectiveConfigToStreamingAssets() {
            if (effectiveSettings == null) {
                return;
            }

            MqttTopicConfig[] topicConfigs = new MqttTopicConfig[effectiveSettings.Topics.Length];
            for (int i = 0; i < effectiveSettings.Topics.Length; i++) {
                MqttTopicSubscription topic = effectiveSettings.Topics[i];
                topicConfigs[i] = new MqttTopicConfig {
                    Topic = topic.Topic,
                    QualityOfService = topic.QualityOfService.ToString(),
                    SubscribeOnConnect = topic.SubscribeOnConnect
                };
            }

            MqttRuntimeConfig snapshot = new MqttRuntimeConfig {
                BrokerHost = effectiveSettings.BrokerHost,
                BrokerPort = effectiveSettings.BrokerPort,
                UseTls = effectiveSettings.UseTls,
                ClientIdPrefix = effectiveSettings.ClientIdPrefix,
                KeepAliveSeconds = effectiveSettings.KeepAliveSeconds,
                CleanSession = effectiveSettings.CleanSession,
                ConnectTimeoutSeconds = (int)effectiveSettings.ConnectTimeoutSeconds,
                AutoReconnect = effectiveSettings.AutoReconnect,
                ReconnectInitialDelaySeconds = effectiveSettings.ReconnectInitialDelaySeconds,
                ReconnectMaxDelaySeconds = effectiveSettings.ReconnectMaxDelaySeconds,
                Username = lastUsername,
                Password = lastPassword,
                TrustedCertificatePath = effectiveSettings.TrustedCertificatePath,
                AllowUntrustedCertificates = effectiveSettings.AllowUntrustedCertificates,
                Topics = topicConfigs
            };

            bool saved = MqttRuntimeConfigLoader.SaveToStreamingAssets(streamingAssetsConfigRelativePath, snapshot);
            if (saved) {
                MqttLog.Info("MqttManager: saved the working connection settings to " + streamingAssetsConfigRelativePath + ".");
            }
        }

        /// <summary> If the StreamingAssets config file is still loading, the actual connect attempt is deferred until it finishes so it never runs against stale settings. </summary>
        public void Connect() {
            if (effectiveSettings == null) {
                MqttLog.Error("MqttManager: cannot connect, brokerSettings is not assigned.");
                return;
            }

            if (configLoadState == ConfigLoadState.Loading) {
                MqttLog.Info("MqttManager: Connect() called while the runtime config is still loading - deferring until it finishes.");
                connectRequestedWhilePending = true;
                return;
            }

            _ = client.ConnectAsync(CancellationToken.None);
        }

        public void Disconnect() {
            _ = client.DisconnectAsync(CancellationToken.None);
        }

        public void Publish(string topic, byte[] payload, MqttQualityOfService qualityOfService = MqttQualityOfService.AtLeastOnce, bool retain = false) {
            if (string.IsNullOrWhiteSpace(topic)) {
                MqttLog.Error("MqttManager: Publish called with an empty topic.");
                return;
            }

            _ = client.PublishAsync(topic, payload, qualityOfService, retain, CancellationToken.None);
        }

        public void Publish(string topic, string payload, MqttQualityOfService qualityOfService = MqttQualityOfService.AtLeastOnce, bool retain = false) {
            Publish(topic, Encoding.UTF8.GetBytes(payload ?? string.Empty), qualityOfService, retain);
        }

        /// <summary> Subscribes onMessage to every message whose topic matches topicFilter ('+'/'#' wildcards supported). The broker-level SUBSCRIBE is only sent once per unique topicFilter. </summary>
        public void Subscribe(string topicFilter, Action<MqttMessage> onMessage, MqttQualityOfService qualityOfService = MqttQualityOfService.AtLeastOnce) {
            if (string.IsNullOrWhiteSpace(topicFilter) || onMessage == null) {
                MqttLog.Error("MqttManager: Subscribe called with an empty topicFilter or null callback.");
                return;
            }

            EnsureBrokerSubscription(topicFilter, qualityOfService);
            subscriptions[topicFilter].Add(onMessage);
            MqttLog.Info("MqttManager: listener attached to \"" + topicFilter + "\" (" + subscriptions[topicFilter].Count + " listener(s) on this filter).");
        }

        /// <summary>
        /// Registers topicFilter locally (if not already registered) without
        /// attaching a listener, and sends the broker-level SUBSCRIBE immediately
        /// only if already connected. If not connected yet - the common case for
        /// a listener that subscribes from Awake/Start, before the StreamingAssets
        /// config load and first Connect() finish - the actual SUBSCRIBE is
        /// deferred to ResubscribeAllTopics(), which runs on every successful
        /// connect (including reconnects, since CleanSession wipes the broker's
        /// memory of prior subscriptions). Without this, a topic filter
        /// registered before the first successful connect would silently never
        /// receive a broker-level SUBSCRIBE at all - caught live via
        /// MqttLightController subscribing from Update() before Connect()
        /// resolved.
        /// </summary>
        private void EnsureBrokerSubscription(string topicFilter, MqttQualityOfService qualityOfService) {
            bool isNewFilter = !subscriptions.ContainsKey(topicFilter);
            if (isNewFilter) {
                subscriptions[topicFilter] = new List<Action<MqttMessage>>();
                subscriptionQualityOfService[topicFilter] = qualityOfService;

                if (State == MqttConnectionState.Connected) {
                    _ = client.SubscribeAsync(topicFilter, qualityOfService, CancellationToken.None);
                }
            }
        }

        /// <summary> Re-sends the broker-level SUBSCRIBE for every currently-registered topic filter. Called on every successful connect - covers both "subscribed before the first connect finished" and "reconnected after a drop" (a fresh session, per CleanSession, remembers nothing). </summary>
        private void ResubscribeAllTopics() {
            foreach (KeyValuePair<string, MqttQualityOfService> entry in subscriptionQualityOfService) {
                _ = client.SubscribeAsync(entry.Key, entry.Value, CancellationToken.None);
            }
        }

        private void AutoSubscribeConfiguredTopics() {
            if (effectiveSettings == null) {
                return;
            }

            MqttTopicSubscription[] configuredTopics = effectiveSettings.Topics;
            MqttLog.Info("MqttManager: auto-subscribing " + configuredTopics.Length + " configured topic(s) from MqttBrokerSettings...");
            for (int i = 0; i < configuredTopics.Length; i++) {
                MqttTopicSubscription topicSubscription = configuredTopics[i];
                if (!topicSubscription.SubscribeOnConnect || string.IsNullOrWhiteSpace(topicSubscription.Topic)) {
                    continue;
                }

                EnsureBrokerSubscription(topicSubscription.Topic, topicSubscription.QualityOfService);
            }
        }

        public void Unsubscribe(string topicFilter, Action<MqttMessage> onMessage) {
            if (!subscriptions.TryGetValue(topicFilter, out List<Action<MqttMessage>> callbacks)) {
                return;
            }

            callbacks.Remove(onMessage);
            if (callbacks.Count == 0) {
                subscriptions.Remove(topicFilter);
                subscriptionQualityOfService.Remove(topicFilter);
                _ = client.UnsubscribeAsync(topicFilter, CancellationToken.None);
            }
        }

        private void HandleClientConnected() {
            mainThreadQueue.Enqueue(() => {
                AutoSubscribeConfiguredTopics();
                ResubscribeAllTopics();
                if (saveConfigOnSuccessfulConnect) {
                    SaveEffectiveConfigToStreamingAssets();
                }
                Connected?.Invoke();
            });
        }

        private void HandleClientDisconnected(string reason) {
            mainThreadQueue.Enqueue(() => Disconnected?.Invoke(reason));
        }

        private void HandleClientConnectionError(string error) {
            mainThreadQueue.Enqueue(() => ConnectionError?.Invoke(error));
        }

        private void HandleClientMessageReceived(MqttMessage message) {
            mainThreadQueue.Enqueue(() => DispatchMessage(message));
        }

        private void DispatchMessage(MqttMessage message) {
            int matchedFilters = 0;
            int invokedCallbacks = 0;

            foreach (KeyValuePair<string, List<Action<MqttMessage>>> subscription in subscriptions) {
                if (!MqttTopicMatcher.IsMatch(subscription.Key, message.Topic)) {
                    continue;
                }

                matchedFilters++;
                List<Action<MqttMessage>> callbacks = subscription.Value;
                for (int i = 0; i < callbacks.Count; i++) {
                    callbacks[i].Invoke(message);
                    invokedCallbacks++;
                }
            }

            MqttLog.Info("MqttManager: \"" + message.Topic + "\" matched " + matchedFilters + " filter(s), invoked " + invokedCallbacks + " listener(s).");
            AnyMessageReceived?.Invoke(message);
        }

        private IEnumerator LoadRuntimeConfigCoroutine() {
            MqttRuntimeConfig loadedConfig = null;
            yield return MqttRuntimeConfigLoader.Load(
                streamingAssetsConfigRelativePath,
                config => loadedConfig = config,
                errorMessage => MqttLog.Warning("MqttManager: " + errorMessage + " - continuing with Inspector-configured settings.")
            );

            if (loadedConfig != null) {
                bool applyOverride = true;

#if UNITY_EDITOR
                if (IsBrokerSettingsAssetNewerThanConfigFile()) {
                    applyOverride = false;
                    MqttLog.Info("MqttManager: MqttBrokerSettings.asset was saved more recently than " + streamingAssetsConfigRelativePath + " - keeping the Inspector values for this session instead of applying the JSON override. Save " + streamingAssetsConfigRelativePath + " again to have it take priority.");
                }
#endif

                if (applyOverride) {
                    effectiveSettings.ApplyRuntimeOverride(loadedConfig);
                    MqttLog.Info("MqttManager: applied " + streamingAssetsConfigRelativePath + " over MqttBrokerSettings.");
                    if (!string.IsNullOrEmpty(loadedConfig.Username) || !string.IsNullOrEmpty(loadedConfig.Password)) {
                        SetCredentials(loadedConfig.Username, loadedConfig.Password);
                    }
                }
            }

            configLoadState = ConfigLoadState.Ready;

            if (connectRequestedWhilePending) {
                connectRequestedWhilePending = false;
                Connect();
            } else if (autoConnectOnStart) {
                Connect();
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only "whichever was edited more recently wins" check: compares
        /// brokerSettings.asset's on-disk save time against the config file's.
        /// Only meaningful in the Editor - a built player can never edit
        /// MqttBrokerSettings after the fact, so the StreamingAssets file is
        /// unconditionally authoritative there, which is the entire point of it
        /// being externally editable post-build.
        ///
        /// Deliberately does NOT call AssetDatabase.SaveAssetIfDirty(brokerSettings)
        /// - an earlier version of this method did, to catch an unsaved Inspector
        /// edit, and that call was the mechanism behind a real, repeated
        /// data-corruption bug: if brokerSettings was ever dirty for any reason
        /// (including, apparently, surviving a script recompile that happened
        /// while Play Mode was still active from a previous session), that call
        /// would flush whatever was currently in memory straight to the committed
        /// asset file. Comparing on-disk state as-is trades away detecting an
        /// unsaved edit for never being able to write to this asset from runtime
        /// code, which is the correct trade. See docs/decisions.md.
        /// </summary>
        private bool IsBrokerSettingsAssetNewerThanConfigFile() {
            string assetPath = UnityEditor.AssetDatabase.GetAssetPath(brokerSettings);
            string configFilePath = MqttRuntimeConfigLoader.GetAbsoluteFilePath(streamingAssetsConfigRelativePath);

            if (string.IsNullOrEmpty(assetPath) || !System.IO.File.Exists(assetPath) || !System.IO.File.Exists(configFilePath)) {
                return false;
            }

            DateTime assetWriteTimeUtc = System.IO.File.GetLastWriteTimeUtc(assetPath);
            DateTime configWriteTimeUtc = System.IO.File.GetLastWriteTimeUtc(configFilePath);
            return assetWriteTimeUtc > configWriteTimeUtc;
        }
#endif
    }
}
