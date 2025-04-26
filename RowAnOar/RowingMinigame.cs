using System;
using UnityEngine;

namespace RowAnOar
{
    public class RowingMinigame
    {
        private Player player;
        private Ship ship;
        private float targetPosition = 0.5f;
        private float currentPosition = 0f;
        private float direction = 1f;
        private float speed = 1f;
        private bool isSuccess = false;
        private float successTimer = 0f;
        private int successStreak = 0;

        private const float TARGET_WINDOW = 0.1f;
        private const float MAX_POSITION = 1f;
        private const float MIN_POSITION = 0f;
        private const float SUCCESS_DURATION = 1f;

        // Network optimization
        private float lastRowingActionTime = 0f;
        private const float ROWING_ACTION_COOLDOWN = 0.2f;

        public RowingMinigame(Player player, Ship ship)
        {
            this.player = player;
            this.ship = ship;
            currentPosition = 0f;
            speed = RowAnOar.Instance.MinigameSuccessSpeed + (successStreak * 0.01f);
            lastRowingActionTime = 0f;
        }

        public float Update()
        {
            currentPosition += direction * speed * Time.deltaTime;

            if (currentPosition >= MAX_POSITION)
            {
                currentPosition = MAX_POSITION;
                direction = -1f;
            }
            else if (currentPosition <= MIN_POSITION)
            {
                currentPosition = MIN_POSITION;
                direction = 1f;
            }

            if (player == Player.m_localPlayer)
            {
                RowAnOar.Instance.UIManager.UpdateRowingUI(currentPosition, targetPosition, successStreak);
            }

            KeyCode rowKey = RowAnOar.Instance.RowingKey;
            if (Input.GetKeyDown(rowKey) && player == Player.m_localPlayer && Time.time - lastRowingActionTime >= ROWING_ACTION_COOLDOWN)
            {
                lastRowingActionTime = Time.time;
                float distance = Math.Abs(currentPosition - targetPosition);

                if (distance < TARGET_WINDOW)
                {
                    isSuccess = true;
                    successTimer = SUCCESS_DURATION;
                    successStreak++;
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"Good rowing! Streak: {successStreak}");
                }
                else
                {
                    isSuccess = false;
                    successStreak = 0;
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Missed! Try again.");
                }
            }

            if (isSuccess)
            {
                successTimer -= Time.deltaTime;
                if (successTimer <= 0)
                    isSuccess = false;

                float multiplier = RowAnOar.Instance.RowingPowerMultiplier;
                float streakBonus = 1f + (successStreak * 0.1f);
                return multiplier * 100f * streakBonus;
            }

            return 0f;
        }
    }
}
