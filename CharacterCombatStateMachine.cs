using UdonSharp;
using UnityEngine;
using VRC.Udon;

public enum CharacterCombatState
{
    Idle,
    SwordCombat,
    GreatbowAiming
}

[UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
public class CharacterCombatStateMachine : UdonSharpBehaviour
{
    [Header("State")]
    [UdonSynced]
    public CharacterCombatState currentState = CharacterCombatState.Idle;

    [Header("Input")]
    public KeyCode weaponSwitchKey = KeyCode.Q;
    private float weaponSwitchCooldown = 0.3f;
    private float lastWeaponSwitchTime = 0f;

    [Header("Animator")]
    public Animator characterAnimator;
    public string stateParameterName = "CombatState";
    public string weaponTypeParameterName = "WeaponType";

    [Header("Weapon Visuals")]
    public GameObject swordVisual;
    public GameObject greatbowVisual;

    [Header("Audio")]
    public AudioClip weaponDrawSFX;
    public AudioClip weaponSwitchSFX;
    private AudioSource audioSource;

    [Header("VRC Settings")]
    public bool syncStateAcrossNetwork = true;

    private CharacterCombatState previousState;

    private void Start()
    {
        // Cache the animator if not assigned
        if (characterAnimator == null)
        {
            characterAnimator = GetComponent<Animator>();
        }

        // Try to get or create AudioSource
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        previousState = currentState;
        ApplyState(currentState, true);
    }

    private void Update()
    {
        // Only allow local input
        if (!Networking.IsOwner(gameObject))
            return;

        // Check for weapon switch input with cooldown
        if (Input.GetKeyDown(weaponSwitchKey))
        {
            if (Time.time >= lastWeaponSwitchTime + weaponSwitchCooldown)
            {
                lastWeaponSwitchTime = Time.time;
                RequestCycleState();
            }
        }
    }

    public void RequestCycleState()
    {
        if (syncStateAcrossNetwork)
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.AllBuffered, nameof(CycleState));
        }
        else
        {
            CycleState();
        }
    }

    public void CycleState()
    {
        previousState = currentState;

        switch (currentState)
        {
            case CharacterCombatState.Idle:
                currentState = CharacterCombatState.SwordCombat;
                break;

            case CharacterCombatState.SwordCombat:
                currentState = CharacterCombatState.GreatbowAiming;
                break;

            case CharacterCombatState.GreatbowAiming:
                currentState = CharacterCombatState.Idle;
                break;
        }

        ApplyState(currentState, false);
    }

    private void ApplyState(CharacterCombatState newState, bool isInitializing)
    {
        currentState = newState;

        // Update Animator Parameters
        UpdateAnimator(newState);

        // Update Weapon Visuals
        UpdateWeaponVisuals(newState);

        // Play Audio
        if (!isInitializing)
        {
            PlayWeaponSwitchAudio(newState);
        }

        // Debug Log
        Debug.Log($"[Combat State Machine] Switched to: {newState}");
    }

    private void UpdateAnimator(CharacterCombatState newState)
    {
        if (characterAnimator == null)
            return;

        // Set the state parameter (integer value 0, 1, 2)
        int stateValue = (int)newState;
        characterAnimator.SetInteger(stateParameterName, stateValue);

        // Also set weapon type for more granular control
        switch (newState)
        {
            case CharacterCombatState.Idle:
                characterAnimator.SetInteger(weaponTypeParameterName, 0); // Unarmed
                break;

            case CharacterCombatState.SwordCombat:
                characterAnimator.SetInteger(weaponTypeParameterName, 1); // Sword
                break;

            case CharacterCombatState.GreatbowAiming:
                characterAnimator.SetInteger(weaponTypeParameterName, 2); // Greatbow
                break;
        }

        // Trigger state transition animation
        string triggerName = $"Enter{newState}";
        if (characterAnimator.parameters != null)
        {
            foreach (AnimatorParameter param in characterAnimator.parameters)
            {
                if (param.name == triggerName && param.type == AnimatorControllerParameterType.Trigger)
                {
                    characterAnimator.SetTrigger(triggerName);
                }
            }
        }
    }

    private void UpdateWeaponVisuals(CharacterCombatState newState)
    {
        // Disable all weapons first
        if (swordVisual != null)
            swordVisual.SetActive(false);

        if (greatbowVisual != null)
            greatbowVisual.SetActive(false);

        // Enable the appropriate weapon
        switch (newState)
        {
            case CharacterCombatState.Idle:
                // No weapon active in Idle state
                break;

            case CharacterCombatState.SwordCombat:
                if (swordVisual != null)
                    swordVisual.SetActive(true);
                break;

            case CharacterCombatState.GreatbowAiming:
                if (greatbowVisual != null)
                    greatbowVisual.SetActive(true);
                break;
        }
    }

    private void PlayWeaponSwitchAudio(CharacterCombatState newState)
    {
        if (audioSource == null)
            return;

        AudioClip clipToPlay = null;

        if (newState != CharacterCombatState.Idle && weaponSwitchSFX != null)
        {
            clipToPlay = weaponSwitchSFX;
        }
        else if (newState != CharacterCombatState.Idle && weaponDrawSFX != null)
        {
            clipToPlay = weaponDrawSFX;
        }

        if (clipToPlay != null)
        {
            audioSource.PlayOneShot(clipToPlay);
        }
    }

    // Public methods for external systems to check state
    public bool IsInState(CharacterCombatState state)
    {
        return currentState == state;
    }

    public bool CanAttack()
    {
        return currentState == CharacterCombatState.SwordCombat || currentState == CharacterCombatState.GreatbowAiming;
    }

    public CharacterCombatState GetCurrentState()
    {
        return currentState;
    }

    public void SetStateDirectly(CharacterCombatState newState)
    {
        if (Networking.IsOwner(gameObject))
        {
            previousState = currentState;
            currentState = newState;
            ApplyState(newState, false);

            if (syncStateAcrossNetwork)
            {
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.AllBuffered, nameof(OnNetworkStateUpdate));
            }
        }
    }

    public void OnNetworkStateUpdate()
    {
        // This is called when the state syncs from network
        ApplyState(currentState, false);
    }
}
