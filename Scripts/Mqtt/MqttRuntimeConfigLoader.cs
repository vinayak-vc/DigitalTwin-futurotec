using System;
using System.Collections;
using System.IO;

using Newtonsoft.Json;

using UnityEngine;
using UnityEngine.Networking;

namespace ViitorCloud.Games.DigitalTwinFuturotec.Mqtt {
    /// <summary>
    /// Reads an MqttRuntimeConfig from a JSON file under StreamingAssets. Uses
    /// UnityWebRequest rather than System.IO.File because StreamingAssets is only
    /// a plain filesystem path on Standalone/Editor - on Android it is packed
    /// inside the APK and can only be read through UnityWebRequest's "jar:file://"
    /// support, and iOS needs the "file://" prefix Editor/Standalone also use.
    ///
    /// Editing the file after a build only actually works where StreamingAssets
    /// ends up as loose files next to the built player - Standalone (Windows/
    /// Mac/Linux). On Android/iOS the file is packed into the app binary at build
    /// time; changing it afterward means repackaging, not hand-editing a text
    /// file. See docs/architecture.md.
    /// </summary>
    public static class MqttRuntimeConfigLoader {
        public static IEnumerator Load(string relativePath, Action<MqttRuntimeConfig> onLoaded, Action<string> onError) {
            string url = BuildUrl(GetAbsoluteFilePath(relativePath));

            using (UnityWebRequest request = UnityWebRequest.Get(url)) {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success) {
                    onError?.Invoke("MqttRuntimeConfigLoader: failed to read \"" + url + "\": " + request.error);
                    onLoaded?.Invoke(null);
                    yield break;
                }

                string json = request.downloadHandler.text;
                MqttRuntimeConfig config;
                try {
                    config = JsonConvert.DeserializeObject<MqttRuntimeConfig>(json);
                } catch (Exception exception) {
                    onError?.Invoke("MqttRuntimeConfigLoader: failed to parse \"" + url + "\": " + exception.Message);
                    onLoaded?.Invoke(null);
                    yield break;
                }

                onLoaded?.Invoke(config);
            }
        }

        /// <summary> The file's absolute path under StreamingAssets. Only meaningful as a real filesystem path on Standalone/Editor - on Android it's the "jar:file://...!/assets/..." form UnityWebRequest understands, not something System.IO can open directly. </summary>
        public static string GetAbsoluteFilePath(string relativePath) {
            return Application.streamingAssetsPath.TrimEnd('/') + "/" + relativePath.TrimStart('/');
        }

        private static string BuildUrl(string absolutePath) {
#if UNITY_ANDROID && !UNITY_EDITOR
            return absolutePath;
#else
            return "file://" + absolutePath;
#endif
        }

        /// <summary>
        /// Synchronous Editor-only counterpart to Load(...) - for tooling
        /// (context menu buttons, etc.) that isn't already running as a
        /// coroutine. Only safe to call from the Editor/Standalone, where
        /// StreamingAssets is a real filesystem path - not from Android, where
        /// it's packed into the APK and can't be read via System.IO.File.
        /// </summary>
        internal static MqttRuntimeConfig LoadFromStreamingAssets(string relativePath = "DigitalTwinFuturotec/MqttConfig.json") {
            string absolutePath = GetAbsoluteFilePath(relativePath);
            if (!File.Exists(absolutePath)) {
                MqttLog.Error("MqttRuntimeConfigLoader: no config file at \"" + absolutePath + "\".");
                return null;
            }

            try {
                string json = File.ReadAllText(absolutePath);
                return JsonConvert.DeserializeObject<MqttRuntimeConfig>(json);
            } catch (Exception exception) {
                MqttLog.Error("MqttRuntimeConfigLoader: failed to parse \"" + absolutePath + "\": " + exception.Message);
                return null;
            }
        }

        /// <summary>
        /// Writes config as formatted JSON to the StreamingAssets file. Only
        /// works where StreamingAssets is a real writable filesystem path -
        /// Standalone and the Editor. On Android/iOS the file lives inside the
        /// packed app binary and cannot be written back to at runtime; this
        /// logs a clear warning and returns false there instead of failing
        /// silently. Returns true only if the write actually succeeded.
        /// </summary>
        public static bool SaveToStreamingAssets(string relativePath, MqttRuntimeConfig config) {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            MqttLog.Warning("MqttRuntimeConfigLoader: cannot save to \"" + relativePath + "\" - StreamingAssets is packed into the app binary on this platform and isn't writable at runtime.");
            return false;
#else
            string absolutePath = GetAbsoluteFilePath(relativePath);
            try {
                string directory = Path.GetDirectoryName(absolutePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(absolutePath, json);
                return true;
            } catch (Exception exception) {
                MqttLog.Error("MqttRuntimeConfigLoader: failed to save \"" + absolutePath + "\": " + exception.Message);
                return false;
            }
#endif
        }
    }
}
