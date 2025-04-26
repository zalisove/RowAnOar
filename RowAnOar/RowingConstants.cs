using UnityEngine;

namespace RowAnOar
{
    public static class RowingConstants
    {
        // Network constants
        public const float NETWORK_UPDATE_INTERVAL = 0.1f;
        
        // Minigame constants
        public const float TARGET_WINDOW = 0.1f;
        public const float MAX_POSITION = 1f;
        public const float MIN_POSITION = 0f;
        public const float SUCCESS_DURATION = 1f;
        public const float ROWING_ACTION_COOLDOWN = 0.2f;
        
        // Physics constants
        public const float PHYSICS_UPDATE_INTERVAL = 0.05f;
        
        // Common wait times
        public static readonly WaitForSeconds OneSecondWait = new WaitForSeconds(1f);
        public static readonly WaitForSeconds HalfSecondWait = new WaitForSeconds(0.5f);
        public static readonly WaitForSeconds NetworkWait = new WaitForSeconds(NETWORK_UPDATE_INTERVAL);
    }
}
