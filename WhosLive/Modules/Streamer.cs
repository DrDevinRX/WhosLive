namespace WhosLive
{
    public class Streamer
    {
        /// <summary>
        /// Discord User ID, PK
        /// </summary>
        public ulong Id { get; set; }

        /// <summary>
        /// Twitch User Name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Twitch Stream URL
        /// </summary>
        public string StreamUrl { get; set; }

        /// <summary>
        /// Game being played
        /// </summary>
        public string Game { get; set; }

        /// <summary>
        /// Twitch Avatar
        /// </summary>
        public string AvatarUrl { get; set; }

        /// <summary>
        /// Twitch stream title
        /// </summary>
        public string StreamTitle { get; set; }

        /// <summary>
        /// Custom message for stream message
        /// </summary>
        public string CustomMessage { get; set; } = null;

        /// <summary>
        /// Is Streaming on Twitch
        /// </summary>
        public bool IsStreaming { get; set; }

        /// <summary>
        /// Discord Message ID to track so message can be modified or deleted
        /// Set to null after deletion
        /// </summary>
        public ulong? MessageId { get; set; }
    }
}
