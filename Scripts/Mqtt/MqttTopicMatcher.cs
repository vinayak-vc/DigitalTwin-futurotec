using System;

namespace ViitorCloud.Games.DigitalTwinFuturotec.Mqtt {
    /// <summary>
    /// Static helper for matching a received topic against a subscription's topic
    /// filter, per the MQTT 3.1.1/5.0 wildcard rules ('+' single-level, '#'
    /// multi-level, '$'-prefixed topics excluded from wildcard-only filters).
    /// </summary>
    public static class MqttTopicMatcher {
        public static bool IsMatch(string topicFilter, string topic) {
            if (string.IsNullOrEmpty(topicFilter) || string.IsNullOrEmpty(topic)) {
                return false;
            }

            string[] filterSegments = topicFilter.Split('/');
            string[] topicSegments = topic.Split('/');

            bool filterStartsWithWildcard = filterSegments[0] == "#" || filterSegments[0] == "+";
            bool topicIsReserved = topicSegments.Length > 0 && topicSegments[0].StartsWith("$", StringComparison.Ordinal);
            if (filterStartsWithWildcard && topicIsReserved) {
                return false;
            }

            int filterIndex = 0;
            int topicIndex = 0;

            while (filterIndex < filterSegments.Length) {
                string filterSegment = filterSegments[filterIndex];

                if (filterSegment == "#") {
                    return true;
                }

                if (topicIndex >= topicSegments.Length) {
                    return false;
                }

                if (filterSegment != "+" && filterSegment != topicSegments[topicIndex]) {
                    return false;
                }

                filterIndex++;
                topicIndex++;
            }

            return topicIndex == topicSegments.Length;
        }
    }
}
